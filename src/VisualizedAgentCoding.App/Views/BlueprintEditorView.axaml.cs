using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace happy_agent_coding.Views;

public partial class BlueprintEditorView : UserControl
{
    public BlueprintEditorView() => InitializeComponent();

    private bool showingVariables;

    private void OnNodeSelected(object? sender, happy_agent_coding.Controls.BlueprintWorkspace.BlueprintNodeSelectedEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel model)
            model.BlueprintEditor.SelectNode(e.Node);
    }

    private void OnBlueprintReconnect(object? sender, happy_agent_coding.Controls.BlueprintWorkspace.BlueprintReconnectEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel model)
            model.ReconnectBlueprintNodes(e.Source, e.Target);
    }

    private void OnBlueprintAnnotation(object? sender, happy_agent_coding.Controls.BlueprintWorkspace.BlueprintReconnectEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel model)
            model.BindBlueprintComment(e.Source, e.Target);
    }

    private void OnBlueprintValueEdited(object? sender, happy_agent_coding.Controls.BlueprintWorkspace.BlueprintValueEditedEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel model)
            model.UpdateBlueprintValue(e.Node, e.Port, e.Value);
    }

    private void OnBlueprintNodeMoved(VisualizedAgentCoding.Domain.Blueprint.BlueprintNode node)
    {
        if (DataContext is ViewModels.MainViewModel model)
            _ = model.SaveBlueprintLayoutAsync();
    }

    private void OnInsertBlueprintStatement(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel model)
            model.InsertBlueprintStatement();
    }

    private void OnPaletteInsert(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string text } && DataContext is ViewModels.MainViewModel model)
            model.InsertBlueprintStatement(text);
    }

    private void OnContextInsert(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string text } && DataContext is ViewModels.MainViewModel model)
            model.InsertBlueprintStatement(text);
    }

    private void OnWorkspaceInsert(object? sender, string text)
    {
        if (DataContext is ViewModels.MainViewModel model)
            model.InsertBlueprintStatement(text);
    }

    private void OnWorkspaceDelete(object? sender, EventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel model)
            model.DeleteSelectedBlueprintNode();
    }

    private void OnWorkspaceArrange(object? sender, EventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel model)
            model.ArrangeBlueprintNodes();
    }

    private void OnArrangeBlueprintNodes(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel model)
            model.ArrangeBlueprintNodes();
    }

    private void OnDeleteBlueprintNode(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel model)
            model.DeleteSelectedBlueprintNode();
    }

    private async void OnManageProjectVariables(object? sender, RoutedEventArgs e)
    {
        if (showingVariables || DataContext is not ViewModels.MainViewModel model || !model.HasOpenProject
            || TopLevel.GetTopLevel(this) is not Window owner
            || owner.FindControl<Border>("WindowFrame")?.Child is not Grid host) return;
        var dialog = new VariableManagerView(model);
        showingVariables = true;
        Grid.SetRowSpan(dialog, 2);
        host.Children.Add(dialog);
        try { await dialog.Result; }
        finally
        {
            host.Children.Remove(dialog);
            showingVariables = false;
        }
    }
}
