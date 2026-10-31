using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace happy_agent_coding.Views;

public partial class MessageEventView : UserControl
{
    private readonly TaskCompletionSource<string?> completion = new();
    private readonly Func<string, string?> validate;
    public Task<string?> Result => completion.Task;
    public MessageEventView() : this("添加消息事件", "", _ => null) { }
    public MessageEventView(string title, string name, Func<string, string?> validate, string? deletePreview = null, bool renaming = false, bool fixedEvent = false, bool keyEvent = false)
    {
        this.validate = validate;
        InitializeComponent();
        DialogTitle.Text = title;
        MessageName.Text = name;
        MessageName.IsVisible = !fixedEvent;
        MessageName.IsReadOnly = deletePreview is not null;
        AcceptButton.Content = deletePreview is null ? "确定" : "删除区域";
        Description.Text = deletePreview is null
            ? renaming ? "名称区分大小写。改名不会自动修改发送方的 broadcast 调用，请同步调整消息名称。"
                : "同一对象不能有重复的同名消息事件。名称区分大小写。"
            : "将删除整个消息区域及以下代码，可通过撤销恢复。";
        if (keyEvent)
        {
            MessageName.PlaceholderText = "a、space、up、any";
            Description.Text = deletePreview is null
                ? "输入单个小写字母、数字，或 space/enter/上/下/左/右/any 等名称。运行时按下该键，所有绑定此键的对象都会执行。"
                : "将删除整个按键事件区域及以下代码，可通过撤销恢复。";
        }
        else if (fixedEvent) Description.Text = deletePreview is null ? "运行时点击该角色，将执行此区域。每个角色只能有一个点击区域。" : "将删除整个角色点击区域及以下代码，可通过撤销恢复。";
        CodePreview.IsVisible = deletePreview is not null;
        CodePreview.Text = deletePreview;
        Loaded += (_, _) => { if (fixedEvent) AcceptButton.Focus(); else { MessageName.Focus(); MessageName.SelectAll(); } };
        Unloaded += (_, _) => completion.TrySetResult(null);
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape) { completion.TrySetResult(null); e.Handled = true; }
            else if (e.Key == Key.Enter && deletePreview is null && MessageName.IsKeyboardFocusWithin)
            { OnAccept(this, new RoutedEventArgs()); e.Handled = true; }
        }, RoutingStrategies.Tunnel);
    }
    private void OnCancel(object? sender, RoutedEventArgs e) => completion.TrySetResult(null);
    private void OnAccept(object? sender, RoutedEventArgs e)
    {
        var name = (MessageName.Text ?? "").Trim();
        var error = validate(name);
        ValidationError.Text = error;
        ValidationError.IsVisible = error is not null;
        if (error is null) completion.TrySetResult(name);
    }
}
