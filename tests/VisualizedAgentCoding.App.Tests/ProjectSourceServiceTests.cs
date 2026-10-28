using VisualizedAgentCoding.Application.Ports;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Application.Python;
using VisualizedAgentCoding.Infrastructure.Projects;
using VisualizedAgentCoding.Infrastructure.Python;

namespace VisualizedAgentCoding.App.Tests;

public sealed class ProjectSourceServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "project-references-" + Guid.NewGuid().ToString("N"));
    private readonly JsonProjectStore store = new();
    private readonly ProjectWorkspace project;
    private string HeroUri => new Uri(Path.Combine(root, "scripts/user/hero.py")).AbsoluteUri;
    private string UseUri => new Uri(Path.Combine(root, "scripts/user/use.py")).AbsoluteUri;

    public ProjectSourceServiceTests()
    {
        var projects = new ProjectService(store);
        var first = projects.CreateScript(projects.Create(root, "Demo"), VisualizedAgentCoding.Domain.Projects.ProjectScriptKind.User, "hero");
        project = projects.CreateScript(first.Project, VisualizedAgentCoding.Domain.Projects.ProjectScriptKind.User, "use").Project;
        store.WriteScript(root, "scripts/user/hero.py", "class SavedName: pass\n");
        store.WriteScript(root, "scripts/user/use.py", "from hero import Hero as H\nvalue = H()\n");
    }

    [Fact]
    public void OverlayWinsIncludingEmptyBuffersWithoutSaving()
    {
        var documents = new ProjectSourceService(store).Collect(project,
            [new(HeroUri, 9, "class Hero: pass\n"), new(UseUri, 2, "")], CancellationToken.None);
        Assert.Equal("class Hero: pass\n", documents.Single(d => d.Uri == HeroUri).Source);
        Assert.Equal("", documents.Single(d => d.Uri == UseUri).Source);
        Assert.Contains(documents, d => d.Uri.EndsWith("/scripts/main.py"));
        Assert.Equal("class SavedName: pass\n", store.ReadScript(root, "scripts/user/hero.py"));
        Assert.Contains("H()", store.ReadScript(root, "scripts/user/use.py"));
    }

    [Fact]
    public void OutsideProjectOverlayIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new ProjectSourceService(store).Collect(project,
            [new(new Uri(Path.Combine(Path.GetTempPath(), "outside.py")).AbsoluteUri, 1, "")], CancellationToken.None));
    }

    public void Dispose() => Directory.Delete(root, true);
}
