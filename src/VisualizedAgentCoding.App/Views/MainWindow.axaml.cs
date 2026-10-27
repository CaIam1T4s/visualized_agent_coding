using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace happy_agent_coding.Views;

public partial class MainWindow : Window
{
    private bool closingForCleanup;
    private bool cleanupCompleted;
    public MainWindow()
    {
        InitializeComponent();
        PropertyChanged += OnWindowPropertyChanged;
        Closing += OnClosing;
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

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (cleanupCompleted) return;
        e.Cancel = true;
        if (closingForCleanup) return;
        closingForCleanup = true;
        if (DataContext is happy_agent_coding.ViewModels.MainViewModel currentViewModel
            && !await currentViewModel.PrepareForExitAsync())
        {
            closingForCleanup = false;
            return;
        }
        WorkspaceViewHost.SaveCurrentLayout();
        if (DataContext is happy_agent_coding.ViewModels.MainViewModel viewModel)
            await viewModel.DisposeAsync();
        cleanupCompleted = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(Close);
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
