using VisualizedAgentCoding.Application.Editor.ObjectScripts;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Application.Python;
using VisualizedAgentCoding.Application.Ports;
using VisualizedAgentCoding.Domain.Projects;
using VisualizedAgentCoding.Infrastructure.Projects;
using VisualizedAgentCoding.Infrastructure.Python;

namespace VisualizedAgentCoding.App.Tests;

public sealed class ObjectScriptSaveTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "object-save-" + Guid.NewGuid().ToString("N"));
    private readonly JsonProjectStore store = new();
    private readonly ProjectWorkspace project;
    private readonly ProjectScriptEntry script;

    public ObjectScriptSaveTests()
    {
        var projects = new ProjectService(store);
        var created = projects.CreateScript(projects.Create(root, "Demo"), ProjectScriptKind.Object, "hero");
        project = created.Project;
        script = created.Script;
    }

    private string Read() => store.ReadScript(root, script.Path);
    private ObjectScriptSaveService Service(PythonProcessBridge bridge) => new(new ObjectScriptDocumentService(bridge), store);
    private static string Insert(string source, string user) => source.Replace("    # vac:user-code-end", user + "    # vac:user-code-end");

    [Fact]
    public async Task PreparePreservesUnicodeCrLfShellAndDoesNotWriteUntilApplied()
    {
        var original = Read().Replace("    pass", "    pass  # 系统😀").Replace("\n", "\r\n");
        store.WriteScript(root, script.Path, original);
        var edited = Insert(original, "    text = \"你好😀\"\r\n");
        await using var bridge = new PythonProcessBridge();
        var service = Service(bridge);
        var plan = await service.PrepareFullBufferAsync(project, script.Id, 1, original, edited);
        Assert.Equal(original, Read());
        Assert.Equal(edited, plan.MergedSource);
        Assert.Equal("    text = \"你好😀\"\r\n", plan.Document!.UserSource);
        service.Apply(plan, 1);
        Assert.Equal(edited, Read());
    }

    [Fact]
    public async Task EmptyUserContentKeepsSystemPassAndWholeFileValid()
    {
        var original = Insert(Read(), "    value = 42\n");
        store.WriteScript(root, script.Path, original);
        await using var bridge = new PythonProcessBridge();
        var document = await new ObjectScriptDocumentService(bridge).InspectAsync(project, script.Id, 1, original);
        var service = Service(bridge);
        var plan = service.PrepareUserRegion(project, document, 2, "");
        service.Apply(plan, 2);
        Assert.Equal("", plan.Document!.UserSource);
        Assert.Contains("    pass\n", Read());
        Assert.DoesNotContain("value = 42", Read());
        Assert.Empty((await bridge.ParseAsync(new(document.Uri, 2, Read()))).Diagnostics);
    }

    [Fact]
    public async Task InvalidPythonCanBeSavedAndFixedUsingPreviouslyConfirmedAnchors()
    {
        var original = Read();
        await using var bridge = new PythonProcessBridge();
        var service = Service(bridge);
        var invalid = Insert(original, "    text = \"\"\"\n");
        var first = await service.PrepareFullBufferAsync(project, script.Id, 1, original, invalid);
        service.Apply(first, 1);
        Assert.Equal(invalid, Read());
        var fixedSource = Insert(original, "    text = \"fixed\"\n");
        var next = await service.PrepareFullBufferAsync(project, script.Id, 2, invalid, fixedSource, first.Document);
        service.Apply(next, 2);
        Assert.Equal(fixedSource, Read());
    }

    [Fact]
    public async Task ReopenedUnterminatedStringCanBeExplicitlyFixedWithoutGuessingMarkers()
    {
        var template = Read();
        var invalid = Insert(template, "    text = \"\"\"\n");
        store.WriteScript(root, script.Path, invalid);
        await using var bridge = new PythonProcessBridge();
        var fixedSource = Insert(template, "    text = \"fixed\"\n");
        var service = Service(bridge);
        var plan = await service.PrepareFullBufferAsync(project, script.Id, 1, invalid, fixedSource);
        Assert.Equal(invalid, Read());
        Assert.True(plan.Document!.HasUserRegion);
        service.Apply(plan, 1);
        Assert.Equal(fixedSource, Read());
    }

    [Theory]
    [InlineData("class Hero", "class Changed")]
    [InlineData("from runtime", "from changed")]
    [InlineData("# vac:user-code-end", "# deleted")]
    public async Task SystemEditsAreRejectedWithoutSaving(string before, string after)
    {
        var original = Read();
        await using var bridge = new PythonProcessBridge();
        await Assert.ThrowsAsync<ProjectOperationException>(() => Service(bridge)
            .PrepareFullBufferAsync(project, script.Id, 1, original, original.Replace(before, after)));
        Assert.Equal(original, Read());
    }

    [Fact]
    public async Task StaleVersionExternalSourceAndIndexChangesRejectApply()
    {
        var original = Read();
        await using var bridge = new PythonProcessBridge();
        var service = Service(bridge);
        var plan = await service.PrepareFullBufferAsync(project, script.Id, 1, original, Insert(original, "    value = 1\n"));
        Assert.Throws<ProjectOperationException>(() => service.Apply(plan, 2));
        Assert.Equal(original, Read());
        store.WriteScript(root, script.Path, original + "# external\n");
        Assert.Throws<ProjectOperationException>(() => service.Apply(plan, 1));
        Assert.EndsWith("# external\n", Read());
        store.WriteScript(root, script.Path, original);
        store.WriteManifest(root, project.Manifest with { Scripts = [] });
        Assert.Throws<ProjectOperationException>(() => service.Apply(plan, 1));
        Assert.Equal(original, Read());
    }

    [Fact]
    public async Task LegacyFullFileSavingDoesNotGenerateOrMigrateMarkers()
    {
        const string original = "from runtime.element_base import ElementBase\nclass Hero(ElementBase):\n    pass\n";
        store.WriteScript(root, script.Path, original);
        await using var bridge = new PythonProcessBridge();
        var service = Service(bridge);
        var edited = original + "# legacy edit\n";
        var plan = await service.PrepareFullBufferAsync(project, script.Id, 1, original, edited);
        Assert.Null(plan.Document);
        service.Apply(plan, 1);
        Assert.Equal(edited, Read());
        Assert.DoesNotContain("vac:", Read());
    }

    [Fact]
    public async Task UnknownTemplateVersionRejectsMerge()
    {
        var original = Read().Replace("version=1", "version=2");
        store.WriteScript(root, script.Path, original);
        await using var bridge = new PythonProcessBridge();
        await Assert.ThrowsAsync<ProjectOperationException>(() => Service(bridge)
            .PrepareFullBufferAsync(project, script.Id, 1, original, Insert(original, "    value = 1\n")));
        Assert.Equal(original, Read());
    }

    [Fact]
    public async Task WriteFailureKeepsOriginalAndSamePlanCanBeRetried()
    {
        var original = Read();
        await using var bridge = new PythonProcessBridge();
        var failing = new FailingStore(store);
        var service = new ObjectScriptSaveService(new ObjectScriptDocumentService(bridge), failing);
        var edited = Insert(original, "    value = 7\n");
        var plan = await service.PrepareFullBufferAsync(project, script.Id, 1, original, edited);
        failing.FailNext = true;
        Assert.Throws<IOException>(() => service.Apply(plan, 1));
        Assert.Equal(original, Read());
        Assert.Equal(edited, plan.MergedSource);
        service.Apply(plan, 1);
        Assert.Equal(edited, Read());
    }

    private sealed class FailingStore(IProjectStore inner) : IProjectStore
    {
        public bool FailNext;
        public void WriteScript(string root, string path, string source)
        {
            if (FailNext) { FailNext = false; throw new IOException("write failed"); }
            inner.WriteScript(root, path, source);
        }
        public ProjectManifest ReadManifest(string root) => inner.ReadManifest(root);
        public void WriteManifest(string root, ProjectManifest manifest) => inner.WriteManifest(root, manifest);
        public void CreateProject(string root, ProjectManifest manifest, string source) => inner.CreateProject(root, manifest, source);
        public string ReadMainScript(string root) => inner.ReadMainScript(root);
        public void WriteMainScript(string root, string source) => inner.WriteMainScript(root, source);
        public bool ScriptExists(string root, string path) => inner.ScriptExists(root, path);
        public string ReadScript(string root, string path) => inner.ReadScript(root, path);
        public IReadOnlyList<string> ListScriptFiles(string root) => inner.ListScriptFiles(root);
        public void EnsureLayout(string root) => inner.EnsureLayout(root);
        public void DeleteScript(string root, string path) => inner.DeleteScript(root, path);
    }

    public void Dispose() => Directory.Delete(root, true);
}
