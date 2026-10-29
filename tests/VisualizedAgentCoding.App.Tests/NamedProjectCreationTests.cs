using happy_agent_coding.Services;
using happy_agent_coding.ViewModels;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Domain.Projects;

namespace VisualizedAgentCoding.App.Tests;

public sealed class NamedProjectCreationTests : IDisposable
{
    private readonly string parent = Path.Combine(Path.GetTempPath(), "vac-parent-" + Guid.NewGuid().ToString("N"));
    public NamedProjectCreationTests() => Directory.CreateDirectory(parent);

    [Fact]
    public async Task CreatesNamedChildAndRecordsChildAsProjectRoot()
    {
        var marker = Path.Combine(parent, "keep.txt");
        File.WriteAllText(marker, "parent content");
        var recents = new InMemoryRecentProjectsStore();
        await using var vm = new MainViewModel(new JsonSettingsStore(Path.Combine(parent, "settings.json")), recents, new ProjectSession());
        await vm.CreateProjectInDirectory(parent, "我的项目 1");
        var root = Path.Combine(parent, "我的项目 1");
        Assert.Equal(root, vm.ProjectRootPath);
        Assert.Equal("我的项目 1", vm.ProjectName);
        Assert.True(File.Exists(Path.Combine(root, "project.json")));
        Assert.True(File.Exists(Path.Combine(root, "scripts", "main.py")));
        Assert.False(File.Exists(Path.Combine(parent, "project.json")));
        Assert.False(Directory.Exists(Path.Combine(parent, "scripts")));
        Assert.Equal("parent content", File.ReadAllText(marker));
        Assert.Equal(root, Assert.Single(recents.Projects).Path);
        await vm.CloseProject();
        await vm.OpenProject(root);
        Assert.Equal(root, vm.ProjectRootPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("..")]
    [InlineData("../escape")]
    [InlineData("foo/bar")]
    [InlineData("foo\\bar")]
    [InlineData("C:\\escape")]
    [InlineData("name.")]
    [InlineData("CON")]
    [InlineData("nul.txt")]
    [InlineData("LPT1")]
    [InlineData("bad:name")]
    public void InvalidNamesNeverCreateAnything(string name)
    {
        IProjectSession session = new ProjectSession();
        Assert.Throws<ProjectFormatException>(() => session.CreateInDirectory(parent, name));
        Assert.Empty(Directory.EnumerateFileSystemEntries(parent));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExistingChildIsNotReusedOrOverwritten(bool directory)
    {
        var path = Path.Combine(parent, "existing");
        if (directory) Directory.CreateDirectory(path);
        else File.WriteAllText(path, "keep");
        IProjectSession session = new ProjectSession();
        Assert.Throws<ProjectFormatException>(() => session.CreateInDirectory(parent, "existing"));
        if (directory) Assert.Empty(Directory.EnumerateFileSystemEntries(path));
        else Assert.Equal("keep", File.ReadAllText(path));
    }

    [Fact]
    public void MissingParentIsRejected()
    {
        IProjectSession session = new ProjectSession();
        Assert.Throws<ProjectFormatException>(() => session.CreateInDirectory(Path.Combine(parent, "missing"), "demo"));
        Assert.Empty(Directory.EnumerateFileSystemEntries(parent));
    }

    public void Dispose() { if (Directory.Exists(parent)) Directory.Delete(parent, true); }
}
