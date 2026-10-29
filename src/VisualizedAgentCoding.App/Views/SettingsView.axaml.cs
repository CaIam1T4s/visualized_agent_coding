using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using happy_agent_coding.ViewModels;

namespace happy_agent_coding.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void OnGeneralClick(object? sender, RoutedEventArgs e)
    { if (DataContext is MainViewModel vm) vm.SettingsCategoryIndex = 0; }
    private void OnAppearanceClick(object? sender, RoutedEventArgs e)
    { if (DataContext is MainViewModel vm) vm.SettingsCategoryIndex = 1; }
    private async void OnBrowsePythonClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || !vm.CanChangePythonEnvironment
            || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择 Python 可执行文件", AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Python 可执行文件") { Patterns = ["*.exe", "python", "python3", "python3.*"] }, FilePickerFileTypes.All],
        });
        if (files.Count > 0 && vm.CanChangePythonEnvironment && files[0].TryGetLocalPath() is { } path)
            vm.PythonExecutableDraft = path;
    }
}
