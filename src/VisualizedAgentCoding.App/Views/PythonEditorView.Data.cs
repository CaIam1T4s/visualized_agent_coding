using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace happy_agent_coding.Views;

public partial class PythonEditorView
{
    private bool showingVariables;

    private async void OnManageVariablesClick(object? sender, RoutedEventArgs e)
    {
        editorMenu?.Hide();
        if (showingVariables || viewModel is null || !viewModel.HasOpenProject || viewModel.IsPythonActive
            || TopLevel.GetTopLevel(this) is not Window owner
            || owner.FindControl<Border>("WindowFrame")?.Child is not Grid host) return;
        var dialog = new VariableManagerView(viewModel);
        showingVariables = true;
        Grid.SetRowSpan(dialog, 2);
        host.Children.Add(dialog);
        try { await dialog.Result; }
        finally
        {
            host.Children.Remove(dialog);
            showingVariables = false;
            EditorTextBox.TextArea.Focus();
        }
    }
}
