using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Styling;
using AvaloniaFluentUI.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using happy_agent_coding.Services;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Application.Ports;
using VisualizedAgentCoding.Application.Editor;
using VisualizedAgentCoding.Infrastructure.Python;
using VisualizedAgentCoding.Domain.Projects;
using VisualizedAgentCoding.Infrastructure.Projects;

namespace happy_agent_coding.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly ISettingsStore settingsStore;
    private readonly RecentProjectsService recentProjectsService;
    private readonly IProjectSession projectSession;
    private ProjectWorkspace? activeProject;
    private string? openScriptId;
    private Dictionary<ProjectScriptKind, bool>? expansionBeforeSearch;
    private readonly ScriptCategory userScripts;
    private readonly ScriptCategory objectScripts;
    private readonly ScriptCategory backdropScripts;
    public ObservableCollection<RecentProjectEntry> RecentProjectsToday { get; } = [];
    public ObservableCollection<RecentProjectEntry> RecentProjectsThisWeek { get; } = [];

    /// <summary>Script families shown in the resource filter, each with its own inline creation editor.</summary>
    public ObservableCollection<ScriptCategory> ScriptCategories { get; } = [];

    /// <summary>Indexed object scripts shown as sprites in the object manager.</summary>
    public ObservableCollection<StageObjectItem> StageSprites { get; } = [];

    /// <summary>Indexed backdrop scripts shown in the object manager.</summary>
    public ObservableCollection<StageObjectItem> StageBackdrops { get; } = [];

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
        : this(new JsonSettingsStore(), new JsonRecentProjectsStore(), new ProjectSession(), createAnalysisBridge: true)
    {
    }

    public MainViewModel(ISettingsStore settingsStore)
        : this(settingsStore, new JsonRecentProjectsStore(), new ProjectSession())
    {
    }

    public MainViewModel(ISettingsStore settingsStore, IProjectSession projectSession)
        : this(settingsStore, new JsonRecentProjectsStore(), projectSession)
    {
    }

    public MainViewModel(
        ISettingsStore settingsStore,
        IRecentProjectsStore recentProjectsStore,
        IProjectSession projectSession,
        IPythonBridge? pythonBridge = null,
        IAutoSavePolicyProvider? autoSavePolicies = null,
        IProjectStore? analysisStore = null,
        bool createAnalysisBridge = false)
    {
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(recentProjectsStore);
        ArgumentNullException.ThrowIfNull(projectSession);
        this.settingsStore = settingsStore;
        this.projectSession = projectSession;
        recentProjectsService = new RecentProjectsService(recentProjectsStore, projectSession);
        var settings = this.settingsStore.Load();
        pythonExecutable = settings.PythonExecutable;
        pythonExecutableDraft = settings.PythonExecutable;
        ownsAnalysisBridge = createAnalysisBridge;
        runtimeStore = analysisStore ?? Composition.AnalysisComposition.CreateStore();
        if (createAnalysisBridge) pythonBridge = CreateConfiguredBridge();
        executionBridge = pythonBridge;

        SetRecentProjects(recentProjectsService.LoadValidProjects());

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
        ApplyTheme(selectedThemeIndex);
        InitializePythonDiagnostics(pythonBridge);
        InitializeReferences(pythonBridge, runtimeStore);
        InitializeExitAutoSave(autoSavePolicies);
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

    /// <summary>0 = resource filter, 1 = object manager.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsResourceFilterMode))]
    [NotifyPropertyChangedFor(nameof(IsObjectManagerMode))]
    private int resourceExplorerModeIndex;

    /// <summary>0 = sprites, 1 = backdrops.</summary>
    [ObservableProperty]
    private int objectManagerTabIndex;

    [ObservableProperty]
    private StageObjectItem? selectedSprite;

    [ObservableProperty]
    private StageObjectItem? selectedBackdrop;

    /// <summary>Backdrop drawn on the stage: the selected one, otherwise the first listed.</summary>
    public StageObjectItem? CurrentBackdrop => StageBackdrops.FirstOrDefault(b => b.ObjectId == currentBackdropId) ?? StageBackdrops.FirstOrDefault();

    partial void OnSelectedBackdropChanged(StageObjectItem? value)
    {
        if (!restoringStage && !IsPythonActive && !hasRuntimePreview && value is not null && currentBackdropId != value.ObjectId)
        { ClearRuntimeStage(); currentBackdropId = value.ObjectId; stageDirty = true; }
        OnPropertyChanged(nameof(CurrentBackdrop));
        OnPropertyChanged(nameof(DisplayBackdrop));
    }

    public string FileName =>
        OpenScript is { } script ? Path.GetFileName(script.Path) : string.Empty;

    /// <summary>The editor is shown only while an indexed script is open.</summary>
    public bool IsEditorVisible => OpenScript is not null;

    public bool ShowOpenProjectPrompt => !HasOpenProject;

    public string EditorEmptyPromptTitle => HasOpenProject ? "尚未打开脚本" : "尚未打开项目";

    public string EditorEmptyPromptMessage => HasOpenProject
        ? "在资源管理器中打开一个脚本，或选择舞台上的对象。"
        : "打开或新建一个项目后，即可在此编辑脚本。";

    /// <summary>The indexed script currently shown in the editor, or null when none is open.</summary>
    public ProjectScriptEntry? OpenScript =>
        ResolveProjectScript(openScriptId);

    public string? ProjectName => activeProject?.Name;

    public string ProjectDisplayName => activeProject?.Name ?? "未打开项目";

    public string? ProjectRootPath => activeProject?.RootPath;

    public bool HasOpenProject => activeProject is not null;

    /// <summary>The 480×360 preview is shown only while a project is open.</summary>
    public bool ShowStageCanvas => HasOpenProject;

    /// <summary>True while the resource explorer lists scripts instead of stage objects.</summary>
    public bool IsResourceFilterMode => ResourceExplorerModeIndex == 0;

    public bool IsObjectManagerMode => ResourceExplorerModeIndex == 1;

    public bool CanCreateScript => activeProject is not null;

    public Task CreateProject(string rootPath, string? name = null) => ActivateProject(projectSession.Create(rootPath, name));

    public async Task CreateProjectInDirectory(string parentDirectory, string name)
    {
        // Check before saving, and recheck immediately before creation in the session.
        ProjectCreationTarget.Resolve(parentDirectory, name);
        await SaveActiveProjectAsync();
        await ActivateProject(projectSession.CreateInDirectory(parentDirectory, name));
    }

    public Task OpenProject(string rootPath) => ActivateProject(projectSession.Open(rootPath));

    public async Task CloseProject()
    {
        var navigation = ++editorNavigationGeneration;
        await SaveActiveProjectAsync();
        if (navigation != editorNavigationGeneration) return;
        await StopPythonAsync();
        if (navigation != editorNavigationGeneration) return;
        activeProject = null;
        runtimeWorkspace++;
        RefreshRuntimeNavigationState();
        openScriptId = null;
        ResetLoadedScript();
        PythonCode = string.Empty;
        ResetExplorerState();
        ObjectManagerTabIndex = 0;
        RefreshProjectResources();
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(OpenScript));
        OnPropertyChanged(nameof(ProjectName));
        OnPropertyChanged(nameof(ProjectDisplayName));
        OnPropertyChanged(nameof(ProjectRootPath));
        OnPropertyChanged(nameof(HasOpenProject));
        OnPropertyChanged(nameof(ShowStageCanvas));
        OnPropertyChanged(nameof(IsEditorVisible));
        OnPropertyChanged(nameof(ShowOpenProjectPrompt));
        OnPropertyChanged(nameof(EditorEmptyPromptTitle));
        OnPropertyChanged(nameof(EditorEmptyPromptMessage));
        OnPropertyChanged(nameof(CanCreateScript));
        ProjectStatus = "未打开项目";
    }

    [RelayCommand]
    private void OpenProjectManager() => IsProjectManagerOpen = true;

    [RelayCommand]
    private void CloseProjectManager() => IsProjectManagerOpen = false;

    [RelayCommand]
    private async Task CloseActiveProject()
    {
        try { await CloseProject(); }
        catch (Exception ex) { ReportResourceMessage(ex.Message); }
    }

    [RelayCommand]
    private async Task OpenRecentProject(string path)
    {
        try
        {
            await OpenProject(path);
        }
        catch (Exception ex)
        {
            ReportProjectError(ex.Message);
        }
    }

    private async Task ActivateProject(ProjectWorkspace project)
    {
        var navigation = ++editorNavigationGeneration;
        await SaveActiveProjectAsync();
        if (navigation != editorNavigationGeneration) return;
        await StopPythonAsync();
        if (navigation != editorNavigationGeneration) return;
        var loadedStage = stageService.Load(project);
        ResetStage(loadedStage);
        activeProject = project;
        runtimeWorkspace++;
        RefreshRuntimeNavigationState();
        openScriptId = null;
        ResetLoadedScript();
        ClearProjectError();
        AddRecentProject(project);
        PythonCode = string.Empty;
        ResetExplorerState();
        CollapseCategories();
        ObjectManagerTabIndex = 0;
        RefreshProjectResources();
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(OpenScript));
        OnPropertyChanged(nameof(ProjectName));
        OnPropertyChanged(nameof(ProjectDisplayName));
        OnPropertyChanged(nameof(ProjectRootPath));
        OnPropertyChanged(nameof(HasOpenProject));
        OnPropertyChanged(nameof(ShowStageCanvas));
        OnPropertyChanged(nameof(IsEditorVisible));
        OnPropertyChanged(nameof(ShowOpenProjectPrompt));
        OnPropertyChanged(nameof(EditorEmptyPromptTitle));
        OnPropertyChanged(nameof(EditorEmptyPromptMessage));
        OnPropertyChanged(nameof(CanCreateScript));
        ProjectStatus = project.Name;
        IsProjectManagerOpen = false;
    }

    /// <summary>Clears the search box so a closed project does not keep a stale filter.</summary>
    private void ResetExplorerState()
    {
        ResetScriptActions();
        SchedulePythonAnalysis();
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
        RefreshRuntimeNavigationState();
        foreach (var category in ScriptCategories)
        {
            category.Scripts.Clear();
            category.CanCreate = activeProject is not null;
        }

        if (activeProject is null)
        {
            CollapseCategories();
            RefreshStageObjects();
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
                new ProjectScriptItem(script.Id, displayName, script.Path, script.Kind, script.Id == openScriptId,
                    ScriptRename?.ScriptId == script.Id ? ScriptRename : null, ReadScriptTemplateVersion(script)));
        }

        foreach (var category in ScriptCategories)
            SortScripts(category);

        UpdateCategoryExpansion(query);
        RefreshStageObjects();

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

    /// <summary>Saves the current buffer then loads the chosen indexed script into the editor.</summary>
    [RelayCommand]
    private async Task OpenScriptFromExplorer(ProjectScriptItem? item)
    {
        if (item is null || activeProject is null)
            return;

        var script = ResolveProjectScript(item.Id);
        if (script is null)
        {
            ReportResourceMessage("脚本不在当前项目索引中。");
            return;
        }

        if (string.Equals(openScriptId, script.Id, StringComparison.Ordinal))
            return;

        var navigation = ++editorNavigationGeneration;
        try
        {
            await SaveActiveProjectAsync();
            if (navigation != editorNavigationGeneration) return;
            var loaded = projectSession.LoadScript(activeProject, script);
            RememberLoadedScript(script.Id, loaded);
            PythonCode = loaded;
            openScriptId = script.Id;
            EditorState = "就绪";
            OnPropertyChanged(nameof(FileName));
            OnPropertyChanged(nameof(OpenScript));
            OnPropertyChanged(nameof(IsEditorVisible));
            OnPropertyChanged(nameof(ShowOpenProjectPrompt));
            OnPropertyChanged(nameof(EditorEmptyPromptTitle));
            OnPropertyChanged(nameof(EditorEmptyPromptMessage));
            RefreshProjectResources();
            ApplyStageSelection(openScriptId);
            SchedulePythonAnalysis();
        }
        catch (Exception ex)
        {
            ReportResourceMessage(ex.Message);
        }
    }

    /// <summary>
    /// While a run is active, inspects the selected object without navigating away from the preview.
    /// Otherwise opens its script; a retained preview stays until an explicit edit or "返回初始布局".
    /// </summary>
    [RelayCommand]
    private async Task SelectStageObject(StageObjectItem? item)
    {
        if (item is null)
            return;

        if (IsPythonActive)
        {
            foreach (var candidate in StageSprites.Concat(StageBackdrops)) candidate.IsSelected = candidate.ObjectId == item.ObjectId;
            if (item.IsSprite) SelectedSprite = item; else SelectedBackdrop = item;
            ObjectManagerTabIndex = item.IsSprite ? 0 : 1;
            return;
        }
        await OpenScriptFromExplorer(item.ToScriptItem());
        if (openScriptId != item.ScriptId) return;
        ApplyStageSelection(item.ScriptId);
        ObjectManagerTabIndex = item.Kind == ProjectScriptKind.Backdrop ? 1 : 0;
    }

    /// <summary>
    /// Clears sprite/backdrop selection without closing the script currently in the editor.
    /// </summary>
    public void ClearStageSelection() => ApplyStageSelection(null);

    [RelayCommand]
    private Task AddSprite() => AddStageObject(ProjectScriptKind.Object);

    [RelayCommand]
    private Task AddBackdrop() => AddStageObject(ProjectScriptKind.Backdrop);

    private async Task AddStageObject(ProjectScriptKind kind)
    {
        if (activeProject is null)
            return;

        var category = CategoryFor(kind);
        var name = NextAvailableScriptName(kind);
        category.NewScriptName = name;
        CreateScript(category);
        if (category.HasError)
            return;

        var created = ObjectsFor(kind)
            .FirstOrDefault(item => string.Equals(item.DisplayName, name, StringComparison.OrdinalIgnoreCase));
        if (created is not null)
            await SelectStageObject(created);
    }

    private string NextAvailableScriptName(ProjectScriptKind kind)
    {
        var prefix = kind == ProjectScriptKind.Backdrop ? "backdrop" : "sprite";
        var used = new HashSet<string>(
            activeProject!.Manifest.Scripts
                .Where(script => script.Kind == kind)
                .Select(script => Path.GetFileNameWithoutExtension(script.Path)),
            StringComparer.OrdinalIgnoreCase);

        if (!used.Contains(prefix))
            return prefix;

        for (var index = 2; index < 10_000; index++)
        {
            var candidate = $"{prefix}{index}";
            if (!used.Contains(candidate))
                return candidate;
        }

        return $"{prefix}-{Guid.NewGuid():N}"[..16];
    }

    /// <summary>
    /// Rebuilds sprite and backdrop tiles from the project index, keeping session-only
    /// transform values for objects that still exist.
    /// </summary>
    private void RefreshStageObjects()
    {
        restoringStage = true;
        try
        {
        SelectedSprite = SyncStageObjects(StageSprites, ProjectScriptKind.Object);
        SelectedBackdrop = SyncStageObjects(StageBackdrops, ProjectScriptKind.Backdrop);
        OnPropertyChanged(nameof(CurrentBackdrop));
        OnPropertyChanged(nameof(DisplayBackdrop));
        }
        finally { restoringStage = false; RefreshStageInspectorState(); }
    }

    private StageObjectItem? SyncStageObjects(
        ObservableCollection<StageObjectItem> collection,
        ProjectScriptKind kind)
    {
        if (activeProject is null)
        {
            foreach (var old in collection) { old.PropertyChanged -= OnStageItemChanged; old.Dispose(); }
            collection.Clear();
            return null;
        }

        var existing = collection
            .Where(item => !string.IsNullOrWhiteSpace(item.ObjectId))
            .ToDictionary(item => item.ObjectId, StringComparer.Ordinal);
        var scripts = activeProject.Manifest.Scripts
            .Where(script => script.Kind == kind && !string.IsNullOrWhiteSpace(script.ObjectId))
            .OrderBy(script => Path.GetFileNameWithoutExtension(script.Path), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        collection.Clear();
        StageObjectItem? selected = null;
        foreach (var script in scripts)
        {
            var displayName = Path.GetFileNameWithoutExtension(script.Path);
            var isSelected = string.Equals(script.Id, openScriptId, StringComparison.Ordinal);
            if (!existing.TryGetValue(script.ObjectId!, out var item))
            {
                item = new StageObjectItem(script.Id, script.ObjectId!, kind, displayName, script.Path);
                RestoreStageItem(item);
            }

            item.SyncFromScript(script.Id, displayName, script.Path, isSelected);
            collection.Add(item);
            if (isSelected)
                selected = item;
        }

        foreach (var removed in existing.Values.Where(old => !collection.Contains(old)))
        { removed.PropertyChanged -= OnStageItemChanged; removed.Dispose(); stageDirty = true; }
        return selected;
    }

    private void ApplyStageSelection(string? scriptId)
    {
        foreach (var sprite in StageSprites)
            sprite.IsSelected = string.Equals(sprite.ScriptId, scriptId, StringComparison.Ordinal);
        foreach (var backdrop in StageBackdrops)
            backdrop.IsSelected = string.Equals(backdrop.ScriptId, scriptId, StringComparison.Ordinal);

        SelectedSprite = StageSprites.FirstOrDefault(item => item.IsSelected);
        SelectedBackdrop = StageBackdrops.FirstOrDefault(item => item.IsSelected);
        if (!IsPythonActive && !hasRuntimePreview && SelectedBackdrop is { } current && currentBackdropId != current.ObjectId)
        { ClearRuntimeStage(); currentBackdropId = current.ObjectId; stageDirty = true; }
        OnPropertyChanged(nameof(CurrentBackdrop));
        OnPropertyChanged(nameof(DisplayBackdrop));
    }

    private ObservableCollection<StageObjectItem> ObjectsFor(ProjectScriptKind kind) =>
        kind == ProjectScriptKind.Backdrop ? StageBackdrops : StageSprites;

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
        SetRecentProjects(recentProjectsService.RecordOpened(project));
    }

    private void SetRecentProjects(IEnumerable<RecentProjectEntry> projects)
    {
        var ordered = projects.ToArray();

        RecentProjectsToday.Clear();
        RecentProjectsThisWeek.Clear();
        RecentProjectsThisMonth.Clear();
        RecentProjectsOlder.Clear();
        var now = DateTimeOffset.Now;
        foreach (var item in ordered)
        {
            var age = now - item.OpenedAt;
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
                includeLayout ? AuxiliaryWidth : savedSettings.AuxiliaryWidth,
                PythonExecutable));
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
    private async Task Save()
    {
        if (activeProject is null)
        {
            EditorState = "尚未打开项目";
            return;
        }

        try
        {
            await SaveActiveProjectAsync(checkUnchangedDisk: true);
            EditorState = "已保存";
        }
        catch (Exception ex)
        {
            EditorState = $"保存失败：{ex.Message}";
            ReportResourceMessage(EditorState);
        }
    }

}
