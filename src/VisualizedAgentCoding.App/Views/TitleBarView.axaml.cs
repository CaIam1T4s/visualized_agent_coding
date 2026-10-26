using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Automation;
using AtomUI.Icons.AntDesign;

namespace happy_agent_coding.Views;

public partial class TitleBarView : UserControl
{
    private const uint WmSysCommand = 0x0112;
    private const int ScMove = 0xF010;
    private static readonly IntPtr MoveCaptionCommand = new(ScMove | 2);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    public TitleBarView()
    {
        InitializeComponent();
        TitleBarAppIcon.Content = CreateIcon(AntDesignIconKind.CodeOutlined);
        SaveIcon.Content = CreateIcon(AntDesignIconKind.SaveOutlined);
    }

    private static Control CreateIcon(AntDesignIconKind kind)
    {
        var icon = (Control)new AntDesignIconProvider(kind).ProvideValue(null!);
        icon.Width = 16;
        icon.Height = 16;
        return icon;
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || GetWindow() is not { } window)
            return;

        if (OperatingSystem.IsWindows() && window.TryGetPlatformHandle() is { } platformHandle)
        {
            ReleaseCapture();
            SendMessage(platformHandle.Handle, WmSysCommand, MoveCaptionCommand, IntPtr.Zero);
            e.Handled = true;
            return;
        }

        window.BeginMoveDrag(e);
    }

    private void OnTitleBarDoubleTapped(object? sender, TappedEventArgs e)
    {
        ToggleMaximizeRestore();
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e)
    {
        if (GetWindow() is { } window)
            window.WindowState = WindowState.Minimized;
    }

    private void OnMaximizeRestoreClick(object? sender, RoutedEventArgs e)
    {
        ToggleMaximizeRestore();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
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
        UpdateWindowState(window.WindowState);
    }

    public void UpdateWindowState(WindowState windowState)
    {
        var isMaximized = windowState == WindowState.Maximized;
        MaximizeIcon.Text = isMaximized ? "\uE923" : "\uE922";
        ToolTip.SetTip(MaximizeButton, isMaximized ? "还原" : "最大化");
        AutomationProperties.SetName(MaximizeButton, isMaximized ? "还原" : "最大化");
    }

    private Window? GetWindow() => TopLevel.GetTopLevel(this) as Window;
}