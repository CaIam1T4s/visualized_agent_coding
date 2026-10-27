using VisualizedAgentCoding.Application.Ports;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Application.Python;
using VisualizedAgentCoding.Infrastructure.Projects;
using VisualizedAgentCoding.Infrastructure.Python;

namespace VisualizedAgentCoding.App.Tests;

public sealed class ProjectReferenceServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "project-references-" + Guid.NewGuid().ToString("N"));
    private readonly JsonProjectStore store = new();
    private readonly ProjectWorkspace project;
    private string HeroUri => new Uri(Path.Combine(root, "scripts/user/hero.py")).AbsoluteUri;
    private string UseUri => new Uri(Path.Combine(root, "scripts/user/use.py")).AbsoluteUri;

    public ProjectReferenceServiceTests()
    {
        var projects = new ProjectService(store);
        var first = projects.CreateScript(projects.Create(root, "Demo"), VisualizedAgentCoding.Domain.Projects.ProjectScriptKind.User, "hero");
        project = projects.CreateScript(first.Project, VisualizedAgentCoding.Domain.Projects.ProjectScriptKind.User, "use").Project;
        store.WriteScript(root, "scripts/user/hero.py", "class SavedName: pass\n");
        store.WriteScript(root, "scripts/user/use.py", "from hero import Hero as H\nvalue = H()\n");
    }

    [Fact]
    public async Task OverlayWinsForAllOpenDocumentsIncludingEmptyBuffersWithoutSaving()
    {
        var bridge = new CapturingBridge();
        var query = new ProjectReferenceService(bridge, store);
        await query.FindAsync(project, HeroUri, 9, 1, 8,
            new[] { new PythonSourceDocument(HeroUri, 9, "class Hero: pass\n"), new PythonSourceDocument(UseUri, 2, "") });
        Assert.Equal("class Hero: pass\n", bridge.Request!.Documents.Single(d => d.Uri == HeroUri).Source);
        Assert.Equal("", bridge.Request.Documents.Single(d => d.Uri == UseUri).Source);
        Assert.Contains(bridge.Request.Documents, d => d.Uri.EndsWith("/scripts/main.py"));
        Assert.Equal("class SavedName: pass\n", store.ReadScript(root, "scripts/user/hero.py"));
        Assert.Contains("H()", store.ReadScript(root, "scripts/user/use.py"));
    }

    [Fact]
    public async Task RealHelperFindsCrossFileReferencesAgainstUnsavedDefinition()
    {
        await using var bridge = new PythonProcessBridge();
        var query = new ProjectReferenceService(bridge, store);
        var result = await query.FindAsync(project, HeroUri, 9, 1, 8,
            new[] { new PythonSourceDocument(HeroUri, 9, "class Hero: pass\n") });
        Assert.Equal(9, result.Version);
        Assert.Equal(HeroUri, result.Uri);
        Assert.Equal(4, result.References.Length);
        Assert.Contains(result.References, reference => reference.Uri == UseUri && reference.Range.Line == 2 && reference.Name == "H");
        Assert.All(result.References, reference => Assert.DoesNotContain("vac-analysis-", reference.Uri));
        Assert.Equal("class SavedName: pass\n", store.ReadScript(root, "scripts/user/hero.py"));
    }

    [Fact]
    public async Task RealHelperUsesUnsavedContentOfReferencingDocument()
    {
        await using var bridge = new PythonProcessBridge();
        var query = new ProjectReferenceService(bridge, store);
        var result = await query.FindAsync(project, HeroUri, 10, 1, 8,
            new[] { new PythonSourceDocument(HeroUri, 10, "class Hero: pass\n"), new PythonSourceDocument(UseUri, 4, "") });
        Assert.Single(result.References);
        Assert.Equal(HeroUri, result.References[0].Uri);
    }

    [Fact]
    public async Task DocumentsOutsideProjectAreRejected()
    {
        var bridge = new CapturingBridge();
        var query = new ProjectReferenceService(bridge, store);
        await Assert.ThrowsAsync<ArgumentException>(() => query.FindAsync(project, HeroUri, 1, 1, 1,
            new[] { new PythonSourceDocument(new Uri(Path.Combine(Path.GetTempPath(), "outside.py")).AbsoluteUri, 1, "") }));
        Assert.Null(bridge.Request);
    }

    public void Dispose() => Directory.Delete(root, true);

    private sealed class CapturingBridge : IPythonBridge
    {
        public PythonReferencesRequest? Request { get; private set; }
        public Task<PythonHelperInfo> InitializeAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PythonParseResult> ParseAsync(PythonParseRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PythonReferencesResult> FindReferencesAsync(PythonReferencesRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            return Task.FromResult(new PythonReferencesResult(request.Uri, request.Version, [], [], false));
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
