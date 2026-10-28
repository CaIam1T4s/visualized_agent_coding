using System.Text.Json;
using VisualizedAgentCoding.Application.Ports;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Application.Python;
using VisualizedAgentCoding.Domain.Projects;
using VisualizedAgentCoding.Infrastructure.Projects;
using VisualizedAgentCoding.Infrastructure.Python;

namespace VisualizedAgentCoding.App.Tests;

public sealed class ScriptRenameServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "rename-plan-" + Guid.NewGuid().ToString("N"));
    private readonly FaultStore store = new();
    private readonly ProjectWorkspace project;
    private readonly string targetId;
    private string UriFor(string file) => new Uri(Path.Combine(root, "scripts/user/" + file + ".py")).AbsoluteUri;
    public ScriptRenameServiceTests()
    {
        var projects = new ProjectService(store);
        var first = projects.CreateScript(projects.Create(root, "Demo"), ProjectScriptKind.User, "hello");
        targetId = first.Script.Id;
        project = projects.CreateScript(first.Project, ProjectScriptKind.User, "use").Project;
        store.WriteScript(root, "scripts/user/hello.py", "def greet(): pass\n");
        store.WriteScript(root, "scripts/user/use.py", "from hello import greet as welcome\nwelcome()\n");
    }

    [Fact]
    public async Task PreviewUsesBufferWithoutSavingAndApplyKeepsStableId()
    {
        await using var bridge = new PythonProcessBridge();
        var service = new ScriptRenameService(bridge, store);
        var overlay = "from hello import greet as welcome\nwelcome()\nwelcome()\n";
        var plan = await service.PlanAsync(project, targetId, "greetings", 2, [new(UriFor("use"), 2, overlay)]);
        Assert.Single(plan.Changes);
        Assert.DoesNotContain("greetings", store.ReadScript(root, "scripts/user/use.py"));
        Assert.Equal(2, store.ReadScript(root, "scripts/user/use.py").Split('\n').Length - 1);
        var renamed = service.Apply(plan);
        Assert.Equal("scripts/user/greetings.py", renamed.FindScript(targetId)!.Path);
        Assert.False(store.ScriptExists(root, "scripts/user/hello.py"));
        Assert.Equal(overlay.Replace("from hello", "from greetings"), store.ReadScript(root, "scripts/user/use.py"));
        Assert.Equal("def greet(): pass\n", store.ReadScript(root, "scripts/user/greetings.py"));
    }

    [Fact]
    public async Task ExternalSourceChangeRejectsApplyWithoutOverwriting()
    {
        await using var bridge = new PythonProcessBridge();
        var service = new ScriptRenameService(bridge, store);
        var plan = await service.PlanAsync(project, targetId, "greetings", 1, []);
        store.WriteScript(root, "scripts/user/use.py", "external = 1\n");
        Assert.Throws<ProjectOperationException>(() => service.Apply(plan));
        Assert.Equal("external = 1\n", store.ReadScript(root, "scripts/user/use.py"));
        Assert.True(store.ScriptExists(root, "scripts/user/hello.py"));
    }

    [Fact]
    public async Task AddedScriptInvalidatesPreview()
    {
        await using var bridge = new PythonProcessBridge();
        var service = new ScriptRenameService(bridge, store);
        var plan = await service.PlanAsync(project, targetId, "greetings", 1, []);
        store.WriteScript(root, "scripts/user/external.py", "import hello\n");
        Assert.Throws<ProjectOperationException>(() => service.Apply(plan));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WriteOrManifestFailureRestoresAllOriginals(bool failManifest)
    {
        await using var bridge = new PythonProcessBridge();
        var service = new ScriptRenameService(bridge, store);
        var manifest = JsonSerializer.Serialize(store.ReadManifest(root));
        var before = store.ReadScript(root, "scripts/user/use.py");
        var plan = await service.PlanAsync(project, targetId, "greetings", 1,
            [new(UriFor("hello"), 1, "def greet(): return 42\n")]);
        if (failManifest) store.FailCommit = true; else store.FailWrite = true;
        Assert.Throws<ProjectOperationException>(() => service.Apply(plan));
        Assert.Equal(before, store.ReadScript(root, "scripts/user/use.py"));
        Assert.Equal("def greet(): pass\n", store.ReadScript(root, "scripts/user/hello.py"));
        Assert.Equal(manifest, JsonSerializer.Serialize(store.ReadManifest(root)));
        Assert.False(store.ScriptExists(root, "scripts/user/greetings.py"));
    }

    [Fact]
    public async Task DynamicImportAndDuplicateNameBlockPlanning()
    {
        await using var bridge = new PythonProcessBridge();
        var service = new ScriptRenameService(bridge, store);
        await Assert.ThrowsAsync<ProjectOperationException>(() => service.PlanAsync(project, targetId, "use", 1, []));
        store.WriteScript(root, "scripts/user/use.py", "__import__('hello')\n");
        await Assert.ThrowsAsync<ProjectOperationException>(() => service.PlanAsync(project, targetId, "greetings", 1, []));
        Assert.True(store.ScriptExists(root, "scripts/user/hello.py"));
    }

    [Fact]
    public async Task RenameCannotPersistSystemRegionEditsInAnotherOpenObject()
    {
        var created = new ProjectService(store).CreateScript(project, ProjectScriptKind.Object, "hero");
        var original = store.ReadScript(root, created.Script.Path);
        await using var bridge = new PythonProcessBridge();
        await Assert.ThrowsAsync<ProjectOperationException>(() => new ScriptRenameService(bridge, store)
            .PlanAsync(created.Project, targetId, "greetings", 1,
                [new(new Uri(created.Project.ToAbsolutePath(created.Script.Path)).AbsoluteUri, 1,
                    original.Replace("class Hero", "class Changed"))]));
        Assert.Equal(original, store.ReadScript(root, created.Script.Path));
        Assert.False(store.ScriptExists(root, "scripts/user/greetings.py"));
    }

    public void Dispose() => Directory.Delete(root, true);

    private sealed class FaultStore : IProjectStore
    {
        private readonly JsonProjectStore inner = new();
        public bool FailWrite;
        public bool FailCommit;
        public ProjectManifest ReadManifest(string r) => inner.ReadManifest(r);
        public void WriteManifest(string r, ProjectManifest m)
        {
            if (FailCommit && m.Scripts.Any(s => s.Path == "scripts/user/greetings.py"))
            { FailCommit = false; throw new IOException("manifest write failed"); }
            inner.WriteManifest(r, m);
        }
        public void WriteScript(string r, string p, string s)
        {
            if (FailWrite && p.Replace('\\', '/') == "scripts/user/use.py")
            { FailWrite = false; throw new IOException("source write failed"); }
            inner.WriteScript(r, p, s);
        }
        public void CreateProject(string r, ProjectManifest m, string s) => inner.CreateProject(r, m, s);
        public string ReadMainScript(string r) => inner.ReadMainScript(r);
        public void WriteMainScript(string r, string s) => inner.WriteMainScript(r, s);
        public bool ScriptExists(string r, string p) => inner.ScriptExists(r, p);
        public string ReadScript(string r, string p) => inner.ReadScript(r, p);
        public IReadOnlyList<string> ListScriptFiles(string r) => inner.ListScriptFiles(r);
        public void EnsureLayout(string r) => inner.EnsureLayout(r);
        public void DeleteScript(string r, string p) => inner.DeleteScript(r, p);
    }
}
