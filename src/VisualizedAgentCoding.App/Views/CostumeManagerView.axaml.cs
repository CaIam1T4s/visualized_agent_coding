using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using happy_agent_coding.ViewModels;

namespace happy_agent_coding.Views;

public partial class CostumeManagerView : UserControl
{
    private readonly MainViewModel? model;
    private readonly StageObjectItem? item;
    private readonly TaskCompletionSource<bool> completion = new();
    private bool closed;

    public CostumeManagerView() : this(null, null) { }

    public CostumeManagerView(MainViewModel? model, StageObjectItem? item)
    {
        this.model = model;
        this.item = item;
        InitializeComponent();
        if (item is not null)
        {
            TitleText.Text = "管理造型 — " + item.DisplayName;
            CostumeList.ItemsSource = item.Costumes;
            CostumeList.SelectedItem = item.SelectedCostume ?? item.Costumes.FirstOrDefault();
            RefreshInitial();
        }
        Loaded += (_, _) => CostumeNameBox.Focus();
        Unloaded += (_, _) => Close();
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        }, RoutingStrategies.Tunnel);
    }

    public Task WaitForClose() => completion.Task;

    private void Close()
    {
        if (closed) return;
        closed = true;
        completion.TrySetResult(true);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (CostumeList.SelectedItem is StageCostumeItem costume)
            CostumeNameBox.Text = costume.Name;
        else
            CostumeNameBox.Text = string.Empty;
        ShowError(null);
    }

    private async void OnAdd(object? sender, RoutedEventArgs e)
    {
        if (model is null || item is null || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        try
        {
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择角色造型图片", AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("图片") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp"] }]
            });
            if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path) return;
            var before = item.Costumes.Count;
            await model.ImportStageImageAsync(item, path);
            if (item.Costumes.Count == before)
            { ShowError(model.ProjectResourceMessage); return; }
            // Keep the new costume selected for editing, but only the first costume became initial.
            CostumeList.SelectedItem = item.Costumes.LastOrDefault();
            RefreshInitial();
        }
        catch (Exception ex) { ShowError(ex.Message); }
    }

    private void OnRemove(object? sender, RoutedEventArgs e)
    {
        if (model is null || item is null || CostumeList.SelectedItem is not StageCostumeItem costume)
        { ShowError("请先选择要删除的造型。"); return; }
        model.RemoveStageCostume(item, costume);
        CostumeList.SelectedItem = item.SelectedCostume ?? item.Costumes.FirstOrDefault();
        RefreshInitial();
    }

    private void OnRename(object? sender, RoutedEventArgs e)
    {
        if (model is null || item is null || CostumeList.SelectedItem is not StageCostumeItem costume) return;
        if (!model.RenameStageCostume(item, costume, CostumeNameBox.Text ?? string.Empty))
        { ShowError("造型名称未修改，请检查名称是否为空或重复。"); return; }
        RefreshInitial();
    }

    private void OnSetInitial(object? sender, RoutedEventArgs e)
    {
        if (model is null || item is null || CostumeList.SelectedItem is not StageCostumeItem costume)
        { ShowError("请先选择要设为初始的造型。"); return; }
        model.SelectInitialStageCostume(item, costume);
        RefreshInitial();
    }

    private void RefreshInitial() =>
        InitialCostumeText.Text = "当前初始造型：" + (item?.SelectedCostume?.Name ?? "无");

    private void ShowError(string? message)
    {
        ErrorText.Text = message ?? string.Empty;
        ErrorText.IsVisible = !string.IsNullOrWhiteSpace(message);
    }
}
