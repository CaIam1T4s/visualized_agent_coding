using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using VisualizedAgentCoding.Application.Python;
using VisualizedAgentCoding.Application.Projects;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualizedAgentCoding.Domain.Projects;

namespace happy_agent_coding.ViewModels;

public partial class MainViewModel
{
    private string? pendingDeleteId;
    private CancellationTokenSource? deleteAnalysisCancellation;
    private ScriptDeletionImpactService? deletionImpactService;
    private readonly List<Task> deleteAnalysisTasks = new();
    public event Action<PythonReferenceItem>? DeleteReferenceOpened;
    public ObservableCollection<PythonReferenceItem> DeleteReferences { get; } = [];
    [ObservableProperty] private string deleteReferenceStatus = string.Empty;
    [ObservableProperty] private string deleteReferenceIssues = string.Empty;
    [ObservableProperty] private bool canConfirmDelete;
    [ObservableProperty] private string deleteButtonText = "删除";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsScriptActionDialogOpen))]
    private bool isDeleteConfirmationOpen;

    [ObservableProperty]
    private string deleteConfirmationTitle = string.Empty;

    [ObservableProperty]
    private string deleteConfirmationPath = string.Empty;

    [ObservableProperty]
    private string deleteConfirmationMessage = string.Empty;

    [ObservableProperty]
    private string? deleteError;

    [RelayCommand]
    private void RequestDeleteScript(ProjectScriptItem? item)
    {
        if (activeProject?.FindScript(item?.Id) is not { } script) return;
        CancelRenameScript();
        pendingDeleteId = script.Id;
        var effect = script.Kind switch
        {
            ProjectScriptKind.Object => "将同时删除对应角色、关联的同名脚本和事件绑定。",
            ProjectScriptKind.Backdrop => "将同时删除对应背景、关联的同名脚本和事件绑定。",
            _ => "将删除脚本及其事件绑定。",
        };
        DeleteConfirmationTitle = $"删除 {Path.GetFileName(script.Path)}：";
        DeleteConfirmationPath = script.Path;
        DeleteConfirmationMessage = $"{effect}此操作无法撤销。";
        DeleteError = null;
        IsDeleteConfirmationOpen = true;
        deleteAnalysisCancellation?.Cancel();
        deleteAnalysisCancellation?.Dispose();
        deleteAnalysisCancellation = new CancellationTokenSource();
        deleteAnalysisTasks.RemoveAll(task => task.IsCompleted);
        deleteAnalysisTasks.Add(AnalyzeDeleteImpactAsync(script.Id, deleteAnalysisCancellation.Token));
    }

    private async Task AnalyzeDeleteImpactAsync(string scriptId, CancellationToken token)
    {
        CanConfirmDelete = false;
        DeleteButtonText = "删除";
        DeleteReferences.Clear();
        DeleteReferenceIssues = string.Empty;
        DeleteReferenceStatus = "正在检查引用影响…";
        var project = activeProject!;
        var version = pythonDocumentVersion;
        try
        {
            if (deletionImpactService is null) throw new InvalidOperationException("引用分析不可用。");
            var overlays = OpenScript is { } open
                ? new[] { new PythonSourceDocument(new Uri(project.ToAbsolutePath(open.Path)).AbsoluteUri, version, PythonCode) }
                : Array.Empty<PythonSourceDocument>();
            var result = await Task.Run(() => deletionImpactService.AnalyzeAsync(project, scriptId, version, overlays, token), token);
            if (token.IsCancellationRequested) return;
            result = result.ForCurrent(activeProject, pythonDocumentVersion);
            if (result.State is DeletionImpactState.Failed or DeletionImpactState.Stale)
                throw new InvalidOperationException(result.Error);
            foreach (var location in result.References)
                DeleteReferences.Add(new(location, Path.GetRelativePath(project.RootPath, new Uri(location.Uri).LocalPath)));
            DeleteReferenceStatus = $"发现 {result.AffectedScriptCount} 个脚本、{DeleteReferences.Count} 处引用受影响（含导入）。";
            DeleteReferenceIssues = string.Join(Environment.NewLine, result.Issues.Select(issue =>
                $"{Path.GetRelativePath(project.RootPath, new Uri(issue.Uri).LocalPath)}：{issue.Message}"));
            if (result.State == DeletionImpactState.Incomplete) DeleteReferenceStatus += "分析结果不完整。";
            if (result.References.Length > 0)
                DeleteReferenceIssues = string.Join(Environment.NewLine,
                    new[] { DeleteReferenceIssues, "引用方源码将保留，请手动修复受影响的引用。" }.Where(text => !string.IsNullOrEmpty(text)));
            DeleteButtonText = result.RequiresOverride ? "仍然删除" : "删除";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested) return;
            DeleteReferenceStatus = $"无法完成引用检查：{ex.Message}";
            DeleteReferenceIssues = "引用影响未知。仍然删除后，请手动检查其他脚本的导入。";
            DeleteButtonText = "仍然删除";
        }
        if (!token.IsCancellationRequested) CanConfirmDelete = true;
    }

    [RelayCommand]
    private async Task InspectDeleteReference(PythonReferenceItem? item)
    {
        if (item is null) return;
        CancelDeleteScript();
        if (await OpenReference(item)) DeleteReferenceOpened?.Invoke(item);
    }

    [RelayCommand]
    private void CancelDeleteScript()
    {
        deleteAnalysisCancellation?.Cancel();
        CanConfirmDelete = false;
        pendingDeleteId = null;
        DeleteError = null;
        IsDeleteConfirmationOpen = false;
    }

    [RelayCommand]
    private async Task ConfirmDeleteScript()
    {
        if (!CanConfirmDelete || !IsDeleteConfirmationOpen || activeProject?.FindScript(pendingDeleteId) is not { } script) return;
        try
        {
            var project = activeProject;
            // Preserve the other editor buffer; the target's buffer is deliberately discarded.
            if (openScriptId != script.Id) await SaveActiveProjectAsync();
            if (!ReferenceEquals(activeProject, project)) throw new ProjectOperationException("项目已切换，请重新删除。");
            activeProject = projectSession.DeleteScript(activeProject, script.Id);
            if (openScriptId == script.Id)
            {
                openScriptId = null;
                ResetLoadedScript();
                PythonCode = string.Empty;
                EditorState = "就绪";
            }
            CancelDeleteScript();
            RefreshProjectResources();
            NotifyScriptEditorState();
            SchedulePythonAnalysis();
            ReportResourceMessage(projectSession.HasPendingScriptCleanup(activeProject)
                ? "已从项目移除，文件将在下次打开项目时重试清理。"
                : $"已删除 {Path.GetFileName(script.Path)}");
        }
        catch (Exception ex)
        {
            DeleteError = ex.Message;
        }
    }

}
