using System;
using System.Linq;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using VisualizedAgentCoding.Application.Ports;
using VisualizedAgentCoding.Application.Python;

namespace happy_agent_coding.ViewModels;

public partial class MainViewModel : IAsyncDisposable
{
    private PythonDiagnosticsCoordinator? pythonDiagnostics;
    private long pythonDocumentVersion;
    public Task PythonAnalysisTask { get; private set; } = Task.CompletedTask;
    public ObservableCollection<PythonDiagnostic> PythonDiagnostics { get; } = [];
    public bool HasPythonDiagnostics => EditorDiagnostics.Any();
    public int PythonErrorCount => EditorDiagnostics.Count(d => d.Severity == "error");
    public string PythonErrorListTitle => $"当前程序共有 {PythonErrorCount} 个错误";
    public string PythonErrorSummary => PythonAnalysisStatus switch
    {
        "" => "未检查",
        "语法检查中…" => "检查中…",
        _ when PythonAnalysisStatus.StartsWith("语法检查不可用：", StringComparison.Ordinal) => "检查不可用",
        _ => $"{PythonErrorCount} 个错误",
    };
    public string EditorOperationStatus => EditorState == "就绪" ? string.Empty : EditorState;

    partial void OnEditorStateChanged(string value) => OnPropertyChanged(nameof(EditorOperationStatus));
    partial void OnPythonAnalysisStatusChanged(string value) => OnPropertyChanged(nameof(PythonErrorSummary));

    [ObservableProperty]
    private string pythonAnalysisStatus = string.Empty;

    private void InitializePythonDiagnostics(IPythonBridge? bridge)
    {
        if (bridge is null) return;
        pythonDiagnostics = new PythonDiagnosticsCoordinator(bridge, update =>
        {
            PythonDiagnostics.Clear();
            foreach (var diagnostic in update.Result?.Diagnostics ?? []) PythonDiagnostics.Add(diagnostic);
            PythonAnalysisStatus = update.Status;
            NotifyEditorDiagnosticsChanged();
        }, dispatch: action =>
        {
            if (Dispatcher.UIThread.CheckAccess()) action();
            else Dispatcher.UIThread.Post(action);
        });
    }

    private void NotifyEditorDiagnosticsChanged()
    {
        if (PythonAnalysisStatus == "语法正常" || PythonAnalysisStatus.EndsWith(" 个语法错误", StringComparison.Ordinal)
            || PythonAnalysisStatus.StartsWith("检查完成：", StringComparison.Ordinal))
            PythonAnalysisStatus = PythonErrorCount == 0 ? "语法正常" : EditorDiagnostics.Any(d => d.Category == "import-rule")
                ? $"检查完成：{PythonErrorCount} 个错误" : $"{PythonErrorCount} 个语法错误";
        OnPropertyChanged(nameof(HasPythonDiagnostics));
        OnPropertyChanged(nameof(PythonErrorCount));
        OnPropertyChanged(nameof(PythonErrorListTitle));
        OnPropertyChanged(nameof(PythonErrorSummary));
        OnPropertyChanged(nameof(EditorDiagnostics));
    }

    partial void OnPythonCodeChanged(string value)
    {
        RefreshRuntimeNavigationState();
        UpdateOpenTemplateDecoration();
        SchedulePythonAnalysis();
        if (IsBlockEditorMode) ScheduleBlockEditorRefresh();
    }

    private void SchedulePythonAnalysis()
    {
        if (!navigatingReference) ResetReferenceQuery();
        var version = ++pythonDocumentVersion;
        if (pythonDiagnostics is null) return;
        PythonParseRequest? document = null;
        if (activeProject is not null && OpenScript is { } script)
            document = new PythonParseRequest(new Uri(activeProject.ToAbsolutePath(script.Path)).AbsoluteUri, version, PythonCode, VisualizedAgentCoding.Application.Editor.ScriptImportService.Context(activeProject, script, editorProjection?.Document));
        PythonAnalysisTask = pythonDiagnostics.UpdateAsync(document);
    }

    public async ValueTask DisposeAsync()
    {
        DisposeStageImages();
        await StopPythonAsync();
        await runtime.DisposeAsync();
        environmentLifetime.Cancel();
        await environmentTask;
        renameCancellation?.Cancel();
        deleteAnalysisCancellation?.Cancel();
        ResetReferenceQuery();
        await Task.WhenAll(renameTasks);
        renameCancellation?.Dispose();
        await Task.WhenAll(deleteAnalysisTasks);
        deleteAnalysisCancellation?.Dispose();
        await Task.WhenAll(referenceTasks);
        if (pythonDiagnostics is null) return;
        var previous = pythonDiagnostics;
        pythonDiagnostics = null;
        await previous.DisposeAsync();
    }
}
