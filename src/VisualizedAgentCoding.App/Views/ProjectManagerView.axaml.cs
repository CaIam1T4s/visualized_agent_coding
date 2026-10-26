using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using happy_agent_coding.ViewModels;
using VisualizedAgentCoding.Domain.Projects;

namespace happy_agent_coding.Views;

public partial class ProjectManagerView : UserControl
{
    public ProjectManagerView()
    {
        InitializeComponent();
    }

    private async void OnCreateProjectClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window is null || DataContext is not MainViewModel viewModel)
            return;

        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择新项目位置",
            AllowMultiple = false,
        });
        if (folders.Count == 0)
            return;
        var path = folders[0].TryGetLocalPath();
        if (path is null)
            return;

        try
        {
            viewModel.CreateProject(path);
        }
        catch (Exception ex) when (ex is ProjectFormatException or IOException or UnauthorizedAccessException)
        {
            viewModel.ReportProjectError(ex.Message);
        }
    }

    private void OnRecentProjectClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && sender is Button { Tag: string path })
            viewModel.OpenRecentProjectCommand.Execute(path);
    }

    private async void OnOpenProjectClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window is null || DataContext is not MainViewModel viewModel)
            return;

        var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择项目文件夹",
            AllowMultiple = false,
        });
        if (folders.Count == 0)
            return;
        var path = folders[0].TryGetLocalPath();
        if (path is null)
            return;

        try
        {
            viewModel.OpenProject(path);
        }
        catch (Exception ex) when (ex is ProjectFormatException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            viewModel.ReportProjectError(ex.Message);
        }
    }
}
