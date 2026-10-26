using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia;
using Avalonia.Styling;
using AvaloniaFluentUI.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using happy_agent_coding.Services;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Domain.Projects;

namespace happy_agent_coding.ViewModels;

public sealed record RecentProjectEntry(string Name, string Path, DateTimeOffset OpenedAt);

public partial class MainViewModel : ViewModelBase
{
    private readonly ISettingsStore settingsStore;
    private readonly IProjectSession projectSession;
    private ProjectWorkspace? activeProject;
    private Dictionary<ProjectScriptKind, bool>? expansionBeforeSearch;
    private readonly ScriptCategory userScripts;
    private readonly ScriptCategory objectScripts;
    private readonly ScriptCategory backdropScripts;
    public ObservableCollection<RecentProjectEntry> RecentProjectsToday { get; } = [];
    public ObservableCollection<RecentProjectEntry> RecentProjectsThisWeek { get; } = [];

    /// <summary>Script families shown in the resource filter, each with its own inline creation editor.</summary>
    public ObservableCollection<ScriptCategory> ScriptCategories { get; } = [];

    public ObservableCollection<ProjectScriptItem> UserScripts => userScripts.Scripts;
    public ObservableCollection<ProjectScriptItem> ObjectScripts => objectScripts.Scripts;
    public ObservableCollection<ProjectScriptItem> BackdropScripts => backdropScripts.Scripts;
    public string ProjectResourceStatus { get; private set; } = "打开项目后显示脚本";

    /// <summary>Transient feedback (e.g. a created script) shown at the explorer's bottom left.</summary>
    public string ProjectResourceMessage { get; private set; } = string.Empty;

    public ObservableCollection<RecentProjectEntry> RecentProjectsThisMonth { get; } = [];
    public ObservableCollection<RecentProjectEntry> RecentProjectsOlder { get; } = [];
    public bool HasRecentProjects => RecentProjectsToday.Count + RecentProjectsThisWeek.Count +
        RecentProjectsThisMonth.Count + RecentProjectsOlder.Count > 0;

    private void ReportResourceMessage(string message)
    {
        ProjectResourceMessage = message;
        OnPropertyChanged(nameof(ProjectResourceMessage));
    }

    public MainViewModel()
        : this(new JsonSettingsStore(), new ProjectSession())
    {
    }

    public MainViewModel(ISettingsStore settingsStore)
        : this(settingsStore, new ProjectSession())
    {
    }

