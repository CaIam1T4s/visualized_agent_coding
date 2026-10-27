using Avalonia.Controls;
using happy_agent_coding.ViewModels;

namespace happy_agent_coding.Views;

public partial class ObjectManagerView : UserControl
{
    public ObjectManagerView()
    {
        InitializeComponent();
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
