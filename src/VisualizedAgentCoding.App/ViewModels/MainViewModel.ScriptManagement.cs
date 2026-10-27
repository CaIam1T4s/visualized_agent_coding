using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualizedAgentCoding.Domain.Projects;

namespace happy_agent_coding.ViewModels;

public partial class MainViewModel
{
    public ScriptRenameState? ScriptRename { get; private set; }
    private string? pendingDeleteId;

    [ObservableProperty]
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
    private void StartRenameScript(ProjectScriptItem? item)
    {
        if (activeProject?.FindScript(item?.Id) is not { } script || IsDeleteConfirmationOpen)
            return;
        if (script.Kind != ProjectScriptKind.User)
        {
            ReportResourceMessage("角色和背景暂不支持重命名，需先支持类名与引用同步。");
            return;
        }

        ScriptRename = new ScriptRenameState(script.Id, Path.GetFileNameWithoutExtension(script.Path),
            ConfirmRenameScript, CancelRenameScript);
        CategoryFor(script.Kind).CompleteCreation();
        CategoryFor(script.Kind).IsExpanded = true;
        RefreshProjectResources();
    }

    [RelayCommand]
    private void CancelRenameScript()
    {
        if (ScriptRename is null) return;
        ScriptRename = null;
        RefreshProjectResources();
    }

    private void ConfirmRenameScript()
    {
        if (ScriptRename is not { } rename || activeProject is null) return;
        try
        {
            SaveActiveProject();
            activeProject = projectSession.RenameScript(activeProject, rename.ScriptId, rename.Name);
            var path = activeProject.FindScript(rename.ScriptId)!.Path;
            ScriptRename = null;
            RefreshProjectResources();
            NotifyScriptEditorState();
            ReportResourceMessage(projectSession.HasPendingScriptCleanup(activeProject)
                ? $"已重命名为 {Path.GetFileName(path)}，旧文件将在下次打开项目时重试清理。"
                : $"已重命名为 {Path.GetFileName(path)}");
        }
        catch (Exception ex) when (ex is ProjectOperationException or ProjectFormatException or IOException or UnauthorizedAccessException)
        {
            rename.Error = ex.Message;
        }
    }

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
    }

    [RelayCommand]
    private void CancelDeleteScript()
    {
        pendingDeleteId = null;
        DeleteError = null;
        IsDeleteConfirmationOpen = false;
    }

    [RelayCommand]
    private void ConfirmDeleteScript()
    {
        if (!IsDeleteConfirmationOpen || activeProject?.FindScript(pendingDeleteId) is not { } script) return;
        try
        {
            // Preserve the other editor buffer; the target's buffer is deliberately discarded.
            if (openScriptId != script.Id) SaveActiveProject();
            activeProject = projectSession.DeleteScript(activeProject, script.Id);
            if (openScriptId == script.Id)
            {
                openScriptId = null;
                PythonCode = string.Empty;
                EditorState = "就绪";
            }
            CancelDeleteScript();
            RefreshProjectResources();
            NotifyScriptEditorState();
            ReportResourceMessage(projectSession.HasPendingScriptCleanup(activeProject)
                ? "已从项目移除，文件将在下次打开项目时重试清理。"
                : $"已删除 {Path.GetFileName(script.Path)}");
        }
        catch (Exception ex) when (ex is ProjectOperationException or ProjectFormatException or IOException or UnauthorizedAccessException)
        {
            DeleteError = ex.Message;
        }
    }

    private void NotifyScriptEditorState()
    {
        SchedulePythonAnalysis();
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(OpenScript));
        OnPropertyChanged(nameof(IsEditorVisible));
        OnPropertyChanged(nameof(ShowOpenProjectPrompt));
        OnPropertyChanged(nameof(EditorEmptyPromptTitle));
        OnPropertyChanged(nameof(EditorEmptyPromptMessage));
    }

    private void ResetScriptActions()
    {
        ScriptRename = null;
        CancelDeleteScript();
    }
}
