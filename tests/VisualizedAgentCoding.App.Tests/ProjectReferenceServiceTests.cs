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

    public void Dispose() => Directory.Delete(root, true);
}
