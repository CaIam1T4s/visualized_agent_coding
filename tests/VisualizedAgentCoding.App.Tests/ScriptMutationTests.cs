using VisualizedAgentCoding.Application.Ports;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Domain.Projects;
using VisualizedAgentCoding.Infrastructure.Projects;

namespace VisualizedAgentCoding.App.Tests;

public sealed class ScriptMutationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"script-mutation-{Guid.NewGuid():N}");
    private readonly FaultStore store = new();
    private readonly ProjectService service;
    private readonly ProjectWorkspace project;
    private readonly ProjectScriptEntry script;

    public ScriptMutationTests()
    {
        service = new(store);
        var created = service.CreateScript(service.Create(root, "Demo"), ProjectScriptKind.User, "original");
        script = created.Script;
        project = service.RegisterEventHandler(created.Project, script.Id,
            new EventHandlerRegistration(ProjectEventKinds.Start, null, "on_start"));
        store.WriteScript(root, script.Path, "# 用户代码\nvalue = 42\n");
        store.ManifestWrites = 0;
    }

    [Fact]
    public void RenamePreservesIdentityContentAndBindingsAcrossReopen()
    {
        var updated = service.CommitScriptFileRename(project, script.Id, " renamed.py ");
        var renamed = Assert.Single(updated.Manifest.Scripts);
        Assert.Equal(script with { Path = "scripts/user/renamed.py" }, renamed);
        Assert.Equal("# 用户代码\nvalue = 42\n", service.LoadScript(updated, renamed));
        Assert.False(store.ScriptExists(root, script.Path));
        var reopened = service.Open(root);
        Assert.Equal(renamed, Assert.Single(reopened.Manifest.Scripts));
        Assert.Equal(project.Manifest.EventHandlers, reopened.Manifest.EventHandlers);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../escape")]
    [InlineData("bad name")]
    [InlineData(".py")]
    public void InvalidRenameLeavesProjectUnchanged(string name)
    {
        Assert.Throws<ProjectOperationException>(() => service.CommitScriptFileRename(project, script.Id, name));
        AssertOriginal();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RenameRejectsIndexedOrUnindexedCollision(bool indexed)
    {
        var current = project;
        if (indexed)
            current = service.CreateScript(project, ProjectScriptKind.User, "taken").Project;
        else
            store.WriteScript(root, "scripts/user/taken.py", "external");
        Assert.Throws<ProjectOperationException>(() => service.CommitScriptFileRename(current, script.Id, "TAKEN"));
        Assert.Equal("scripts/user/original.py", store.ReadManifest(root).Scripts[0].Path);
        Assert.True(store.ScriptExists(root, script.Path));
    }

    [Fact]
    public void SameNameIsANoOp()
    {
        Assert.Same(project, service.CommitScriptFileRename(project, script.Id, "original.py"));
        Assert.Equal(0, store.ManifestWrites);
    }

    [Theory]
    [InlineData(ProjectScriptKind.Object)]
    [InlineData(ProjectScriptKind.Backdrop)]
    public void ObjectAndBackdropRenameIsRefused(ProjectScriptKind kind)
    {
        var created = service.CreateScript(project, kind, "hero");
        Assert.Throws<ProjectOperationException>(() => service.CommitScriptFileRename(created.Project, created.Script.Id, "new"));
        Assert.True(store.ScriptExists(root, created.Script.Path));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MainScriptAndUnknownIdsAreProtected(bool rename)
    {
        foreach (var id in new[] { project.Manifest.MainScriptId, "missing", "" })
        {
            Assert.Throws<ProjectOperationException>(() => Mutate(rename, id));
        }
        AssertOriginal();
        Assert.True(store.ScriptExists(root, project.Manifest.MainScript));
    }

    [Theory]
    [InlineData(ProjectScriptKind.User)]
    [InlineData(ProjectScriptKind.Object)]
    [InlineData(ProjectScriptKind.Backdrop)]
    public void DeleteRemovesOnlyTargetAndItsBindings(ProjectScriptKind kind)
    {
        var target = service.CreateScript(project, kind, "target");
        var bound = service.RegisterEventHandler(target.Project, target.Script.Id,
            new EventHandlerRegistration(ProjectEventKinds.Start, null, "start_target"));
        var updated = service.DeleteScript(bound, target.Script.Id);
        Assert.Equal(script, Assert.Single(updated.Manifest.Scripts));
        Assert.Equal(project.Manifest.EventHandlers, updated.Manifest.EventHandlers);
        Assert.False(store.ScriptExists(root, target.Script.Path));
        var reopened = service.Open(root);
        Assert.Equal(script, Assert.Single(reopened.Manifest.Scripts));
        Assert.Equal(project.Manifest.EventHandlers, reopened.Manifest.EventHandlers);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ManifestFailureKeepsOriginalFileIndexAndBindings(bool rename)
    {
        store.FailManifestWrite = 1;
        Assert.Throws<IOException>(() => Mutate(rename, script.Id));
        AssertOriginal();
        Assert.False(store.ScriptExists(root, "scripts/user/renamed.py"));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void FailedPhysicalDeleteNeverResurrectsScriptAndReopenRetries(bool rename, bool accessDenied)
    {
        store.AccessDenied = accessDenied;
        store.FailDeletePath = script.Path;
        var updated = Mutate(rename, script.Id);
        Assert.True(store.ScriptExists(root, script.Path));
        var reopened = new ProjectService(store).Open(root);
        Assert.Equal(rename ? 1 : 0, reopened.Manifest.Scripts.Count);
        if (rename)
            Assert.Equal(script.Id, Assert.Single(reopened.Manifest.Scripts).Id);
        else
            Assert.Empty(reopened.Manifest.EventHandlers);
        store.FailDeletePath = null;
        var recovered = service.Open(root);
        Assert.False(store.ScriptExists(root, script.Path));
        Assert.Equal(updated.Manifest.Scripts, recovered.Manifest.Scripts);
    }

    [Fact]
    public void RenameCommitAndRollbackCleanupFailureDoesNotAdoptNewFile()
    {
        store.FailManifestWrite = 2;
        store.FailDeletePath = "scripts/user/renamed.py";
        Assert.Throws<IOException>(() => service.CommitScriptFileRename(project, script.Id, "renamed"));
        Assert.True(store.ScriptExists(root, "scripts/user/renamed.py"));
        AssertOriginal();
        store.FailDeletePath = null;
        service.Open(root);
        Assert.False(store.ScriptExists(root, "scripts/user/renamed.py"));
    }

    [Fact]
    public void RenameCommitFailureRollsBackAndPreservesOriginalException()
    {
        store.FailManifestWrite = 2;
        var error = Assert.Throws<IOException>(() => service.CommitScriptFileRename(project, script.Id, "renamed"));
        Assert.Equal("Injected manifest write failure", error.Message);
        Assert.False(store.ScriptExists(root, "scripts/user/renamed.py"));
        AssertOriginal();
    }

    [Fact]
    public void FailedRollbackIntentSurvivesAnotherMutationWithOldWorkspace()
    {
        store.FailManifestWrite = 2;
        store.FailDeletePath = "scripts/user/renamed.py";
        Assert.Throws<IOException>(() => service.CommitScriptFileRename(project, script.Id, "renamed"));
        var created = service.CreateScript(project, ProjectScriptKind.User, "other");
        Assert.Contains("scripts/user/renamed.py", created.Project.Manifest.PendingScriptDeletions);
        Assert.Equal(2, service.Open(root).Manifest.Scripts.Count);
    }

    [Fact]
    public void PendingPathsCannotBeReusedUntilCleanupSucceeds()
    {
        store.FailDeletePath = script.Path;
        var deleted = service.DeleteScript(project, script.Id);
        Assert.Throws<ProjectOperationException>(() => service.CreateScript(deleted, ProjectScriptKind.User, "original"));
        store.FailDeletePath = null;
        var reopened = service.Open(root);
        Assert.Empty(reopened.Manifest.PendingScriptDeletions);
        var created = service.CreateScript(reopened, ProjectScriptKind.User, "original");
        Assert.NotEqual(script.Id, created.Script.Id);
        Assert.Single(service.Open(root).Manifest.Scripts);
    }

    [Theory]
    [InlineData("scripts/user/../../outside.py")]
    [InlineData("scripts/main.py")]
    [InlineData("scripts/user/original.py")]
    public void InvalidCleanupIntentIsRejectedBeforeDeletingFiles(string path)
    {
        store.WriteManifest(root, project.Manifest with { PendingScriptDeletions = [path] });
        Assert.Throws<ProjectFormatException>(() => service.Open(root));
        Assert.True(store.ScriptExists(root, script.Path));
        Assert.True(store.ScriptExists(root, project.Manifest.MainScript));
    }

    [Fact]
    public void OpenRecoversRenameInterruptedAfterNewFileWasWritten()
    {
        store.WriteManifest(root, project.Manifest with { PendingScriptDeletions = ["scripts/user/renamed.py"] });
        store.WriteScript(root, "scripts/user/renamed.py", store.ReadScript(root, script.Path));
        // Simulate process termination before the renamed index was committed.
        var reopened = new ProjectService(new JsonProjectStore()).Open(root);
        Assert.Equal(script, Assert.Single(reopened.Manifest.Scripts));
        Assert.Empty(reopened.Manifest.PendingScriptDeletions);
        Assert.False(store.ScriptExists(root, "scripts/user/renamed.py"));
        AssertOriginal();
    }

    private ProjectWorkspace Mutate(bool rename, string id) => rename
        ? service.CommitScriptFileRename(project, id, "renamed")
        : service.DeleteScript(project, id);

    private void AssertOriginal()
    {
        var reopened = service.Open(root);
        Assert.Equal(script, Assert.Single(reopened.Manifest.Scripts));
        Assert.Equal(project.Manifest.EventHandlers, reopened.Manifest.EventHandlers);
        Assert.Equal("# 用户代码\nvalue = 42\n", store.ReadScript(root, script.Path));
    }

    public void Dispose() => Directory.Delete(root, true);

    private sealed class FaultStore : IProjectStore
    {
        private readonly JsonProjectStore inner = new();
        public int ManifestWrites { get; set; }
        public int FailManifestWrite { get; set; }
        public string? FailDeletePath { get; set; }
        public bool AccessDenied { get; set; }
        public ProjectManifest ReadManifest(string rootPath) => inner.ReadManifest(rootPath);
        public void WriteManifest(string rootPath, ProjectManifest manifest)
        {
            if (++ManifestWrites == FailManifestWrite)
                throw new IOException("Injected manifest write failure");
            inner.WriteManifest(rootPath, manifest);
        }
        public void DeleteScript(string rootPath, string path)
        {
            if (path == FailDeletePath)
            {
                if (AccessDenied) throw new UnauthorizedAccessException("Injected access denial");
                throw new IOException("Injected deletion failure");
            }
            inner.DeleteScript(rootPath, path);
        }
        public void CreateProject(string r, ProjectManifest m, string c) => inner.CreateProject(r, m, c);
        public string ReadMainScript(string r) => inner.ReadMainScript(r);
        public void WriteMainScript(string r, string c) => inner.WriteMainScript(r, c);
        public bool ScriptExists(string r, string p) => inner.ScriptExists(r, p);
        public string ReadScript(string r, string p) => inner.ReadScript(r, p);
        public void WriteScript(string r, string p, string c) => inner.WriteScript(r, p, c);
        public IReadOnlyList<string> ListScriptFiles(string r) => inner.ListScriptFiles(r);
        public void EnsureLayout(string r) => inner.EnsureLayout(r);
    }
}
