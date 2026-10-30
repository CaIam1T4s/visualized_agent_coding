using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace happy_agent_coding.Views;

public partial class StageReloadView : UserControl
{
    private readonly TaskCompletionSource<bool> completion = new();
    public Task<bool> Result => completion.Task;
    public StageReloadView()
    {
        InitializeComponent();
        Loaded += (_, _) => CancelButton.Focus();
        Unloaded += (_, _) => completion.TrySetResult(false);
        AddHandler(KeyDownEvent, (_, e) =>
        { if (e.Key == Key.Escape) { completion.TrySetResult(false); e.Handled = true; } }, RoutingStrategies.Tunnel);
    }
    private void OnCancel(object? sender, RoutedEventArgs e) => completion.TrySetResult(false);
    private void OnAccept(object? sender, RoutedEventArgs e) => completion.TrySetResult(true);
}
