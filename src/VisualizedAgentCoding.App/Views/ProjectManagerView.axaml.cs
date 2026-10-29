using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using happy_agent_coding.ViewModels;
using VisualizedAgentCoding.Domain.Projects;

namespace happy_agent_coding.Views;

public partial class ProjectManagerView : UserControl
{
    private bool creatingProject;
    public ProjectManagerView()
    {
        InitializeComponent();
    }

    private async void OnCreateProjectClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var window = TopLevel.GetTopLevel(this) as Window;
        if (window is null || DataContext is not MainViewModel viewModel || creatingProject || Content is not Grid host)
            return;
        creatingProject = true;
        try
        {
            viewModel.ClearProjectError();
            var folders = await window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择项目文件夹所在的目录",
                AllowMultiple = false,
            });
            if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } path || !viewModel.IsProjectManagerOpen)
                return;
            var dialog = new ProjectNameView(path);
            Grid.SetColumnSpan(dialog, 3);
            Grid.SetRowSpan(dialog, 3);
            host.Children.Add(dialog);
            string? name;
            try { name = await dialog.Result; }
            finally { host.Children.Remove(dialog); }
            if (name is null || !viewModel.IsProjectManagerOpen || DataContext != viewModel) return;
            await viewModel.CreateProjectInDirectory(path, name);
        }
        catch (Exception ex)
        {
            viewModel.ReportProjectError(ex.Message);
        }
        finally { creatingProject = false; }
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
            await viewModel.OpenProject(path);
        }
        catch (Exception ex)
        {
            viewModel.ReportProjectError(ex.Message);
        }
    }
}
