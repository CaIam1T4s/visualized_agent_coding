using VisualizedAgentCoding.Application.Editor.ObjectScripts;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Domain.Projects;
using VisualizedAgentCoding.Infrastructure.Projects;
using VisualizedAgentCoding.Infrastructure.Python;
using happy_agent_coding.ViewModels;

namespace VisualizedAgentCoding.App.Tests;

public sealed class ScriptRenameTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "vac-rename-" + Guid.NewGuid().ToString("N"));
    private readonly JsonProjectStore store = new();
    private readonly PythonProcessBridge bridge = new();
    private ProjectWorkspace project = null!;

    public Task InitializeAsync()
    {
        project = new ProjectService(store).Create(root);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await bridge.DisposeAsync();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    [Theory]
    [InlineData(ProjectScriptKind.Object, "scripts/objects/hero.py", "scripts/objects/fire_hero.py", "Hero", "FireHero")]
    [InlineData(ProjectScriptKind.Backdrop, "scripts/backdrops/hero.py", "scripts/backdrops/fire_hero.py", "Hero", "FireHero")]
    public async Task ObjectAndBackdropRenameSynchronizesClassAndKeepsIdentity(
        ProjectScriptKind kind, string oldPath, string newPath, string oldClass, string newClass)
    {
        var created = new ProjectService(store).CreateScript(project, kind, "hero");
        project = created.Project;
        var source = store.ReadScript(root, created.Script.Path);
        Assert.Contains($"class {oldClass}(", source);
        var rename = new ScriptRenameService(bridge, store);
        var plan = await rename.PlanAsync(project, created.Script.Id, "fire_hero", 1, []);
        Assert.Contains(plan.Changes, change => change.After.Contains($"class {newClass}(", StringComparison.Ordinal));
        var preview = new ScriptRenamePreviewItem(plan.Changes.Single());
        Assert.True(preview.HasClassChange);
        Assert.False(preview.HasVisibleChanges);
        Assert.Contains($"{oldClass} → {newClass}", preview.ClassChange);
        var updated = rename.Apply(plan);
        var renamed = Assert.Single(updated.Manifest.Scripts);
        Assert.Equal(newPath, renamed.Path);
        Assert.Equal(created.Script.Id, renamed.Id);
        Assert.Equal(created.Script.ObjectId, renamed.ObjectId);
        Assert.False(store.ScriptExists(root, oldPath));
        var rewritten = store.ReadScript(root, newPath);
        Assert.Contains($"class {newClass}(", rewritten);
        var inspected = await new ObjectScriptDocumentService(bridge).InspectAsync(updated, renamed.Id, 2, rewritten);
        Assert.Equal(ObjectScriptStatus.Recognized, inspected.Status);
    }
}
