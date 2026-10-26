using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace happy_agent_coding.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        PropertyChanged += OnWindowPropertyChanged;
        UpdateWindowChromeState();
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowStateProperty)
            UpdateWindowChromeState();
    }

    private void UpdateWindowChromeState()
    {
        var isMaximized = WindowState == WindowState.Maximized;
        WindowFrame.Classes.Set("maximized", isMaximized);
        ResizeGripGrid.IsVisible = !isMaximized;
        TitleBarView.UpdateWindowState(WindowState);
    }

    private void OnResizeGripPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { Tag: string edgeName } ||
            !Enum.TryParse<WindowEdge>(edgeName, out var edge))
            return;

        BeginResizeDrag(edge, e);
        e.Handled = true;
    }
}