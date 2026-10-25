using Avalonia.Controls;
using Avalonia.Input;

namespace happy_agent_coding.Views;

public partial class TitleBarView : UserControl
{
    public TitleBarView()
    {
        InitializeComponent();
        TitleBarAppIcon.Content = CreateIcon(AtomUI.Icons.AntDesign.AntDesignIconKind.CodeOutlined);
        SaveIcon.Content = CreateIcon(AtomUI.Icons.AntDesign.AntDesignIconKind.SaveOutlined);
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && GetWindow() is { } window)
            window.BeginMoveDrag(e);
    }

    private void OnTitleBarDoubleTapped(object? sender, TappedEventArgs e)
    {
        ToggleMaximizeRestore();
    }

    private static Control CreateIcon(AtomUI.Icons.AntDesign.AntDesignIconKind kind)
    {
        var icon = (Control)new AtomUI.Icons.AntDesign.AntDesignIconProvider(kind).ProvideValue(null!);
        icon.Width = 16;
        icon.Height = 16;
        return icon;
    }

    private void OnMinimizeClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (GetWindow() is { } window)
            window.WindowState = WindowState.Minimized;
    }

    private void OnMaximizeRestoreClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ToggleMaximizeRestore();
    }

    private void OnCloseClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        GetWindow()?.Close();
    }

    private void ToggleMaximizeRestore()
    {
        if (GetWindow() is not { } window)
            return;

        window.WindowState = window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        MaximizeIcon.Text = window.WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        ToolTip.SetTip(MaximizeButton, window.WindowState == WindowState.Maximized ? "还原" : "最大化");
    }

    private Window? GetWindow() => TopLevel.GetTopLevel(this) as Window;
}
