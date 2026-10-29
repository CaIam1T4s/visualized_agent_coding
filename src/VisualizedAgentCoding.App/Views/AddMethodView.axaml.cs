using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace happy_agent_coding.Views;

public partial class AddMethodView : UserControl
{
    private readonly TaskCompletionSource<string?> completion = new();
    private readonly Func<string, string?> validate;
    public AddMethodView() : this(_ => null) { }
    public AddMethodView(Func<string, string?> validate)
    {
        this.validate = validate;
        InitializeComponent();
        Loaded += (_, _) => { MethodName.Focus(); MethodName.SelectAll(); };
        Unloaded += (_, _) => completion.TrySetResult(null);
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape) { completion.TrySetResult(null); e.Handled = true; }
            else if (e.Key == Key.Enter && MethodName.IsKeyboardFocusWithin)
            { OnAccept(this, new RoutedEventArgs()); e.Handled = true; }
        }, RoutingStrategies.Tunnel);
    }
    public Task<string?> Result => completion.Task;
    private void OnCancel(object? sender, RoutedEventArgs e) => completion.TrySetResult(null);
    private void OnAccept(object? sender, RoutedEventArgs e)
    {
        var name = MethodName.Text ?? "";
        var error = validate(name);
        ValidationError.Text = error;
        ValidationError.IsVisible = error is not null;
        if (error is null) completion.TrySetResult(name);
    }
}
