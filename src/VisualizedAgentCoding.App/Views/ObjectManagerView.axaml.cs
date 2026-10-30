using Avalonia.Controls;
using happy_agent_coding.ViewModels;
using Avalonia.Platform.Storage;
using System;

namespace happy_agent_coding.Views;

public partial class ObjectManagerView : UserControl
{
    private bool showingStageReload;
    public ObjectManagerView()
    {
        InitializeComponent();
    }

    private async void OnImportImageClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button { Tag: StageObjectItem item } || DataContext is not MainViewModel model
            || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        try
        {
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择角色或背景图片", AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("图片") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp"] }]
            });
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) await model.ImportStageImageAsync(item, path);
        }
        catch (Exception ex) { model.ReportStageImageError(ex.Message); }
    }

    private async void OnReloadStageClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (showingStageReload || DataContext is not MainViewModel model || TopLevel.GetTopLevel(this) is not Window owner
            || owner.FindControl<Border>("WindowFrame")?.Child is not Grid host) return;
        var projectRoot = model.ProjectRootPath;
        var dialog = new StageReloadView();
        showingStageReload = true;
        Grid.SetRowSpan(dialog, 2); host.Children.Add(dialog);
        try
        {
            if (await dialog.Result && DataContext == model && model.ProjectRootPath == projectRoot) model.ReloadStage();
        }
        finally { host.Children.Remove(dialog); showingStageReload = false; (sender as Control)?.Focus(); }
    }

    private void OnStageObjectContextRequested(object? sender, Avalonia.Input.ContextRequestedEventArgs e)
    {
        if (sender is Control { DataContext: StageObjectItem item } source && DataContext is MainViewModel viewModel)
        {
            ScriptActionMenu.Show(source, viewModel, item.ToScriptItem());
            e.Handled = true;
        }
    }

    private void OnStageObjectClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button { Tag: StageObjectItem item })
            return;
        if (DataContext is not MainViewModel viewModel)
            return;

        viewModel.SelectStageObjectCommand.Execute(item);
    }
}
