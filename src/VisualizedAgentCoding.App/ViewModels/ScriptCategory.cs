using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualizedAgentCoding.Domain.Projects;

namespace happy_agent_coding.ViewModels;

/// <summary>A script shown in the resource filter: stable id plus the name and path to display.</summary>
public sealed record ProjectScriptItem(
    string Id,
    string DisplayName,
    string RelativePath,
    ProjectScriptKind Kind,
    bool IsOpen = false,
    ScriptRenameState? RenameState = null,
    int? TemplateVersion = null)
{
    public bool IsRenaming => RenameState is not null;
    public bool NeedsTemplateUpgrade => Kind != ProjectScriptKind.User && TemplateVersion is > 0 and < VisualizedAgentCoding.Application.Editor.ObjectScripts.ObjectScriptTemplateVersion.Current;
    public string TemplateUpgradeHint => $"此脚本使用旧版模板（v{TemplateVersion}）。请打开脚本，在“更多”中选择“升级脚本模板…”进行升级。";
}

/// <summary>One script family (user/object/backdrop) with its own inline creation state.</summary>
public partial class ScriptCategory : ObservableObject
{
    private readonly Action<ScriptCategory> createRequest;

    public ScriptCategory(
        ProjectScriptKind kind,
        string title,
        string createMenuHeader,
        Action<ScriptCategory> createRequest)
    {
        ArgumentNullException.ThrowIfNull(createRequest);
        Kind = kind;
        Title = title;
        CreateMenuHeader = createMenuHeader;
        this.createRequest = createRequest;
        Scripts.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
    }

    public ProjectScriptKind Kind { get; }

    public string Title { get; }

    /// <summary>Label of the inline button and right-click menu item that starts creation.</summary>
    public string CreateMenuHeader { get; }

    public ObservableCollection<ProjectScriptItem> Scripts { get; } = [];

    public bool IsEmpty => Scripts.Count == 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCollapsed))]
    private bool isExpanded;

    /// <summary>Inverse of <see cref="IsExpanded"/> so the chevron glyph needs no negated binding.</summary>
    public bool IsCollapsed => !IsExpanded;

    [ObservableProperty]
    private bool isCreating;

    /// <summary>False when no project is open, so creation is unavailable.</summary>
    [ObservableProperty]
    private bool canCreate;

    [ObservableProperty]
    private string newScriptName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    /// <summary>Opens the inline name editor and makes sure the new script will be visible.</summary>
    public void StartCreating()
    {
        if (!CanCreate)
            return;

        ErrorMessage = null;
        NewScriptName = string.Empty;
        IsCreating = true;
        IsExpanded = true;
    }

    /// <summary>Closes the inline editor after the script was created.</summary>
    public void CompleteCreation()
    {
        IsCreating = false;
        NewScriptName = string.Empty;
        ErrorMessage = null;
    }

    public void ReportError(string message) => ErrorMessage = message;

    [RelayCommand]
    private void StartCreate() => StartCreating();

    [RelayCommand]
    private void CancelCreate()
    {
        IsCreating = false;
        NewScriptName = string.Empty;
        ErrorMessage = null;
    }

    [RelayCommand]
    private void ConfirmCreate()
    {
        if (!IsCreating)
            IsCreating = true;

        createRequest(this);
    }
}
