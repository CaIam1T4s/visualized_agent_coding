using Avalonia.Controls;
using Avalonia;
using Avalonia.Threading;

namespace happy_agent_coding.Views;

public partial class ScriptReloadView : UserControl
{
    public ScriptReloadView() => InitializeComponent();
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && IsVisible)
            Dispatcher.UIThread.Post(() => { if (IsVisible) CancelButton.Focus(); });
    }
}