    public MainViewModel(ISettingsStore settingsStore, IProjectSession projectSession)
    {
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(projectSession);
        this.settingsStore = settingsStore;
        this.projectSession = projectSession;
        var settings = this.settingsStore.Load();

        userScripts = new ScriptCategory(ProjectScriptKind.User, "用户脚本", "新建用户脚本", CreateScript);
        objectScripts = new ScriptCategory(ProjectScriptKind.Object, "对象脚本", "新建对象脚本", CreateScript);
        backdropScripts = new ScriptCategory(ProjectScriptKind.Backdrop, "背景脚本", "新建背景脚本", CreateScript);
        ScriptCategories.Add(userScripts);
        ScriptCategories.Add(objectScripts);
        ScriptCategories.Add(backdropScripts);

        sidebarDisplayModeIndex = Math.Clamp(settings.SidebarDisplayModeIndex, 0, 2);
        selectedThemeIndex = Math.Clamp(settings.ThemeIndex, 0, 2);
        topPanelRatio = LayoutSettingValues.ClampTopPanelRatio(settings.TopPanelRatio);
        auxiliaryWidth = LayoutSettingValues.ClampAuxiliaryWidth(settings.AuxiliaryWidth);
        PythonCode = "def main():\n    print(\"Hello, world!\")\n\n\nif __name__ == \"__main__\":\n    main()";
        ApplyTheme(selectedThemeIndex);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineNumbers))]
    [NotifyPropertyChangedFor(nameof(EditorState))]
    private string pythonCode = string.Empty;

    [ObservableProperty]
    private string editorState = "就绪";

    [ObservableProperty]
    private int selectedThemeIndex;

    [ObservableProperty]
    private int sidebarDisplayModeIndex;

    [ObservableProperty]
    private double topPanelRatio = LayoutSettingValues.DefaultTopPanelRatio;

    [ObservableProperty]
    private double auxiliaryWidth = LayoutSettingValues.DefaultAuxiliaryWidth;

    [ObservableProperty]
    private int editorModeIndex = 0;

    [ObservableProperty]
    private bool isSettingsOpen;

    [ObservableProperty]
    private bool isProjectManagerOpen;

    [ObservableProperty]
    private string projectStatus = "未打开项目";

    /// <summary>Message shown when a folder was rejected as a project.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProjectError))]
    private string? projectError;

    public bool HasProjectError => !string.IsNullOrWhiteSpace(ProjectError);

    public void ReportProjectError(string message) => ProjectError = message;

    public void ClearProjectError() => ProjectError = null;

    [ObservableProperty]
    private string resourceSearchQuery = string.Empty;

    /// <summary>0 = resource filter, 1 = object manager (placeholder).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsResourceFilterMode))]
    [NotifyPropertyChangedFor(nameof(IsObjectManagerMode))]
    private int resourceExplorerModeIndex;

    public string FileName => activeProject is null ? "main.py" : Path.GetFileName(activeProject.MainScriptPath);

    public string? ProjectName => activeProject?.Name;

    public string ProjectDisplayName => activeProject?.Name ?? "未打开项目";

    public string? ProjectRootPath => activeProject?.RootPath;

    public bool HasOpenProject => activeProject is not null;

    /// <summary>True while the resource explorer lists scripts (the object manager is a placeholder).</summary>
    public bool IsResourceFilterMode => ResourceExplorerModeIndex == 0;

    public bool IsObjectManagerMode => ResourceExplorerModeIndex == 1;

    public bool CanCreateScript => activeProject is not null;

    public void CreateProject(string rootPath, string? name = null) => ActivateProject(projectSession.Create(rootPath, name));

    public void OpenProject(string rootPath) => ActivateProject(projectSession.Open(rootPath));

    public void CloseProject()
    {
        SaveActiveProject();
        activeProject = null;
        PythonCode = string.Empty;
        ResetExplorerState();
        RefreshProjectResources();
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(ProjectName));
        OnPropertyChanged(nameof(ProjectDisplayName));
        OnPropertyChanged(nameof(ProjectRootPath));
        OnPropertyChanged(nameof(HasOpenProject));
        OnPropertyChanged(nameof(CanCreateScript));
        ProjectStatus = "未打开项目";
    }

    [RelayCommand]
    private void OpenProjectManager() => IsProjectManagerOpen = true;

    [RelayCommand]
    private void CloseProjectManager() => IsProjectManagerOpen = false;

    [RelayCommand]
    private void CloseActiveProject() => CloseProject();

    [RelayCommand]
    private void OpenRecentProject(string path)
    {
        try
        {
            OpenProject(path);
        }
        catch (Exception ex) when (ex is ProjectFormatException or IOException or UnauthorizedAccessException or JsonException)
        {
            ReportProjectError(ex.Message);
        }
    }

    private void ActivateProject(ProjectWorkspace project)
    {
        SaveActiveProject();
        activeProject = project;
        ClearProjectError();
        AddRecentProject(project);
        PythonCode = projectSession.LoadMainScript(project);
        ResetExplorerState();
        CollapseCategories();
        RefreshProjectResources();
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(ProjectName));
        OnPropertyChanged(nameof(ProjectDisplayName));
        OnPropertyChanged(nameof(ProjectRootPath));
        OnPropertyChanged(nameof(HasOpenProject));
        OnPropertyChanged(nameof(CanCreateScript));
        ProjectStatus = project.Name;
        IsProjectManagerOpen = false;
    }

    private void SaveActiveProject()
    {
        if (activeProject is not null)
            projectSession.SaveMainScript(activeProject, PythonCode);
    }

    /// <summary>Clears the search box so a closed project does not keep a stale filter.</summary>
    private void ResetExplorerState()
    {
        ResourceSearchQuery = string.Empty;
        ProjectResourceMessage = string.Empty;
        OnPropertyChanged(nameof(ProjectResourceMessage));
    }

    partial void OnResourceSearchQueryChanged(string value)
    {
        RefreshProjectResources();
    }

    private void RefreshProjectResources()
    {
        foreach (var category in ScriptCategories)
        {
            category.Scripts.Clear();
            category.CanCreate = activeProject is not null;
        }

        if (activeProject is null)
        {
            CollapseCategories();
            ProjectResourceMessage = string.Empty;
            OnPropertyChanged(nameof(ProjectResourceMessage));
            ProjectResourceStatus = "打开项目后显示脚本";
            OnPropertyChanged(nameof(ProjectResourceStatus));
            return;
        }

        var query = ResourceSearchQuery.Trim();
        foreach (var script in activeProject.Manifest.Scripts)
        {
            var displayName = Path.GetFileNameWithoutExtension(script.Path);
            if (!MatchesQuery(displayName, script.Path, query))
                continue;

            CategoryFor(script.Kind).Scripts.Add(
                new ProjectScriptItem(script.Id, displayName, script.Path, script.Kind));
        }

        foreach (var category in ScriptCategories)
            SortScripts(category);

        UpdateCategoryExpansion(query);

        var count = ScriptCategories.Sum(category => category.Scripts.Count);
        ProjectResourceStatus = count == 0
            ? string.IsNullOrWhiteSpace(query) ? "当前项目暂无脚本" : "没有匹配的脚本"
            : $"{count} 项脚本";
        OnPropertyChanged(nameof(ProjectResourceStatus));
    }

    private static bool MatchesQuery(string displayName, string relativePath, string query) =>
        string.IsNullOrWhiteSpace(query)
        || displayName.Contains(query, StringComparison.OrdinalIgnoreCase)
        || relativePath.Contains(query, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// While filtering, only categories with matches stay open so every hit is visible. The
    /// expansion the user chose before the query is saved once and put back when the box clears.
    /// </summary>
    private void UpdateCategoryExpansion(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            RestoreCategoryExpansion();
            return;
        }

        expansionBeforeSearch ??= ScriptCategories.ToDictionary(category => category.Kind, category => category.IsExpanded);
        foreach (var category in ScriptCategories)
            category.IsExpanded = category.Scripts.Count > 0;
    }

    private void RestoreCategoryExpansion()
    {
        if (expansionBeforeSearch is null)
            return;

        foreach (var category in ScriptCategories)
        {
            if (expansionBeforeSearch.TryGetValue(category.Kind, out var isExpanded))
                category.IsExpanded = isExpanded;
        }

        expansionBeforeSearch = null;
    }

    /// <summary>Collapses every category, used when the project list no longer applies.</summary>
    private void CollapseCategories()
    {
        expansionBeforeSearch = null;
        foreach (var category in ScriptCategories)
        {
            category.IsExpanded = false;
            category.CompleteCreation();
        }
    }

    private static void SortScripts(ScriptCategory category)
    {
        var ordered = category.Scripts
            .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        category.Scripts.Clear();
        foreach (var item in ordered)
            category.Scripts.Add(item);
    }

    private ScriptCategory CategoryFor(ProjectScriptKind kind) => kind switch
    {
        ProjectScriptKind.Object => objectScripts,
        ProjectScriptKind.Backdrop => backdropScripts,
        _ => userScripts,
    };

    /// <summary>Creates the script named in a category's inline editor and refreshes the project files.</summary>
    private void CreateScript(ScriptCategory category)
    {
        if (activeProject is null)
        {
            category.ReportError("尚未打开项目。");
            return;
        }

        try
        {
            var creation = projectSession.CreateScript(activeProject, category.Kind, category.NewScriptName);
            activeProject = creation.Project;
            category.CompleteCreation();
            RefreshProjectResources();
            ReportResourceMessage($"已创建 {Path.GetFileName(creation.Script.Path)}");
        }
        catch (Exception ex) when (ex is ProjectOperationException or IOException or UnauthorizedAccessException)
        {
            category.ReportError(ex.Message);
        }
    }

    private void AddRecentProject(ProjectWorkspace project)
    {
        var openedAt = DateTimeOffset.Now;
        var entry = new RecentProjectEntry(project.Name, project.RootPath, openedAt);
        var existing = RecentProjectsToday.Concat(RecentProjectsThisWeek)
            .Concat(RecentProjectsThisMonth).Concat(RecentProjectsOlder)
            .Where(item => !string.Equals(item.Path, project.RootPath, StringComparison.OrdinalIgnoreCase))
            .Append(entry).OrderByDescending(item => item.OpenedAt).ToArray();

        RecentProjectsToday.Clear();
        RecentProjectsThisWeek.Clear();
        RecentProjectsThisMonth.Clear();
        RecentProjectsOlder.Clear();
        foreach (var item in existing)
        {
            var age = openedAt - item.OpenedAt;
            var bucket = age.TotalDays < 1 ? RecentProjectsToday
                : age.TotalDays < 7 ? RecentProjectsThisWeek
                : age.TotalDays < 30 ? RecentProjectsThisMonth
                : RecentProjectsOlder;
            bucket.Add(item);
        }
        OnPropertyChanged(nameof(HasRecentProjects));
    }

    [RelayCommand]
    private void OpenSettings()
    {
        IsSettingsOpen = true;
    }

    [RelayCommand]
    private void CloseSettings()
    {
        IsSettingsOpen = false;
    }

    partial void OnSelectedThemeIndexChanged(int value)
    {
        ApplyTheme(value);
        SaveSettings(includeLayout: false);
    }

    partial void OnSidebarDisplayModeIndexChanged(int value)
    {
        SaveSettings(includeLayout: false);
    }

    public void UpdateLayout(double topPanelRatio, double? auxiliaryWidth = null)
    {
        TopPanelRatio = LayoutSettingValues.ClampTopPanelRatio(topPanelRatio);
        if (auxiliaryWidth.HasValue)
            AuxiliaryWidth = LayoutSettingValues.ClampAuxiliaryWidth(auxiliaryWidth.Value);
    }

    public void SaveLayout()
    {
        SaveSettings(includeLayout: true);
    }

    private static void ApplyTheme(int value)
    {
        if (Application.Current is not { } application)
            return;

        AvaloniaFluentTheme.Instance.PreferSystemTheme = value == 0;
        AvaloniaFluentTheme.Instance.CurrentTheme = value switch
        {
            1 => ThemeVariant.Light,
            2 => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
        application.RequestedThemeVariant = AvaloniaFluentTheme.Instance.CurrentTheme;
    }

    private void SaveSettings(bool includeLayout)
    {
        try
        {
            var savedSettings = settingsStore.Load();
            settingsStore.Save(new AppSettings(
                SidebarDisplayModeIndex,
                SelectedThemeIndex,
                includeLayout ? TopPanelRatio : savedSettings.TopPanelRatio,
                includeLayout ? AuxiliaryWidth : savedSettings.AuxiliaryWidth));
        }
        catch (IOException)
        {
            // A settings write failure must not prevent the editor from running.
        }
        catch (UnauthorizedAccessException)
        {
            // A settings write failure must not prevent the editor from running.
        }
    }

    public string LineNumbers => string.Join(Environment.NewLine,
        Enumerable.Range(1, Math.Max(1, PythonCode.Split('\n').Length)));

    [RelayCommand]
    private void CycleSidebarDisplayMode()
    {
        SidebarDisplayModeIndex = (SidebarDisplayModeIndex + 1) % 3;
    }

    [RelayCommand]
    private void Save()
    {
        try
        {
            SaveActiveProject();
            EditorState = activeProject is null ? "尚未打开项目" : "已保存";
        }
        catch (IOException)
        {
            EditorState = "保存失败";
        }
        catch (UnauthorizedAccessException)
        {
            EditorState = "保存失败";
        }
    }

    [RelayCommand]
    private void Run()
    {
        EditorState = "等待运行时接入";
    }
}
