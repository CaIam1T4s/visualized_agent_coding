using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using AtomUI.Icons.AntDesign;
using happy_agent_coding.ViewModels;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace happy_agent_coding.Views;

public partial class ObjectManagerView : UserControl
{
    private bool showingStageReload;
    private bool showingCostumes;
    private bool savingStageObjectName;
    private MainViewModel? attachedModel;
    public ObjectManagerView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachModel(DataContext as MainViewModel);
        Unloaded += (_, _) => AttachModel(null);
    }

    private void AttachModel(MainViewModel? model)
    {
        if (ReferenceEquals(attachedModel, model)) return;
        if (attachedModel is not null) attachedModel.StageObjectNameFocusRequested -= OnStageObjectNameFocusRequested;
        attachedModel = model;
        if (attachedModel is not null) attachedModel.StageObjectNameFocusRequested += OnStageObjectNameFocusRequested;
    }

    private void OnStageObjectNameFocusRequested(StageObjectItem item) =>
        Dispatcher.UIThread.Post(() =>
        {
            var box = this.GetVisualDescendants().OfType<TextBox>()
                .FirstOrDefault(candidate => Equals(candidate.Tag, "StageObjectName"));
            if (box?.DataContext is not StageObjectItem current || !ReferenceEquals(current, item)) return;
            box.Focus();
            box.SelectAll();
        });

    private async void OnStageObjectNameLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (savingStageObjectName || sender is not TextBox { DataContext: StageObjectItem item } box
            || DataContext is not MainViewModel model) return;
        await CommitStageObjectNameAsync(model, item, box);
    }

    private async void OnStageObjectNameKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: StageObjectItem item } box
            || DataContext is not MainViewModel model) return;
        if (e.Key == Avalonia.Input.Key.Enter)
        {
            e.Handled = true;
            await CommitStageObjectNameAsync(model, item, box);
            box.Focus();
            box.SelectAll();
        }
        else if (e.Key == Avalonia.Input.Key.Escape)
        {
            e.Handled = true;
            box.Text = item.DisplayName;
            box.SelectAll();
        }
    }

    private async Task CommitStageObjectNameAsync(MainViewModel model, StageObjectItem item, TextBox box)
    {
        if (savingStageObjectName) return;
        savingStageObjectName = true;
        try
        {
            var name = (box.Text ?? string.Empty).Trim();
            if (string.Equals(name, item.DisplayName, StringComparison.Ordinal))
            {
                box.Text = item.DisplayName;
                return;
            }
            if (!await model.RenameStageObjectAsync(item, name))
            {
                box.Text = item.DisplayName;
                box.SelectAll();
            }
        }
        finally { savingStageObjectName = false; }
    }

    private void OnHideIconAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is ContentControl host) host.Content = CreateIcon(AntDesignIconKind.EyeInvisibleOutlined);
    }

    private static Control CreateIcon(AntDesignIconKind kind)
    {
        var icon = (Control)new AntDesignIconProvider(kind).ProvideValue(null!);
        icon.Width = 14;
        icon.Height = 14;
        return icon;
    }

    private async void OnManageCostumesClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (showingCostumes || sender is not Button { Tag: StageObjectItem item } || DataContext is not MainViewModel model
            || !item.IsSprite || !item.CanEditProperties
            || TopLevel.GetTopLevel(this) is not Window owner
            || owner.FindControl<Border>("WindowFrame")?.Child is not Grid host) return;
        var dialog = new CostumeManagerView(model, item);
        showingCostumes = true;
        Grid.SetRowSpan(dialog, 2);
        host.Children.Add(dialog);
        try { await dialog.WaitForClose(); }
        finally { host.Children.Remove(dialog); showingCostumes = false; (sender as Control)?.Focus(); }
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
        if (showingStageReload || DataContext is not MainViewModel model || model.IsPythonActive || !model.HasOpenProject || TopLevel.GetTopLevel(this) is not Window owner
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
            if (!viewModel.CanEditInitialStage) { e.Handled = true; return; }
            ScriptActionMenu.Show(source, viewModel, item.ToScriptItem());
            e.Handled = true;
        }
    }

    private void OnAddSpriteClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel model && model.AddSpriteCommand.CanExecute(null))
            model.AddSpriteCommand.Execute(null);
    }

    private void OnAddBackdropClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel model && model.AddBackdropCommand.CanExecute(null))
            model.AddBackdropCommand.Execute(null);
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
