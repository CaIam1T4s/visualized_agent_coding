namespace happy_agent_coding.ViewModels;

public partial class MainViewModel
{
    public bool IsScriptActionDialogOpen => IsDeleteConfirmationOpen || IsRenamePreviewOpen || IsReloadConfirmationOpen;

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
        CancelRenameScript();
        CancelDeleteScript();
        CancelReloadScript();
    }
}
