using System.Collections.Concurrent;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Application.Runtime;
using VisualizedAgentCoding.Domain.Projects;
using VisualizedAgentCoding.Infrastructure.Projects;
using VisualizedAgentCoding.Infrastructure.Python;

namespace VisualizedAgentCoding.App.Tests;

public sealed class ManagedProjectEntryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "vac-entry-" + Guid.NewGuid().ToString("N"));
    private readonly JsonProjectStore store = new();

    [Fact]
    public async Task LegacyEntryUpgradesDuringPreparationAndDoesNotBecomeANavigableUserSource()
    {
        var project = new ProjectService(store).Create(root);
        store.WriteMainScript(root, ManagedProjectEntry.LegacySource.Replace("\n", "\r\n"));
        await using var bridge = new PythonProcessBridge();
        var request = await new ProjectRunService(store, bridge).PrepareAsync(project, PythonEnvironment.DefaultExecutable, default);
        Assert.Equal(ManagedProjectEntry.Source, store.ReadMainScript(root));
        Assert.Contains(request.Sources, s => s.Source == ManagedProjectEntry.Source);
        Assert.DoesNotContain(request.PresentationSources!, s => s.ScriptId == project.Manifest.MainScriptId);
    }

    [Theory]
    [InlineData("print('custom entry')\n")]
    [InlineData("# vac:entry-version=99\n")]
    [InlineData("# vac:entry-version=1\nprint('changed')\n")]
    public async Task ModifiedOrUnknownEntryIsRejectedWithoutOverwriting(string source)
    {
        var project = new ProjectService(store).Create(root);
        store.WriteMainScript(root, source);
        await using var bridge = new PythonProcessBridge();
        var error = await Assert.ThrowsAsync<ProjectOperationException>(() =>
            new ProjectRunService(store, bridge).PrepareAsync(project, PythonEnvironment.DefaultExecutable, default));
        Assert.Contains("系统入口", error.Message);
        Assert.Equal(source, store.ReadMainScript(root));
    }

    [Fact]
    public async Task ManagedEntryInitializesAllObjectsBeforeStartingAndRestartsCleanly()
    {
        var service = new ProjectService(store);
        var project = service.Create(root);
        Assert.Equal(ManagedProjectEntry.Source, store.ReadMainScript(root));
        foreach (var name in new[] { "cat", "dog" })
        {
            var created = service.CreateScript(project, ProjectScriptKind.Object, name);
            project = created.Project;
            var source = ProjectScriptTemplates.CreateWithImports(ProjectScriptKind.Object, char.ToUpperInvariant(name[0]) + name[1..],
                [new("members", "members"), new("initialize", "initialize"), new("start", "start")]);
            source = source.Replace("        " + ProjectScriptTemplates.UserPlaceholderStatement,
                "        print('init " + name + "')");
            var last = source.LastIndexOf("print('init " + name + "')", StringComparison.Ordinal);
            source = source[..last] + source[last..].Replace("init ", "start ");
            store.WriteScript(root, created.Script.Path, source);
        }
        await using var bridge = new PythonProcessBridge();
        var request = await new ProjectRunService(store, bridge).PrepareAsync(project, PythonEnvironment.DefaultExecutable, default);
        await using var runtime = new PythonRuntimeProcess(Path.Combine(AppContext.BaseDirectory, "python-runtime"));
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var output = new ConcurrentQueue<string>();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            Assert.Equal(0, await runtime.RunAsync(request, e => { if (e.Kind == "stdout") output.Enqueue(e.Text); }, timeout.Token));
            Assert.Equal(new[] { "init cat", "init dog", "start cat", "start dog" },
                string.Concat(output).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
    }

    [Fact]
    public async Task ManagedInitializationFailureSkipsStartsAndPreservesUserFrame()
    {
        const string actor = "from runtime.element_base import ElementBase\nclass Cat(ElementBase):\n    def initialize(self):\n        raise ValueError('bad init')\n    def _vac_on_start(self):\n        print('must not start')\n";
        Directory.CreateDirectory(root);
        var request = new PythonRunRequest("entry-failure", PythonEnvironment.DefaultExecutable, root, "scripts/main.py",
            [new("scripts/main.py", ManagedProjectEntry.Source), new("scripts/objects/cat.py", actor)],
            [new("cat", "cat-object", "scripts/objects/cat.py", "object")], []);
        await using var runtime = new PythonRuntimeProcess(Path.Combine(AppContext.BaseDirectory, "python-runtime"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var events = new ConcurrentQueue<PythonRunEvent>();
        Assert.Equal(1, await runtime.RunAsync(request, events.Enqueue, timeout.Token));
        Assert.DoesNotContain(events, e => e.Kind == "stdout" && e.Text.Contains("must not start"));
        Assert.Contains(Assert.Single(events, e => e.Kind == "exception").Frames!,
            frame => frame.Path == Path.Combine(root, "scripts", "objects", "cat.py") && frame.Line == 4);
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
