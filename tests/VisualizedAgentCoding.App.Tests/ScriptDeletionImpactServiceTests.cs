using VisualizedAgentCoding.Application.Ports;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Application.Python;
using VisualizedAgentCoding.Infrastructure.Projects;
using VisualizedAgentCoding.Infrastructure.Python;

namespace VisualizedAgentCoding.App.Tests;

public sealed class ScriptDeletionImpactServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "project-references-" + Guid.NewGuid().ToString("N"));
    private readonly JsonProjectStore store = new();
    private readonly ProjectWorkspace project;
    private string HeroUri => new Uri(Path.Combine(root, "scripts/user/hero.py")).AbsoluteUri;
    private string UseUri => new Uri(Path.Combine(root, "scripts/user/use.py")).AbsoluteUri;

    public ScriptDeletionImpactServiceTests()
    {
        var projects = new ProjectService(store);
        var first = projects.CreateScript(projects.Create(root, "Demo"), VisualizedAgentCoding.Domain.Projects.ProjectScriptKind.User, "hero");
        project = projects.CreateScript(first.Project, VisualizedAgentCoding.Domain.Projects.ProjectScriptKind.User, "use").Project;
        store.WriteScript(root, "scripts/user/hero.py", "class SavedName: pass\n");
        store.WriteScript(root, "scripts/user/use.py", "from hero import Hero as H\nvalue = H()\n");
    }

    [Fact]
    public async Task ModuleImpactUsesUnsavedImporterAndMapsLocationsBack()
    {
        await using var bridge = new PythonProcessBridge();
        var query = new ScriptDeletionImpactService(bridge, store);
        var result = await query.AnalyzeAsync(project, project.Manifest.Scripts.Single(s => s.Path == "scripts/user/hero.py").Id, 11,
            new[] { new PythonSourceDocument(UseUri, 11, "import hero as h\n") });
        Assert.Equal(DeletionImpactState.Complete, result.State);
        Assert.Equal(UseUri, Assert.Single(result.References).Uri);
        Assert.Contains("from hero", store.ReadScript(root, "scripts/user/use.py"));
        var empty = await query.AnalyzeAsync(project, project.Manifest.Scripts.Single(s => s.Path == "scripts/user/hero.py").Id, 12,
            new[] { new PythonSourceDocument(UseUri, 12, "") });
        Assert.Empty(empty.References);
    }

    [Fact]
    public async Task RepeatedUnsavedAliasCallsAreAllIncluded()
    {
        await using var bridge = new PythonProcessBridge();
        var result = await new ScriptDeletionImpactService(bridge, store).AnalyzeAsync(project,
            project.Manifest.Scripts.Single(s => s.Path == "scripts/user/hero.py").Id, 15,
            [new(HeroUri, 15, "def greet(): pass\n"),
             new(UseUri, 15, "from hero import greet as welcome\n" + string.Concat(Enumerable.Repeat("welcome()\n", 5)))]);
        Assert.Equal(DeletionImpactState.Complete, result.State);
        Assert.Equal(1, result.AffectedScriptCount);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, result.References.Select(r => r.Range.Line));
    }

    [Fact]
    public async Task BackdropDeletionReportsStringCallsAndSwitchBindings()
    {
        var projects = new ProjectService(store);
        var day = projects.CreateScript(project, VisualizedAgentCoding.Domain.Projects.ProjectScriptKind.Backdrop, "day");
        var night = projects.CreateScript(day.Project, VisualizedAgentCoding.Domain.Projects.ProjectScriptKind.Backdrop, "night");
        var objectId = night.Script.ObjectId!;
        store.WriteScript(root, "scripts/user/use.py",
            "from vac_runtime import switch_backdrop\nswitch_backdrop('night')\n# backdrop-switch:" + objectId + "\n");
        var result = await new ScriptDeletionImpactService(new EmptyBridge(), store)
            .AnalyzeAsync(night.Project, night.Script.Id, 1, []);
        Assert.Equal(DeletionImpactState.Complete, result.State);
        Assert.Contains(result.References, item => item.Uri == UseUri && item.Range.Line == 2
            && item.Name == "switch_backdrop");
        Assert.Contains(result.References, item => item.Uri == UseUri && item.Range.Line == 3
            && item.Name == "backdrop-switch");
        Assert.True(result.RequiresOverride);
    }

    [Fact]
    public async Task FailuresRemainUnknownAndRequireOverride()
    {
        var result = await new ScriptDeletionImpactService(new StubBridge(), store)
            .AnalyzeAsync(project, project.Manifest.Scripts[0].Id, 4, []);
        Assert.Equal(DeletionImpactState.Failed, result.State);
        Assert.True(result.RequiresOverride);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task IncompleteResultsAndStaleVersionsCannotAppearComplete()
    {
        var bridge = new StubBridge { Incomplete = true };
        var result = await new ScriptDeletionImpactService(bridge, store)
            .AnalyzeAsync(project, project.Manifest.Scripts[0].Id, 4, []);
        Assert.Equal(DeletionImpactState.Incomplete, result.State);
        Assert.True(result.RequiresOverride);
        Assert.Equal(DeletionImpactState.Stale, result.ForCurrent(project, 5).State);
        Assert.Same(result, result.ForCurrent(project, 4));
    }

    [Fact]
    public async Task CancellationPropagatesRatherThanBecomingFailure()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ScriptDeletionImpactService(new StubBridge(), store)
                .AnalyzeAsync(project, project.Manifest.Scripts[0].Id, 4, [], cancellation.Token));
    }

    private sealed class EmptyBridge : IPythonBridge
    {
        public Task<PythonReferencesResult> FindModuleDependenciesAsync(PythonModuleDependenciesRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new PythonReferencesResult(request.Uri, request.Version, [], [], false));
        public Task<PythonReferencesResult> FindReferencesAsync(PythonReferencesRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PythonHelperInfo> InitializeAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PythonParseResult> ParseAsync(PythonParseRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StubBridge : IPythonBridge
    {
        public bool Incomplete { get; init; }
        public Task<PythonReferencesResult> FindModuleDependenciesAsync(PythonModuleDependenciesRequest request, CancellationToken cancellationToken = default)
            => Incomplete ? Task.FromResult(new PythonReferencesResult(request.Uri, request.Version, [], [], true))
                : throw new IOException("helper unavailable");
        public Task<PythonReferencesResult> FindReferencesAsync(PythonReferencesRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PythonHelperInfo> InitializeAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PythonParseResult> ParseAsync(PythonParseRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public void Dispose() => Directory.Delete(root, true);
}
