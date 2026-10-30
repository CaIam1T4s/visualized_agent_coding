using System.Collections.Concurrent;
using happy_agent_coding.Services;
using happy_agent_coding.ViewModels;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Infrastructure.Projects;
using VisualizedAgentCoding.Application.Runtime;
using VisualizedAgentCoding.Domain.Projects;
using VisualizedAgentCoding.Infrastructure.Python;

namespace VisualizedAgentCoding.App.Tests;

public sealed class PythonRuntimeTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "vac runtime 中文 " + Guid.NewGuid().ToString("N"));
    private readonly string runtimePath = Path.Combine(AppContext.BaseDirectory, "python-runtime");
    public PythonRuntimeTests() => Directory.CreateDirectory(root);
    private PythonRunRequest Request(string main, PythonRunSource[]? extra = null, PythonRunScript[]? scripts = null) =>
        new(Guid.NewGuid().ToString("N"), PythonEnvironment.DefaultExecutable, root, "scripts/main.py",
            [new("scripts/main.py", main), .. extra ?? []], scripts ?? [], []);

    [Fact]
    public async Task RealProcessStreamsPromptsAndMultipleInputsWithoutNewline()
    {
        await using var runtime = new PythonRuntimeProcess(runtimePath);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var prompt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var output = new ConcurrentQueue<string>();
        var errors = new ConcurrentQueue<string>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var task = runtime.RunAsync(Request("a=input('名字：'); b=input('年龄：'); print(a,b); print('错误流',file=__import__('sys').stderr)"), e =>
        {
            if (e.Kind == "ready") ready.TrySetResult();
            if (e.Kind == "stdout") { output.Enqueue(e.Text); if (e.Text.Contains("名字")) prompt.TrySetResult(); }
            if (e.Kind == "stderr") errors.Enqueue(e.Text);
        }, timeout.Token);
        await ready.Task.WaitAsync(timeout.Token);
        await prompt.Task.WaitAsync(timeout.Token);
        await runtime.SendInputAsync("小明", timeout.Token);
        await runtime.SendInputAsync("12", timeout.Token);
        Assert.Equal(0, await task);
        var text = string.Concat(output);
        Assert.Contains("小明 12", text);
        Assert.Contains("错误流", string.Concat(errors));
        await Assert.ThrowsAsync<IOException>(() => runtime.SendInputAsync("late"));
    }

    [Theory]
    [InlineData("input('等待输入')")]
    [InlineData("while True: pass")]
    public async Task CancellationStopsBlockedInputOrLoopAndAllowsRestart(string source)
    {
        await using var runtime = new PythonRuntimeProcess(runtimePath);
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var run = runtime.RunAsync(Request(source), e => { if (e.Kind == "ready") ready.TrySetResult(); }, cancel.Token);
        await ready.Task.WaitAsync(cancel.Token);
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(5)));
        using var nextTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Assert.Equal(0, await runtime.RunAsync(Request("print('next')"), _ => { }, nextTimeout.Token));
    }

    [Fact]
    public async Task AllObjectsInitializeBeforeMainAndStartUsingFrozenImports()
    {
        await using var runtime = new PythonRuntimeProcess(runtimePath);
        var output = new ConcurrentQueue<string>();
        var source = ProjectScriptTemplates.CreateWithImports(ProjectScriptKind.Object, "Cat",
            [new("members", "members"), new("initialize", "initialize"), new("start", "start")]);
        source = source.Replace("        pass  # 请删除此行并编写代码", "        print('initialize')", StringComparison.Ordinal);
        var lastPrint = source.LastIndexOf("print('initialize')", StringComparison.Ordinal);
        source = source[..lastPrint] + source[lastPrint..].Replace("print('initialize')", "print('start')");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var request = Request("from scripts.user.util import value\nprint('main',value)",
            [new("scripts/objects/cat.py", source), new("scripts/user/util.py", "value=42")],
            [new("cat-id", "object-id", "scripts/objects/cat.py", "object")]);
        Assert.Equal(0, await runtime.RunAsync(request, e => { if (e.Kind == "stdout") output.Enqueue(e.Text); }, timeout.Token));
        var text = string.Concat(output);
        Assert.True(text.IndexOf("initialize", StringComparison.Ordinal) < text.IndexOf("main 42", StringComparison.Ordinal));
        Assert.True(text.IndexOf("main 42", StringComparison.Ordinal) < text.IndexOf("start", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(root, "runtime", "element_base.py")));
        Assert.False(Directory.Exists(Path.Combine(root, "__pycache__")));
    }

    [Fact]
    public async Task InitializationFailurePreventsMainAndStartAndReportsOriginalSourcePosition()
    {
        await using var runtime = new PythonRuntimeProcess(runtimePath);
        var events = new ConcurrentQueue<PythonRunEvent>();
        const string source = "from runtime.element_base import ElementBase\nclass Cat(ElementBase):\n    def initialize(self):\n        raise ValueError('bad init')\n    def _vac_on_start(self):\n        print('must not start')\n";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var code = await runtime.RunAsync(Request("print('must not run main')", [new("scripts/objects/cat.py", source)],
            [new("cat", "obj", "scripts/objects/cat.py", "object")]), events.Enqueue, timeout.Token);
        Assert.Equal(1, code);
        Assert.DoesNotContain("must not", string.Concat(events.Where(e => e.Kind == "stdout").Select(e => e.Text)));
        var error = Assert.Single(events, e => e.Kind == "exception");
        Assert.Contains(error.Frames!, f => f.Path == Path.Combine(root, "scripts", "objects", "cat.py") && f.Line == 4);
    }

    [Fact]
    public async Task ExistingProjectRuntimeIsPreservedAndUsed()
    {
        await using var runtime = new PythonRuntimeProcess(runtimePath);
        var output = new ConcurrentQueue<string>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var request = Request("from runtime.element_base import ElementBase\nprint(ElementBase.marker)",
            [new("runtime/element_base.py", "class ElementBase:\n    marker='custom base'")]);
        Assert.Equal(0, await runtime.RunAsync(request, e => { if (e.Kind == "stdout") output.Enqueue(e.Text); }, timeout.Token));
        Assert.Contains("custom base", string.Concat(output));
    }

    [Fact]
    public void PythonSettingRoundTripsAndOldSettingsUseDefault()
    {
        var store = new JsonSettingsStore(Path.Combine(root, "settings.json"));
        File.WriteAllText(store.FilePath, "{\"Appearance.Theme\":\"Dark\"}");
        Assert.Equal("", store.Load().PythonExecutable);
        store.Save(store.Load() with { PythonExecutable = @"C:\Python 中文\python.exe" });
        Assert.Equal(@"C:\Python 中文\python.exe", store.Load().PythonExecutable);
        Assert.Equal(2, store.Load().ThemeIndex);
    }

    [Fact]
    public async Task EnvironmentValidationReportsActualExecutableAndRejectsInvalidPaths()
    {
        var environment = new PythonEnvironment();
        var info = await environment.ValidateAsync(PythonEnvironment.DefaultExecutable);
        Assert.True(Path.IsPathFullyQualified(info.Executable));
        Assert.True(File.Exists(info.Executable));
        await Assert.ThrowsAnyAsync<Exception>(() => environment.ValidateAsync(Path.Combine(root, "missing-python.exe")));
    }

    [Fact]
    public async Task DescendantHoldingStdioIsReapedWhenProjectExits()
    {
        if (!OperatingSystem.IsWindows()) return;
        await using var runtime = new PythonRuntimeProcess(runtimePath);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Assert.Equal(0, await runtime.RunAsync(Request("import subprocess,sys\nsubprocess.Popen([sys.executable,'-c','import time; time.sleep(60)'])\nprint('done')"), _ => { }, timeout.Token));
    }

    [Fact]
    public async Task ProjectRunsWithoutAnOpenEditorAndKeepsOutputWhenSwitchingViews()
    {
        var store = new JsonProjectStore();
        var project = new ProjectService(store).Create(Path.Combine(root, "project"));
        var actor = new ProjectService(store).CreateScript(project, ProjectScriptKind.Object, "actor").Script;
        store.WriteScript(project.RootPath, actor.Path, store.ReadScript(project.RootPath, actor.Path)
            .Replace(ProjectScriptTemplates.UserPlaceholderStatement, "print('项目运行成功')"));
        await using var bridge = new PythonProcessBridge();
        await using var vm = new MainViewModel(new JsonSettingsStore(Path.Combine(root, "settings.json")),
            new InMemoryRecentProjectsStore(), new ProjectSession(), bridge);
        await vm.OpenProject(project.RootPath);
        Assert.Null(vm.OpenScript);
        await vm.RunCommand.ExecuteAsync(null);
        Assert.Contains("项目运行成功", vm.TerminalOutput);
        Assert.Equal("已结束 · 退出码 0", vm.RuntimeStatus);
        Assert.False(vm.CanSendTerminalInput);
        vm.StageViewIndex = 1;
        Assert.True(vm.IsStageVisible);
        vm.StageViewIndex = 0;
        Assert.Contains("项目运行成功", vm.TerminalOutput);
    }

    [Fact]
    public async Task SameRunCommandCanStopDuringStartupAndCloseKeepsNoActiveSession()
    {
        var store = new JsonProjectStore();
        var project = new ProjectService(store).Create(Path.Combine(root, "project"));
        await using var bridge = new PythonProcessBridge();
        await using var vm = new MainViewModel(new JsonSettingsStore(Path.Combine(root, "settings.json")),
            new InMemoryRecentProjectsStore(), new ProjectSession(), bridge);
        await vm.OpenProject(project.RootPath);
        var run = vm.RunCommand.ExecuteAsync(null);
        Assert.True(vm.IsPythonActive);
        Assert.True(vm.RunCommand.CanExecute(null));
        Assert.Equal("停止", vm.RunButtonText);
        Assert.False(vm.CanChangePythonEnvironment);
        await vm.RunCommand.ExecuteAsync(null);
        await run;
        Assert.Equal("已停止", vm.RuntimeStatus);
        Assert.False(vm.CanSendTerminalInput);
        await vm.CloseProject();
        Assert.False(vm.IsPythonActive);
    }

    [Fact]
    public async Task InterpreterApplicationValidatesPersistsAndRetainsPreviousChoiceOnFailure()
    {
        var store = new JsonSettingsStore(Path.Combine(root, "settings.json"));
        store.Save(new AppSettings(ThemeIndex: 2, AuxiliaryWidth: 600));
        await using var vm = new MainViewModel(store, new InMemoryRecentProjectsStore(), new ProjectSession());
        await vm.ApplyPythonExecutableAsync(PythonEnvironment.DefaultExecutable);
        Assert.Contains("已验证并保存", vm.PythonEnvironmentStatus);
        Assert.True(Path.IsPathFullyQualified(vm.PythonExecutable));
        Assert.Equal(vm.PythonExecutable, store.Load().PythonExecutable);
        Assert.Equal(2, store.Load().ThemeIndex);
        Assert.Equal(600, store.Load().AuxiliaryWidth);
        var executable = vm.PythonExecutable;
        await vm.ApplyPythonExecutableAsync(Path.Combine(root, "invalid.exe"));
        Assert.Contains("验证失败", vm.PythonEnvironmentStatus);
        Assert.Equal(executable, vm.PythonExecutable);
        Assert.Equal(executable, store.Load().PythonExecutable);
        vm.SelectedThemeIndex = 1;
        Assert.Equal(executable, store.Load().PythonExecutable);
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
