using AvaloniaEdit.Document;
using happy_agent_coding.Services;
using happy_agent_coding.ViewModels;
using VisualizedAgentCoding.Application.Editor.ObjectScripts;
using VisualizedAgentCoding.Application.Ports;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Domain.Projects;
using VisualizedAgentCoding.Infrastructure.Projects;
using VisualizedAgentCoding.Infrastructure.Python;

namespace VisualizedAgentCoding.App.Tests;

public sealed class ScriptUpgradeTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "script-upgrade-" + Guid.NewGuid().ToString("N"));
    [Theory]
    [InlineData(ProjectScriptKind.Object, "\n")]
    [InlineData(ProjectScriptKind.Backdrop, "\r\n")]
    public async Task PreviewPreservesRawUserBodyAndDoesNotWriteDisk(ProjectScriptKind kind, string newline)
    {
        var body = "    # 用户注释\n    def walk(self):\n        self.text = '你好'\n    value = 2\n".Replace("\n", newline);
        var (project, script, source) = Create(kind, newline, body);
        await using var bridge = new PythonProcessBridge();
        var documents = new ObjectScriptDocumentService(bridge);
        var original = await documents.InspectAsync(project, script.Id, 2, source);
        var updated = await new ObjectScriptUpgradeService(documents).PrepareAsync(project, original, 2);
        Assert.Equal(3, updated.TemplateVersion);
        var members = updated.Regions!.Single(r => r.Kind == "members").Span;
        Assert.Equal(body, updated.FullSource.Substring(members.Offset, members.Length));
        Assert.Equal(new[] { "members", "start" }, updated.Regions!.Select(r => r.Kind));
        Assert.Contains(ProjectScriptTemplates.UserPlaceholderStatement, updated.FullSource);
        Assert.Equal(source, File.ReadAllText(project.ToAbsolutePath(script.Path)));
    }

    [Fact]
    public async Task UpgradeAndUndoAcrossSavedVersionsUpdateBadgesAndPreserveConflictProtection()
    {
        var (project, script, source) = Create(ProjectScriptKind.Object, "\n", "    value = 1\n");
        await using var bridge = new PythonProcessBridge();
        await using var vm = new MainViewModel(new Settings(), new InMemoryRecentProjectsStore(), new ProjectSession(), bridge);
        await vm.OpenProject(root);
        Assert.True(Assert.Single(vm.ObjectScripts).NeedsTemplateUpgrade);
        await vm.OpenScriptFromExplorerCommand.ExecuteAsync(Assert.Single(vm.ObjectScripts));
        var original = (await vm.InspectEditorProtectionAsync(CancellationToken.None))!;
        var single = new ObjectScriptProjection(original);
        // An editor buffer need not include its hidden separator newline.
        single.Update(single.ViewSource.TrimEnd('\n'));
        vm.SetEditorProjection(single);
        vm.PythonCode = single.FullSource;
        var migrated = await vm.PrepareScriptUpgradeAsync(CancellationToken.None);
        var text = new TextDocument(single.ViewSource);
        IEditorProjection current = single;
        var restoring = false;
        MultiRegionReadOnlyProvider? provider = null;
        text.TextChanged += (_, _) =>
        {
            if (restoring) return;
            if (current is ObjectScriptProjection one) one.Update(text.Text);
            else ((MultiRegionProjection)current).UpdateRegions(provider!.ReadBodies());
            vm.PythonCode = current.FullSource;
        };
        var before = new StructuralEditorState(single.Document, 0, 0, 0, text.Text);
        var after = new StructuralEditorState(migrated, 0, 0, 0);
        StructuralEditorHistory.Apply(text, before, after, () => restoring = true, state =>
        {
            current = EditorProjection.Create(state.Document);
            if (current is ObjectScriptProjection one && state.ViewSource is not null) one.Update(state.ViewSource);
            provider = current is MultiRegionProjection multi ? new(text, multi.EditableRegions) : null;
            vm.RestoreEditorStructure(current);
            restoring = false;
        });
        Assert.False(Assert.Single(vm.ObjectScripts).NeedsTemplateUpgrade);
        text.SetOwnerThread(null!);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal("已保存", vm.EditorState);
        Assert.Equal(migrated.FullSource, File.ReadAllText(project.ToAbsolutePath(script.Path)));
        text.SetOwnerThread(Thread.CurrentThread);
        text.UndoStack.Undo();
        Assert.Equal(before.ViewSource, text.Text);
        Assert.Equal(source, vm.PythonCode);
        Assert.True(Assert.Single(vm.ObjectScripts).NeedsTemplateUpgrade);
        text.SetOwnerThread(null!);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal("已保存", vm.EditorState);
        Assert.Equal(source, File.ReadAllText(project.ToAbsolutePath(script.Path)));
        text.SetOwnerThread(Thread.CurrentThread);
        text.UndoStack.Redo();
        Assert.False(Assert.Single(vm.ObjectScripts).NeedsTemplateUpgrade);
        Assert.Equal(migrated.FullSource, vm.PythonCode);
        var external = source + "# external\n";
        File.WriteAllText(project.ToAbsolutePath(script.Path), external);
        text.SetOwnerThread(null!);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Contains("外部修改", vm.EditorState);
        Assert.Equal(external, File.ReadAllText(project.ToAbsolutePath(script.Path)));
    }

    [Fact]
    public async Task InvalidAndLatestTemplatesCannotBeUpgraded()
    {
        var (project, script, source) = Create(ProjectScriptKind.Object, "\n", "");
        await using var bridge = new PythonProcessBridge();
        var documents = new ObjectScriptDocumentService(bridge);
        var service = new ObjectScriptUpgradeService(documents);
        var invalid = await documents.InspectAsync(project, script.Id, 1, source.Replace("# vac:user-code-end", "# broken-end"));
        await Assert.ThrowsAsync<ProjectOperationException>(() => service.PrepareAsync(project, invalid, 1));
        var latest = await documents.InspectAsync(project, script.Id, 2, ProjectScriptTemplates.CreateWithImports(script.Kind, "Hero"));
        await Assert.ThrowsAsync<ProjectOperationException>(() => service.PrepareAsync(project, latest, 2));
        Assert.Equal(source, File.ReadAllText(project.ToAbsolutePath(script.Path)));
    }

    [Theory]
    [InlineData("# vac:template-version=1\n", 1, true)]
    [InlineData("\uFEFF# ordinary comment\n# vac:template-version=2\r\n", 2, true)]
    [InlineData("\"\"\"\n# vac:template-version=1\n\"\"\"", null, false)]
    [InlineData("# vac:template-version=99\n", 99, false)]
    [InlineData("# vac:template-version=bad\n", null, false)]
    public void OnlyKnownOlderObjectHeadersHaveUpgradeDecoration(string source, int? version, bool warning)
    {
        Assert.Equal(version, ObjectScriptTemplateVersion.ReadHeader(source));
        Assert.Equal(warning, new ProjectScriptItem("id", "hero", "hero.py", ProjectScriptKind.Object, TemplateVersion: version).NeedsTemplateUpgrade);
        Assert.False(new ProjectScriptItem("id", "user", "user.py", ProjectScriptKind.User, TemplateVersion: version).NeedsTemplateUpgrade);
    }

    private (ProjectWorkspace, ProjectScriptEntry, string) Create(ProjectScriptKind kind, string newline, string body)
    {
        var projects = new ProjectService(new JsonProjectStore());
        var created = projects.CreateScript(projects.Create(root, "Demo"), kind, "hero");
        var source = ProjectScriptTemplates.Create(kind, "Hero").Replace("\n", newline)
            .Replace("    # vac:user-code-end", body + "    # vac:user-code-end");
        File.WriteAllText(created.Project.ToAbsolutePath(created.Script.Path), source);
        return (created.Project, created.Script, source);
    }
    private sealed class Settings : ISettingsStore
    {
        public AppSettings Load() => new();
        public void Save(AppSettings settings) { }
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
