using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace happy_agent_coding.ViewModels;

public partial class MainViewModel
{
    private string? reloadTargetId;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsScriptActionDialogOpen))]
    private bool isReloadConfirmationOpen;
    [ObservableProperty] private string? reloadError;

    [RelayCommand]
    private void RequestReloadScript()
    {
        if (OpenScript is null || IsScriptActionDialogOpen) return;
        reloadTargetId = openScriptId;
        ReloadError = null;
        IsReloadConfirmationOpen = true;
    }

    [RelayCommand]
    private void CancelReloadScript()
    {
        reloadTargetId = null;
        IsReloadConfirmationOpen = false;
        ReloadError = null;
    }

    [RelayCommand]
    private async Task ConfirmReloadScript()
    {
        if (!IsReloadConfirmationOpen || OpenScript is not { } script || script.Id != reloadTargetId) return;
        await scriptSaveGate.WaitAsync();
        try
        {
            if (activeProject is null || openScriptId != reloadTargetId) return;
            ++editorNavigationGeneration;
            var source = projectSession.LoadScript(activeProject, script);
            RememberLoadedScript(script.Id, source);
            PythonCode = source;
            CancelReloadScript();
            EditorState = "已重新加载磁盘文件";
            SchedulePythonAnalysis();
        }
        catch (Exception ex) { ReloadError = ex.Message; }
        finally { scriptSaveGate.Release(); }
    }
}
