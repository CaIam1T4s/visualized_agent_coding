using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace happy_agent_coding.Views;

public partial class ScriptDeleteView : UserControl
{
    public ScriptDeleteView() => InitializeComponent();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && IsVisible)
            Dispatcher.UIThread.Post(() => { if (IsVisible) CancelButton?.Focus(); });
    }
}
