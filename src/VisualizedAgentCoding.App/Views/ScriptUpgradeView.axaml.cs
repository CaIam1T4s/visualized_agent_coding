using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace happy_agent_coding.Views;

public partial class ScriptUpgradeView : UserControl
{
    private readonly TaskCompletionSource<bool> completion = new();
    public ScriptUpgradeView() { InitializeComponent();
        Unloaded += (_, _) => completion.TrySetResult(false);
        AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Escape) { completion.TrySetResult(false); e.Handled = true; } }, RoutingStrategies.Tunnel);
    }
    public ScriptUpgradeView(string before, string after) : this()
    {
        OriginalCode.Text = before;
        UpgradedCode.Text = after;
        Loaded += (_, _) => OriginalCode.TextArea.Focus();
    }
    public Task<bool> Result => completion.Task;
    private void OnCancel(object? sender, RoutedEventArgs e) => completion.TrySetResult(false);
    private void OnConfirm(object? sender, RoutedEventArgs e) => completion.TrySetResult(true);
}
