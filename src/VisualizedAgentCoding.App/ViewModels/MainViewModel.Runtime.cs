using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualizedAgentCoding.Application.Runtime;
using VisualizedAgentCoding.Application.Python;
using VisualizedAgentCoding.Application.Editor;
using VisualizedAgentCoding.Infrastructure.Python;

namespace happy_agent_coding.ViewModels;

public partial class RuntimeLocationItem(RuntimePresentedFrame presentation, long generation, long workspace) : ObservableObject
{
    public RuntimePresentedFrame Presentation { get; } = presentation;
    public long Generation { get; } = generation;
    public long Workspace { get; } = workspace;
    public string DisplayText => Presentation.DisplayText;
    public string Code => Presentation.Code;
    public string Diagnostic => Presentation.Diagnostic ?? "";
    public bool CanNavigate => Presentation.CanNavigate && !IsStale;
    public bool HasCode => Code.Length > 0;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanNavigate))] private bool isStale;
}

public partial class MainViewModel
{
    private readonly PythonRuntimeProcess runtime = new();
    private CancellationTokenSource? runCancellation;
    private Task runtimeTask = Task.CompletedTask;
    private long runGeneration;
    private readonly ConcurrentQueue<(long Generation, PythonRunEvent Event)> outputQueue = new();
    private readonly DispatcherTimer terminalTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private long queuedCharacters;
    private int droppedOutput;
    private PythonRunRequest? lastRun;
    private long runtimeWorkspace;
    private long runWorkspace;
    [ObservableProperty] private bool canDispatchStageClicks;
    private readonly object stageEventLock = new();
    private (long Generation, RuntimeStageFrame Frame)? pendingStage;
    [ObservableProperty] private bool hasRuntimeError;
    [ObservableProperty] private string runtimeErrorSummary = "";
    [ObservableProperty] private string runtimeErrorText = "";
    [ObservableProperty] private string runtimeErrorExplanation = "";
    [ObservableProperty] private string runtimeDiagnosticDetails = "";
    [ObservableProperty] private string runtimeNavigationStatus = "";
    [ObservableProperty] private RuntimeLocationItem? selectedRuntimeLocation;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsTerminalVisible)), NotifyPropertyChangedFor(nameof(IsStageVisible))]
    private int stageViewIndex = 1;
    public bool IsTerminalVisible => StageViewIndex == 0;
    public bool IsStageVisible => !IsTerminalVisible;
    [ObservableProperty] private string terminalOutput = "";
    [ObservableProperty] private string terminalInput = "";
    [ObservableProperty] private string runtimeStatus = "未运行";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanChangePythonEnvironment)), NotifyPropertyChangedFor(nameof(RunButtonText)), NotifyPropertyChangedFor(nameof(CanEditStage))]
    private bool isPythonActive;
    [ObservableProperty] private bool canSendTerminalInput;
    public string RunButtonText => IsPythonActive ? "停止" : "运行";
    public ObservableCollection<RuntimeLocationItem> RuntimeLocations { get; } = [];
    public event Action<PythonReferenceItem>? RuntimeLocationOpened;

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task Run()
    {
        if (IsPythonActive) { await StopPythonAsync(); return; }
        if (activeProject is null) { EditorState = "尚未打开项目"; return; }
        if (IsPythonEnvironmentBusy || executionBridge is null) { EditorState = "Python 分析服务尚未就绪"; return; }
        ClearRuntimeStage();
        lock (stageEventLock) pendingStage = null;
        var generation = ++runGeneration;
        runCancellation?.Dispose();
        runCancellation = new();
        CanDispatchStageClicks = false;
        IsPythonActive = true;
        CanSendTerminalInput = false;
        ResetRuntimeError();
        lastRun = null;
        runWorkspace = runtimeWorkspace;
        RuntimeStatus = "启动中…";
        // Keep the selected stage/terminal view while running.
        AppendTerminal($"\n──────── {DateTime.Now:HH:mm:ss} · {activeProject.Name} ────────\n");
        terminalTimer.Tick -= OnTerminalTick;
        terminalTimer.Tick += OnTerminalTick;
        terminalTimer.Start();
        runtimeTask = RunProjectCoreAsync(generation, runCancellation.Token);
        await runtimeTask;
    }

    private async Task RunProjectCoreAsync(long generation, CancellationToken token)
    {
        var project = activeProject!;
        try
        {
            await SaveActiveProjectAsync(token, checkUnchangedDisk: true);
            var revision = pythonDocumentVersion;
            var info = await pythonEnvironment.ValidateAsync(EffectivePythonExecutable, token);
            var request = await new ProjectRunService(runtimeStore, executionBridge!).PrepareAsync(project, info.Executable, token);
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(activeProject, project) || revision != pythonDocumentVersion)
                throw new IOException("运行准备期间编辑内容已变化，请重新运行。");
            if (stageService.Load(project).Revision != stageSnapshot.Revision)
                throw new VisualizedAgentCoding.Domain.Projects.ProjectOperationException("运行准备期间舞台布局已被修改，请重新加载舞台后运行。");
            request = request with { InitialStage = stageSnapshot.State, KeepAlive = true,
                StageImageSizes = CollectStageImageSizes(),
                StageCostumeImageSizes = CollectStageCostumeImageSizes() };
            lastRun = request;
            var exit = await runtime.RunAsync(request, message => QueueRuntimeEvent(generation, message), token);
            FlushTerminalEvents();
            RuntimeStatus = exit == 0 ? "已结束 · 退出码 0" : $"运行失败 · 退出码 {exit}";
            AppendTerminal($"\n[{RuntimeStatus}]\n");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        { FlushTerminalEvents(); RuntimeStatus = "已停止"; AppendTerminal("\n[已停止]\n"); }
        catch (RuntimePreparationException ex)
        { FlushTerminalEvents(); SetRuntimeError(ex.Report); RuntimeStatus = ex.Report.Summary; AppendTerminal("\n[运行前检查未通过]\n"); }
        catch (VisualizedAgentCoding.Domain.Projects.ProjectOperationException ex)
        {
            FlushTerminalEvents(); SetRuntimeError(RuntimeErrorPresentationService.PreparationFailure(ex.Message, ex.ToString()));
            RuntimeStatus = "运行准备失败"; AppendTerminal("\n[运行准备失败]\n");
        }
        catch (Exception ex)
        {
            FlushTerminalEvents();
            SetRuntimeError(RuntimeErrorPresentationService.EnvironmentFailure(ex.ToString()));
            RuntimeStatus = "启动或运行失败";
            AppendTerminal("\n[启动或运行失败，请查看错误卡片]\n");
        }
        finally
        {
            CanSendTerminalInput = false;
            CanDispatchStageClicks = false;
            IsPythonActive = false;
            terminalTimer.Stop();
            EditorState = RuntimeStatus;
        }
    }

    public async Task StopPythonAsync()
    {
        if (!IsPythonActive) return;
        CanDispatchStageClicks = false;
        RuntimeStatus = "停止中…";
        CanSendTerminalInput = false;
        runCancellation?.Cancel();
        await runtimeTask;
        runCancellation?.Dispose();
        runCancellation = null;
    }

    private void QueueRuntimeEvent(long generation, PythonRunEvent message)
    {
        if (message.Kind == "stage")
        {
            if (message.Stage is { } frame && frame.SessionId == lastRun?.SessionId)
                lock (stageEventLock)
                    if (generation == runGeneration && (pendingStage is null || frame.Sequence > pendingStage.Value.Frame.Sequence))
                        pendingStage = (generation, frame);
            return;
        }
        // Bound pending text independently of the visible history, even under a print flood.
        if (message.Kind is "stdout" or "stderr")
        {
            if (Interlocked.Add(ref queuedCharacters, message.Text.Length) > 256_000)
            { Interlocked.Add(ref queuedCharacters, -message.Text.Length); Interlocked.Exchange(ref droppedOutput, 1); return; }
        }
        outputQueue.Enqueue((generation, message));
    }
    private void OnTerminalTick(object? sender, EventArgs e) => FlushTerminalEvents();
    private void FlushTerminalEvents()
    {
        (long Generation, RuntimeStageFrame Frame)? stage;
        lock (stageEventLock) { stage = pendingStage; pendingStage = null; }
        if (stage is { } update && update.Generation == runGeneration) ApplyRuntimeStage(update.Frame);
        var text = new System.Text.StringBuilder();
        while (outputQueue.TryDequeue(out var item))
        {
            var message = item.Event;
            if (message.Kind is "stdout" or "stderr") Interlocked.Add(ref queuedCharacters, -message.Text.Length);
            if (item.Generation != runGeneration) continue;
            if (message.Kind == "ready" && !runCancellation!.IsCancellationRequested)
            { RuntimeStatus = "运行中"; CanSendTerminalInput = true; }
            if (message.Kind is "stdout" or "stderr") text.Append(message.Text);
            if (message.Kind == "interactive-ready" && !runCancellation!.IsCancellationRequested)
            { RuntimeStatus = "运行中 · 舞台交互已启用"; CanDispatchStageClicks = true; }
            if (message.Kind == "exception" && runCancellation?.IsCancellationRequested != true)
                SetRuntimeError(new RuntimeErrorPresentationService().Present(message, lastRun?.PresentationSources ?? []));
        }
        if (Interlocked.Exchange(ref droppedOutput, 0) != 0) text.Append("\n[输出过快，部分内容已省略]\n");
        if (text.Length > 0) AppendTerminal(text.ToString());
    }

    public async Task DispatchStageClickAsync(StageObjectItem item)
    {
        if (!IsPythonActive || !CanDispatchStageClicks || !item.IsSprite || !item.ClickEnabledRuntime
            || !StageRenderSprites.Contains(item) || lastRun is null || runWorkspace != runtimeWorkspace) return;
        var generation = runGeneration;
        try { await runtime.SendSpriteClickAsync(lastRun.SessionId, item.ObjectId, runCancellation!.Token); }
        catch (OperationCanceledException) { }
        catch (IOException) { if (generation == runGeneration && IsPythonActive) RuntimeStatus = "当前无法发送舞台点击，请重新运行。"; }
    }

    public async Task DispatchStageBackdropClickAsync()
    {
        if (!IsPythonActive || !CanDispatchStageClicks || lastRun is null || runWorkspace != runtimeWorkspace) return;
        var generation = runGeneration;
        try { await runtime.SendStageClickAsync(lastRun.SessionId, runCancellation!.Token); }
        catch (OperationCanceledException) { }
        catch (IOException) { if (generation == runGeneration && IsPythonActive) RuntimeStatus = "当前无法发送舞台点击，请重新运行。"; }
    }

    public async Task DispatchStageKeyAsync(string key)
    {
        if (!IsPythonActive || !CanDispatchStageClicks || lastRun is null || runWorkspace != runtimeWorkspace) return;
        var generation = runGeneration;
        try { await runtime.SendKeyAsync(lastRun.SessionId, key, runCancellation!.Token); }
        catch (OperationCanceledException) { }
        catch (IOException) { if (generation == runGeneration && IsPythonActive) RuntimeStatus = "当前无法发送按键事件，请重新运行。"; }
    }

    public async Task DispatchPointerAsync(double x, double y)
    {
        if (!IsPythonActive || !CanDispatchStageClicks || lastRun is null || runWorkspace != runtimeWorkspace) return;
        try { await runtime.SendPointerAsync(lastRun.SessionId, x, y, runCancellation!.Token); }
        catch (OperationCanceledException) { }
        catch (IOException) { }
    }

    private void AppendTerminal(string text)
    {
        var value = TerminalOutput + text;
        TerminalOutput = value.Length > 200_000 ? "[较早输出已截断]\n" + value[^180_000..] : value;
    }

    [RelayCommand]
    private void ClearTerminal() { TerminalOutput = ""; ResetRuntimeError(); }

    private void ResetRuntimeError()
    {
        HasRuntimeError = false; RuntimeLocations.Clear(); SelectedRuntimeLocation = null;
        RuntimeErrorSummary = RuntimeErrorText = RuntimeErrorExplanation = RuntimeDiagnosticDetails = RuntimeNavigationStatus = "";
    }

    private void SetRuntimeError(RuntimeErrorReport report)
    {
        HasRuntimeError = true;
        RuntimeErrorSummary = report.Summary; RuntimeErrorText = report.UserText; RuntimeDiagnosticDetails = report.Details;
        RuntimeErrorExplanation = report.Explanation;
        RuntimeLocations.Clear();
        foreach (var frame in report.Frames) RuntimeLocations.Add(new(frame, runGeneration, runWorkspace));
        SelectedRuntimeLocation = report.PrimaryFrameIndex >= 0 ? RuntimeLocations[report.PrimaryFrameIndex] : RuntimeLocations.FirstOrDefault();
        RefreshRuntimeNavigationState();
    }

    private void RefreshRuntimeNavigationState()
    {
        foreach (var item in RuntimeLocations)
        {
            var snapshot = item.Presentation.Snapshot;
            var script = ResolveProjectScript(snapshot?.ScriptId);
            item.IsStale = item.Generation != runGeneration || item.Workspace != runtimeWorkspace || snapshot is null
                || script is null || script.Path != snapshot.Path || script.ObjectId != snapshot.ObjectId
                || OpenScript?.Id == script.Id && PythonCode != snapshot.Source;
        }
        RuntimeNavigationStatus = RuntimeLocations.Any(i => i.Presentation.CanNavigate && i.IsStale)
            ? "源码或项目已变化；此处保留运行时快照，请重新运行以更新定位。" : "";
    }

    [RelayCommand]
    private async Task SendTerminalInput()
    {
        if (!CanSendTerminalInput) return;
        var generation = runGeneration;
        var text = TerminalInput;
        TerminalInput = "";
        try { await runtime.SendInputAsync(text); if (generation == runGeneration) AppendTerminal(text + "\n"); }
        catch (Exception ex) { if (generation == runGeneration) AppendTerminal("\n[输入失败] " + ex.Message + "\n"); }
    }

    [RelayCommand]
    private async Task OpenRuntimeLocation(RuntimeLocationItem? item)
    {
        if (item is null || !item.Presentation.CanNavigate) return;
        RefreshRuntimeNavigationState();
        if (item.IsStale || item.Presentation.Snapshot is not { } snapshot) return;
        var script = ResolveProjectScript(snapshot.ScriptId);
        if (script is null) return;
        try
        {
            var source = OpenScript?.Id == script.Id ? PythonCode : projectSession.LoadScript(activeProject!, script);
            if (source != snapshot.Source) { MarkStale(); return; }
            var frame = item.Presentation.Original;
            var reference = new PythonReferenceItem(new(new Uri(snapshot.AbsolutePath).AbsoluteUri,
                new(frame.Line, frame.Column ?? 1, frame.Line, frame.Column ?? 1), false, frame.Function), script.Path);
            if (await OpenReference(reference))
            {
                RefreshRuntimeNavigationState();
                if (item.IsStale || PythonCode != snapshot.Source) { MarkStale(); return; }
                RuntimeLocationOpened?.Invoke(reference);
            }
            void MarkStale()
            {
                item.IsStale = true;
                RuntimeNavigationStatus = EditorState = "源码已在本次运行后改变，请重新运行以获得准确定位。";
            }
        }
        catch (Exception ex) { RuntimeNavigationStatus = "异常位置不可用，请重新运行。"; RuntimeDiagnosticDetails += "\n" + ex; }
    }
}
