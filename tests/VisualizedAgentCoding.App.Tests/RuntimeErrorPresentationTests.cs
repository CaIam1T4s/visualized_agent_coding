using System.Collections.Concurrent;
using happy_agent_coding.Services;
using happy_agent_coding.ViewModels;
using VisualizedAgentCoding.Application.Editor;
using VisualizedAgentCoding.Application.Editor.ObjectScripts;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Application.Runtime;
using VisualizedAgentCoding.Domain.Projects;
using VisualizedAgentCoding.Infrastructure.Projects;
using VisualizedAgentCoding.Infrastructure.Python;

namespace VisualizedAgentCoding.App.Tests;

public sealed class RuntimeErrorPresentationTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "vac-errors-" + Guid.NewGuid().ToString("N"));
    private readonly JsonProjectStore store = new();
    private readonly PythonProcessBridge bridge = new();
    private readonly RuntimeErrorPresentationService presenter = new();
    private ProjectWorkspace project = null!;
    public Task InitializeAsync() { project = new ProjectService(store).Create(root); return Task.CompletedTask; }
    public async Task DisposeAsync() { await bridge.DisposeAsync(); if (Directory.Exists(root)) Directory.Delete(root, true); }

    private ProjectScriptEntry Add(ProjectScriptKind kind, string name)
    {
        var created = new ProjectService(store).CreateScript(project, kind, name);
        project = created.Project;
        return created.Script;
    }
    private async Task<(RuntimeSourceSnapshot Snapshot, ISourceProjection Projection)> Freeze(ProjectScriptEntry script, string source)
    {
        var document = script.Kind == ProjectScriptKind.User ? null
            : await new ObjectScriptDocumentService(bridge).InspectAsync(project, script.Id, 1, source);
        var imports = await new ScriptImportService(bridge).InspectAsync(project, script, 1, source, document);
        var projection = RuntimeSourceSnapshot.CreateProjection(script, new Uri(project.ToAbsolutePath(script.Path)).AbsoluteUri, source, document, imports.Block)!;
        return (new(root, script, source, projection, document, imports.Block), projection);
    }
    private static int Row(string source, string snippet) => 1 + source[..source.IndexOf(snippet, StringComparison.Ordinal)].Count(c => c == '\n');
    private RuntimeErrorReport Report(RuntimeSourceSnapshot snapshot, int line, string method = "_vac_on_start") =>
        presenter.Present(new("exception", Exception: new("ZeroDivisionError", "division by zero",
            [new(Path.Combine(root, "python-runtime", "runner.py"), 100, "run"), new(snapshot.AbsolutePath, line, method)]),
            RawTraceback: "RAW " + snapshot.AbsolutePath, ProtocolVersion: 2), [snapshot]);

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(3, true)]
    public async Task ObjectErrorsShowProjectedRowsAndNeverGeneratedHandlerNames(int version, bool crlf)
    {
        var script = Add(ProjectScriptKind.Object, "cat");
        var source = version == 1 ? ProjectScriptTemplates.Create(script.Kind, "Cat").Replace("    # vac:user-code-end", "    def foo(self):\n        return 1 / 0\n    # vac:user-code-end")
            : (version == 2 ? ProjectScriptTemplates.CreateMultiRegion(script.Kind, "Cat") : ProjectScriptTemplates.CreateWithImports(script.Kind, "Cat"))
                .Replace(ProjectScriptTemplates.UserPlaceholderStatement, "result = 1 / 0");
        if (crlf) source = source.Replace("\n", "\r\n");
        var (snapshot, projection) = await Freeze(script, source);
        var actual = Row(source, "1 / 0");
        var expected = Row(projection.ViewSource, "1 / 0");
        var report = Report(snapshot, actual, version == 1 ? "foo" : "_vac_on_start");
        var frame = Assert.Single(report.Frames);
        Assert.Equal(expected, frame.ViewLine);
        Assert.Equal(actual, frame.Original.Line);
        Assert.True(frame.CanNavigate);
        Assert.Contains($"第 {expected} 行", report.UserText);
        Assert.DoesNotContain("_vac_on_start", report.UserText);
        Assert.DoesNotContain("runner.py", report.UserText);
        Assert.DoesNotContain(root, report.UserText);
        Assert.Contains(root, report.Details);
        Assert.Equal(0, report.PrimaryFrameIndex);
    }

    [Fact]
    public async Task ManagedImportsHaveSummaryNavigationAndNormalCodeHasVisibleRows()
    {
        var script = Add(ProjectScriptKind.User, "util");
        const string source = "\"\"\"工具\"\"\"\n# vac:imports-begin\nimport math  # vac:import {\"source\":\"standard\"}\n# vac:imports-end\n\nvalue = 1 / 0\n";
        var (snapshot, projection) = await Freeze(script, source);
        var code = Assert.Single(Report(snapshot, Row(source, "value ="), "<module>").Frames);
        Assert.Equal(Row(projection.ViewSource, "value ="), code.ViewLine);
        var import = Assert.Single(Report(snapshot, Row(source, "import math"), "<module>").Frames);
        Assert.Equal("imports", import.Navigation);
        Assert.Contains("导入摘要", import.DisplayText);
        Assert.DoesNotContain("第 3 行", import.DisplayText);
    }

    [Fact]
    public async Task FrozenMappingsDoNotChangeWhenProjectionIsEdited()
    {
        var script = Add(ProjectScriptKind.Object, "cat");
        var source = store.ReadScript(root, script.Path);
        var (snapshot, projection) = await Freeze(script, source);
        var line = Row(source, "pass  #");
        var before = snapshot.Map(line);
        ((MultiRegionProjection)projection).UpdateRegions(new Dictionary<string, string> { ["members"] = "x = 1\ny = 2\nz = 3\n" });
        Assert.Equal(before, snapshot.Map(line));
        Assert.Equal(source, snapshot.Source);
        Assert.NotEqual(projection.ViewSource, snapshot.ViewSource);
    }

    [Fact]
    public async Task InitializeAndEventRowsRespectViewOrderRatherThanDiskOrder()
    {
        var script = Add(ProjectScriptKind.Backdrop, "sky");
        var source = ProjectScriptTemplates.CreateWithImports(script.Kind, "Sky",
            [new("members", "members"), new("start", "start"), new("initialize", "initialize"), new("click", "event", "stage-click")]);
        var (snapshot, projection) = await Freeze(script, source);
        var doc = ((MultiRegionProjection)projection).Document;
        foreach (var region in doc.Regions!.Where(r => r.Kind != "members"))
        {
            var fullRow = 1 + source[..region.Span.Offset].Count(c => c == '\n');
            Assert.True(projection.TryToViewPosition(fullRow, 9, out var row, out var column));
            var actual = Assert.Single(Report(snapshot, fullRow, "_vac_internal").Frames);
            Assert.Equal(row, actual.ViewLine);
            Assert.Equal(column, actual.ViewColumn);
            Assert.DoesNotContain("_vac_internal", actual.DisplayText);
        }
    }

    [Fact]
    public async Task EmojiColumnsUseUtf16AndRunnerTracebackNeverLeaksIntoStderr()
    {
        var script = Add(ProjectScriptKind.Object, "cat");
        var source = store.ReadScript(root, script.Path).Replace(ProjectScriptTemplates.UserPlaceholderStatement, "\"😀\"; result = 1 / 0");
        var (snapshot, projection) = await Freeze(script, source);
        await using var runtime = new PythonRuntimeProcess();
        var events = new ConcurrentQueue<PythonRunEvent>();
        var request = new PythonRunRequest("emoji", PythonEnvironment.DefaultExecutable, root, project.Manifest.MainScript,
            [new(project.Manifest.MainScript, "pass\n"), new(script.Path, source)],
            [new(script.Id, script.ObjectId, script.Path, "object")], []);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.Equal(1, await runtime.RunAsync(request, events.Enqueue, timeout.Token));
        var error = Assert.Single(events, e => e.Kind == "exception");
        Assert.Empty(string.Concat(events.Where(e => e.Kind == "stderr").Select(e => e.Text)));
        var mapped = Assert.Single(presenter.Present(error, [snapshot]).Frames);
        Assert.Equal(Row(projection.ViewSource, "1 / 0"), mapped.ViewLine);
        if (mapped.Original.Column is not null)
        {
            var viewLine = projection.ViewSource.Split('\n')[mapped.ViewLine!.Value - 1];
            Assert.Equal(viewLine.IndexOf("1 / 0", StringComparison.Ordinal) + 1, mapped.ViewColumn);
        }
        Assert.Contains("runner.py", error.RawTraceback);
    }

    [Fact]
    public async Task HiddenWrapperAndUnknownFramesHaveNoInventedLineNumbers()
    {
        var script = Add(ProjectScriptKind.Object, "cat");
        var source = store.ReadScript(root, script.Path);
        var (snapshot, _) = await Freeze(script, source);
        var hidden = Assert.Single(Report(snapshot, Row(source, "def _vac_on_start")).Frames);
        Assert.Contains("程序启动", hidden.DisplayText);
        Assert.Null(hidden.ViewLine);
        Assert.False(hidden.CanNavigate);
        var unknown = Assert.Single(Report(snapshot, 999).Frames);
        Assert.Contains("位置无法映射", unknown.DisplayText);
        Assert.False(unknown.CanNavigate);
    }

    [Fact]
    public void InternalFailureKeepsDetailsAndDoesNotBlameUserCode()
    {
        var report = presenter.Present(new("exception", Exception: new("RuntimeError", "failed", [new("/product/runner.py", 22, "run")]), RawTraceback: "/product/runner.py:22"), []);
        Assert.Empty(report.Frames);
        Assert.Contains("未找到可定位", report.UserText);
        Assert.DoesNotContain("/product", report.UserText);
        Assert.Contains("/product", report.Details);
    }

    [Fact]
    public async Task PreparationSyntaxErrorsUseEditorRowsAndKeepRawRowsOnlyInDetails()
    {
        var script = Add(ProjectScriptKind.Object, "cat");
        var source = store.ReadScript(root, script.Path).Replace(ProjectScriptTemplates.UserPlaceholderStatement, "value =");
        store.WriteScript(root, script.Path, source);
        var (snapshot, projection) = await Freeze(script, source);
        var error = await Assert.ThrowsAsync<RuntimePreparationException>(() => new ProjectRunService(store, bridge).PrepareAsync(project, PythonEnvironment.DefaultExecutable, default));
        Assert.Contains(error.Report.Frames, f => f.ViewLine == Row(projection.ViewSource, "value ="));
        Assert.DoesNotContain(root, error.Report.UserText);
        Assert.Contains($":{Row(source, "value =")}:", error.Report.Details);
    }

    [Fact]
    public async Task MainErrorCardNavigationIsDisabledWhenBufferChangesOrProjectIsReopened()
    {
        store.WriteMainScript(root, "print('before')\nvalue = 1 / 0\n");
        await using var vmBridge = new PythonProcessBridge();
        await using var vm = new MainViewModel(new JsonSettingsStore(Path.Combine(root, "settings.json")), new InMemoryRecentProjectsStore(), new ProjectSession(), vmBridge);
        await vm.OpenProject(root);
        await vm.RunCommand.ExecuteAsync(null);
        Assert.True(vm.HasRuntimeError);
        var item = Assert.Single(vm.RuntimeLocations);
        Assert.True(item.CanNavigate);
        Assert.DoesNotContain(root, vm.RuntimeErrorText);
        Assert.DoesNotContain("Traceback", vm.TerminalOutput);
        var opened = 0;
        vm.RuntimeLocationOpened += _ => opened++;
        await vm.OpenRuntimeLocationCommand.ExecuteAsync(item);
        Assert.Equal(1, opened);
        vm.PythonCode += "# edited\n";
        Assert.False(item.CanNavigate);
        await vm.OpenRuntimeLocationCommand.ExecuteAsync(item);
        Assert.Equal(1, opened);
        await vm.CloseProject();
        await vm.OpenProject(root);
        Assert.False(item.CanNavigate);
    }

    [Fact]
    public async Task ObjectCallingUserFunctionShowsBothVisiblePositionsAndSelectsTheInnerFrame()
    {
        var user = Add(ProjectScriptKind.User, "input_test");
        const string userSource = "\"\"\"InputTest\"\"\"\ndef foo():\n    print('start')\n    result = 1 / 0\n";
        store.WriteScript(root, user.Path, userSource);
        var actor = Add(ProjectScriptKind.Object, "cat");
        var actorSource = store.ReadScript(root, actor.Path);
        var document = await new ObjectScriptDocumentService(bridge).InspectAsync(project, actor.Id, 1, actorSource);
        var imported = await new ScriptImportService(bridge).InspectAsync(project, actor, 1, actorSource, document,
            [new("scripts.user.input_test", Alias: "input_test", Source: "project", ScriptId: user.Id)]);
        actorSource = imported.Source.Replace(ProjectScriptTemplates.UserPlaceholderStatement, "input_test.foo()");
        store.WriteScript(root, actor.Path, actorSource);
        var (_, projection) = await Freeze(actor, actorSource);
        await using var vmBridge = new PythonProcessBridge();
        await using var vm = new MainViewModel(new JsonSettingsStore(Path.Combine(root, "settings.json")), new InMemoryRecentProjectsStore(), new ProjectSession(), vmBridge);
        await vm.OpenProject(root);
        await vm.RunCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.RuntimeLocations.Count);
        var caller = vm.RuntimeLocations[0].Presentation;
        var callee = vm.RuntimeLocations[1].Presentation;
        Assert.Equal(Row(projection.ViewSource, "input_test.foo()"), caller.ViewLine);
        Assert.Contains("程序启动", caller.DisplayText);
        Assert.Equal(4, callee.ViewLine);
        Assert.Contains("foo", callee.DisplayText);
        Assert.Same(vm.RuntimeLocations[1], vm.SelectedRuntimeLocation);
        Assert.Contains("result = 1 / 0", vm.RuntimeErrorText);
        Assert.DoesNotContain("_vac_on_start", vm.RuntimeErrorText);
        Assert.DoesNotContain("runner.py", vm.RuntimeErrorText);
        Assert.Contains("_vac_on_start", vm.RuntimeDiagnosticDetails);
        Assert.Contains("runner.py", vm.RuntimeDiagnosticDetails);
        var locations = new List<PythonReferenceItem>();
        vm.RuntimeLocationOpened += locations.Add;
        await vm.OpenRuntimeLocationCommand.ExecuteAsync(vm.RuntimeLocations[0]);
        Assert.Equal(actor.Id, vm.OpenScript!.Id);
        Assert.Equal(Row(actorSource, "input_test.foo()"), Assert.Single(locations).Location.Range.Line);
    }

    [Fact]
    public async Task ExceptionChainsAndGroupsAreStructuredWhileUserPrintedTracebacksStayOnStderr()
    {
        await using var runtime = new PythonRuntimeProcess();
        var events = new ConcurrentQueue<PythonRunEvent>();
        const string source = "import traceback,sys\ntry:\n    1 / 0\nexcept Exception as error:\n    traceback.print_exc()\n    if sys.version_info >= (3,11):\n        raise ExceptionGroup('group', [ValueError('one'), TypeError('two')]) from error\n    raise ValueError('one') from error\n";
        var request = new PythonRunRequest("test", PythonEnvironment.DefaultExecutable, root, "scripts/main.py", [new("scripts/main.py", source)], [], []);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.Equal(1, await runtime.RunAsync(request, events.Enqueue, timeout.Token));
        var error = Assert.Single(events, e => e.Kind == "exception");
        Assert.Equal(2, error.ProtocolVersion);
        Assert.Equal("ZeroDivisionError", error.Exception!.Cause!.Type);
        if (error.Exception.Type == "ExceptionGroup") Assert.Equal(2, error.Exception.Children!.Length);
        Assert.Contains("Traceback", string.Concat(events.Where(e => e.Kind == "stderr").Select(e => e.Text)));
        Assert.Contains("one", error.RawTraceback);
        var main = new ProjectScriptEntry(project.Manifest.MainScriptId, "scripts/main.py", ProjectScriptKind.User);
        var (snapshot, _) = await Freeze(main, source);
        var report = presenter.Present(error, [snapshot]);
        Assert.Contains("直接导致", report.UserText);
        Assert.DoesNotContain(root, report.UserText);
        Assert.DoesNotContain("runner.py", report.UserText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImplicitExceptionContextHonorsRaiseFromNone(bool suppress)
    {
        await using var runtime = new PythonRuntimeProcess();
        var events = new ConcurrentQueue<PythonRunEvent>();
        var source = "try:\n    1 / 0\nexcept Exception:\n    raise ValueError('second')" + (suppress ? " from None" : "") + "\n";
        var request = new PythonRunRequest("context", PythonEnvironment.DefaultExecutable, root, "scripts/main.py", [new("scripts/main.py", source)], [], []);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.Equal(1, await runtime.RunAsync(request, events.Enqueue, timeout.Token));
        var error = Assert.Single(events, e => e.Kind == "exception");
        Assert.Equal(suppress, error.Exception!.SuppressContext);
        if (suppress) Assert.Null(error.Exception.Context);
        else Assert.Equal("ZeroDivisionError", error.Exception.Context!.Type);
        Assert.DoesNotContain(events, e => e.Kind == "stderr" && e.Text.Contains("Traceback"));
    }

    [Fact]
    public async Task WorkerThreadExceptionsUseTheSameProtocolAndFailTheRun()
    {
        await using var runtime = new PythonRuntimeProcess();
        var events = new ConcurrentQueue<PythonRunEvent>();
        const string source = "import threading\ndef worker():\n    1 / 0\nthreading.Thread(target=worker).start()\n";
        var request = new PythonRunRequest("thread", PythonEnvironment.DefaultExecutable, root, "scripts/main.py", [new("scripts/main.py", source)], [], []);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.Equal(1, await runtime.RunAsync(request, events.Enqueue, timeout.Token));
        var error = Assert.Single(events, e => e.Kind == "exception");
        Assert.Equal("ZeroDivisionError", error.Exception!.Type);
        Assert.Contains(error.Exception.Frames, f => f.Function == "worker" && f.Line == 3);
        Assert.DoesNotContain(events, e => e.Kind == "stderr" && e.Text.Contains("Traceback"));
    }
}
