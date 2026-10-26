using System;
using System.IO;
using System.Linq;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Styling;
using AvaloniaFluentUI.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using happy_agent_coding.Services;

namespace happy_agent_coding.ViewModels;

public sealed record RecentProjectEntry(string Name, string Path, DateTimeOffset OpenedAt);
public sealed record ProjectScriptEntry(string DisplayName, string RelativePath);

public partial class MainViewModel : ViewModelBase
{
    private readonly ISettingsStore settingsStore;
    private ProjectWorkspace? activeProject;
    public ObservableCollection<RecentProjectEntry> RecentProjectsToday { get; } = [];
    public ObservableCollection<RecentProjectEntry> RecentProjectsThisWeek { get; } = [];
    public ObservableCollection<ProjectScriptEntry> UserScripts { get; } = [];
    public ObservableCollection<ProjectScriptEntry> ObjectScripts { get; } = [];
    public ObservableCollection<ProjectScriptEntry> BackdropScripts { get; } = [];
    public string ProjectResourceStatus { get; private set; } = "打开项目后显示脚本";
    public ObservableCollection<RecentProjectEntry> RecentProjectsThisMonth { get; } = [];
    public ObservableCollection<RecentProjectEntry> RecentProjectsOlder { get; } = [];
    public bool HasRecentProjects => RecentProjectsToday.Count + RecentProjectsThisWeek.Count +
        RecentProjectsThisMonth.Count + RecentProjectsOlder.Count > 0;

    public MainViewModel()
        : this(new JsonSettingsStore())
    {
    }

    public MainViewModel(ISettingsStore settingsStore)
    {
        ArgumentNullException.ThrowIfNull(settingsStore);
        this.settingsStore = settingsStore;
        var settings = this.settingsStore.Load();

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

    [ObservableProperty]
    private string resourceSearchQuery = string.Empty;

    public string FileName => activeProject is null ? "main.py" : Path.GetFileName(activeProject.MainScriptPath);

    public string? ProjectName => activeProject?.Name;

    public string ProjectDisplayName => activeProject?.Name ?? "未打开项目";

    public string? ProjectRootPath => activeProject?.RootPath;

    public bool HasOpenProject => activeProject is not null;

    public void CreateProject(string rootPath, string? name = null) => ActivateProject(ProjectWorkspace.Create(rootPath, name));

    public void OpenProject(string rootPath) => ActivateProject(ProjectWorkspace.Open(rootPath));

    public void CloseProject()
    {
        SaveActiveProject();
        activeProject = null;
        PythonCode = string.Empty;
        RefreshProjectResources();
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(ProjectName));
        OnPropertyChanged(nameof(ProjectDisplayName));
        OnPropertyChanged(nameof(ProjectRootPath));
        OnPropertyChanged(nameof(HasOpenProject));
        ProjectStatus = "未打开项目";
    }

    [RelayCommand]
    private void OpenProjectManager() => IsProjectManagerOpen = true;

    [RelayCommand]
    private void CloseProjectManager() => IsProjectManagerOpen = false;

    [RelayCommand]
    private void CloseProjectCommand() => CloseProject();

    [RelayCommand]
    private void OpenRecentProject(string path)
    {
        try
        {
            OpenProject(path);
        }
        catch (IOException ex)
        {
            ProjectStatus = ex.Message;
        }
        catch (UnauthorizedAccessException ex)
        {
            ProjectStatus = ex.Message;
        }
        catch (System.Text.Json.JsonException ex)
        {
            ProjectStatus = ex.Message;
        }
    }

    private void ActivateProject(ProjectWorkspace project)
    {
        SaveActiveProject();
        activeProject = project;
        AddRecentProject(project);
        PythonCode = project.LoadMainScript();
        RefreshProjectResources();
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(ProjectName));
        OnPropertyChanged(nameof(ProjectDisplayName));
        OnPropertyChanged(nameof(ProjectRootPath));
        OnPropertyChanged(nameof(HasOpenProject));
        ProjectStatus = project.Name;
        IsProjectManagerOpen = false;
    }

    private void SaveActiveProject()
    {
        activeProject?.SaveMainScript(PythonCode);
    }

    partial void OnResourceSearchQueryChanged(string value)
    {
        RefreshProjectResources();
    }

    private void RefreshProjectResources()
    {
        UserScripts.Clear();
        ObjectScripts.Clear();
        BackdropScripts.Clear();
        if (activeProject is null)
        {
            ProjectResourceStatus = "打开项目后显示脚本";
            OnPropertyChanged(nameof(ProjectResourceStatus));
            return;
        }

        try
        {
            PopulateScripts("scripts/user", UserScripts);
            PopulateScripts("scripts/objects", ObjectScripts);
            PopulateScripts("scripts/backdrops", BackdropScripts);
            var count = UserScripts.Count + ObjectScripts.Count + BackdropScripts.Count;
            ProjectResourceStatus = count == 0
                ? string.IsNullOrWhiteSpace(ResourceSearchQuery) ? "当前项目暂无脚本" : "没有匹配的脚本"
                : $"{count} 项脚本";
        }
        catch (IOException)
        {
            ProjectResourceStatus = "无法读取项目脚本";
        }
        catch (UnauthorizedAccessException)
        {
            ProjectResourceStatus = "无法读取项目脚本";
        }

        OnPropertyChanged(nameof(ProjectResourceStatus));
    }

    private void PopulateScripts(string relativeDirectory, ObservableCollection<ProjectScriptEntry> destination)
    {
        var directory = Path.Combine(activeProject!.RootPath, relativeDirectory.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(directory))
            return;

        var query = ResourceSearchQuery.Trim();
        var scripts = Directory.EnumerateFiles(directory, "*.py", SearchOption.AllDirectories)
            .Where(path => string.IsNullOrWhiteSpace(query) ||
                Path.GetFileNameWithoutExtension(path).Contains(query, StringComparison.OrdinalIgnoreCase) ||
                Path.GetRelativePath(activeProject.RootPath, path).Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

        foreach (var path in scripts)
        {
            destination.Add(new ProjectScriptEntry(
                Path.GetFileNameWithoutExtension(path),
                Path.GetRelativePath(activeProject.RootPath, path).Replace(Path.DirectorySeparatorChar, '/')));
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
