using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Styling;
using AvaloniaFluentUI.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using happy_agent_coding.Services;

namespace happy_agent_coding.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly ISettingsStore settingsStore;

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
        mainContentWidth = LayoutSettingValues.ClampMainContentWidth(settings.MainContentWidth);
        topPanelRatio = LayoutSettingValues.ClampTopPanelRatio(settings.TopPanelRatio);
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
    private double mainContentWidth = LayoutSettingValues.DefaultMainContentWidth;

    [ObservableProperty]
    private double topPanelRatio = LayoutSettingValues.DefaultTopPanelRatio;

    [ObservableProperty]
    private int editorModeIndex = 1;

    [ObservableProperty]
    private bool isSettingsOpen;

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

    public string FileName => "main.py";

    partial void OnSelectedThemeIndexChanged(int value)
    {
        ApplyTheme(value);
        SaveSettings();
    }

    partial void OnSidebarDisplayModeIndexChanged(int value)
    {
        SaveSettings();
    }

    public void UpdateLayout(double mainContentWidth, double topPanelRatio)
    {
        MainContentWidth = LayoutSettingValues.ClampMainContentWidth(mainContentWidth);
        TopPanelRatio = LayoutSettingValues.ClampTopPanelRatio(topPanelRatio);
    }

    public void SaveLayout()
    {
        SaveSettings();
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

    private void SaveSettings()
    {
        try
        {
            settingsStore.Save(new AppSettings(
                SidebarDisplayModeIndex,
                SelectedThemeIndex,
                MainContentWidth,
                TopPanelRatio));
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
        EditorState = "已保存";
    }

    [RelayCommand]
    private void Run()
    {
        EditorState = "等待运行时接入";
    }
}
