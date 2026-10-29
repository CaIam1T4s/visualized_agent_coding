using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using happy_agent_coding.ViewModels;

namespace happy_agent_coding.Views;

public partial class TerminalView : UserControl
{
    private MainViewModel? model;
    public TerminalView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (model is not null) model.PropertyChanged -= OnChanged;
            model = DataContext as MainViewModel;
            if (model is not null) { model.PropertyChanged += OnChanged; UpdateOutput(); }
        };
    }
    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(MainViewModel.TerminalOutput)) UpdateOutput(); }
    private void UpdateOutput()
    {
        var follow = OutputEditor.VerticalOffset + OutputEditor.ViewportHeight >= OutputEditor.ExtentHeight - 24;
        var value = model?.TerminalOutput ?? "";
        var previous = OutputEditor.Text ?? "";
        if (value.StartsWith(previous, StringComparison.Ordinal)) OutputEditor.Document.Insert(previous.Length, value[previous.Length..]);
        else OutputEditor.Text = value;
        if (follow) OutputEditor.ScrollToEnd();
    }
    private async void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || model is null || !model.CanSendTerminalInput) return;
        e.Handled = true;
        await model.SendTerminalInputCommand.ExecuteAsync(null);
    }
    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(OutputEditor.SelectionLength > 0 ? OutputEditor.SelectedText : model?.TerminalOutput ?? "");
    }
    private async void OnLocationClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RuntimeLocationItem item } && model is not null)
            await model.OpenRuntimeLocationCommand.ExecuteAsync(item);
    }
    private async void OnCopyErrorClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard && model is not null)
            await clipboard.SetTextAsync(model.RuntimeErrorText);
    }
    private async void OnCopyDiagnosticClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard && model is not null)
            await clipboard.SetTextAsync(model.RuntimeDiagnosticDetails);
    }
}
