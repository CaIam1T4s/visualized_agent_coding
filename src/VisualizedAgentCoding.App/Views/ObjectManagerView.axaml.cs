using Avalonia.Controls;
using happy_agent_coding.ViewModels;

namespace happy_agent_coding.Views;

public partial class ObjectManagerView : UserControl
{
    public ObjectManagerView()
    {
        InitializeComponent();
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
