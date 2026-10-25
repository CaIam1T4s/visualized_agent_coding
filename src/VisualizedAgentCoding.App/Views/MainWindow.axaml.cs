using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AtomUI.Icons.AntDesign;
using happy_agent_coding.Services;
using happy_agent_coding.ViewModels;

namespace happy_agent_coding.Views;

public partial class MainWindow : Window
{
    private const double MainContentMinWidth = LayoutSettingValues.MinMainContentWidth;
    private const double MainContentMaxWidth = LayoutSettingValues.MaxMainContentWidth;
    private const double RightPanelMinWidth = 260;
    private readonly DispatcherTimer layoutSaveTimer;

    public MainWindow()
    {
        InitializeComponent();
        RunIcon.Content = CreateIcon(AntDesignIconKind.PlayCircleOutlined);

        layoutSaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300),
        };
        layoutSaveTimer.Tick += OnLayoutSaveTimerTick;
        HorizontalLayoutSplitter.AddHandler(
            InputElement.PointerReleasedEvent,
            OnLayoutSplitterPointerReleased,
            RoutingStrategies.Bubble,
            handledEventsToo: true);
        VerticalLayoutSplitter.AddHandler(
            InputElement.PointerReleasedEvent,
            OnLayoutSplitterPointerReleased,
            RoutingStrategies.Bubble,
            handledEventsToo: true);
        DataContextChanged += OnDataContextChanged;
        Opened += OnOpened;
        SizeChanged += OnSizeChanged;
        Closed += OnClosed;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
            ApplySavedLayout(viewModel);
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
            ApplySavedLayout(viewModel);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        PreserveCurrentMiddleWidth();
    }

    private void OnLayoutSplitterPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        layoutSaveTimer.Stop();
        layoutSaveTimer.Start();
    }

    private void OnLayoutSaveTimerTick(object? sender, EventArgs e)
    {
        layoutSaveTimer.Stop();
        CaptureAndSaveLayout();
    }

    private void ApplySavedLayout(MainViewModel viewModel)
    {
        SidebarView.SetResponsiveCompact(false);

        ApplyMiddleColumnWidth(viewModel.MainContentWidth);

        var topPanelRatio = LayoutSettingValues.ClampTopPanelRatio(viewModel.TopPanelRatio);
        MainContentGrid.RowDefinitions[0].Height =
            new GridLength(topPanelRatio, GridUnitType.Star);
        MainContentGrid.RowDefinitions[2].Height =
            new GridLength(1 - topPanelRatio, GridUnitType.Star);
    }

    private void PreserveCurrentMiddleWidth()
    {
        var middleWidth = WorkspaceGrid.ColumnDefinitions[1].ActualWidth;
        if (!double.IsFinite(middleWidth) || middleWidth <= 0)
        {
            if (DataContext is not MainViewModel viewModel)
                return;

            middleWidth = viewModel.MainContentWidth;
        }

        ApplyMiddleColumnWidth(middleWidth);
    }

    private void ApplyMiddleColumnWidth(double mainContentWidth)
    {
        var totalWidth = WorkspaceGrid.Bounds.Width;
        if (!double.IsFinite(totalWidth) || totalWidth <= 0)
            return;

        var fixedWidth = WorkspaceGrid.ColumnDefinitions[0].ActualWidth +
                         WorkspaceGrid.ColumnDefinitions[2].ActualWidth;
        var availableWidth = totalWidth - fixedWidth;
        if (availableWidth <= 0)
            return;

        WorkspaceGrid.ColumnDefinitions[1].MinWidth = MainContentMinWidth;
        WorkspaceGrid.ColumnDefinitions[1].MaxWidth = MainContentMaxWidth;
        WorkspaceGrid.ColumnDefinitions[3].MinWidth = RightPanelMinWidth;
        WorkspaceGrid.ColumnDefinitions[3].MaxWidth = double.PositiveInfinity;

        var maximumMiddleWidth = Math.Max(
            MainContentMinWidth,
            availableWidth - RightPanelMinWidth);
        var middleWidth = Math.Clamp(
            LayoutSettingValues.ClampMainContentWidth(mainContentWidth),
            MainContentMinWidth,
            Math.Min(MainContentMaxWidth, maximumMiddleWidth));

        WorkspaceGrid.ColumnDefinitions[1].Width =
            new GridLength(middleWidth, GridUnitType.Pixel);
        WorkspaceGrid.ColumnDefinitions[3].Width =
            new GridLength(1, GridUnitType.Star);
    }

    private void CaptureAndSaveLayout()
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        var mainContentWidth = WorkspaceGrid.ColumnDefinitions[1].ActualWidth;
        var topPanelHeight = MainContentGrid.RowDefinitions[0].ActualHeight;
        var bottomPanelHeight = MainContentGrid.RowDefinitions[2].ActualHeight;
        var totalPanelHeight = topPanelHeight + bottomPanelHeight;

        if (!double.IsFinite(mainContentWidth) || mainContentWidth <= 0 ||
            !double.IsFinite(totalPanelHeight) || totalPanelHeight <= 0)
            return;

        viewModel.UpdateLayout(mainContentWidth, topPanelHeight / totalPanelHeight);
        viewModel.SaveLayout();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        layoutSaveTimer.Stop();
        CaptureAndSaveLayout();
        layoutSaveTimer.Tick -= OnLayoutSaveTimerTick;
        SizeChanged -= OnSizeChanged;
    }

    private static Control CreateIcon(AntDesignIconKind kind)
    {
        var icon = (Control)new AntDesignIconProvider(kind).ProvideValue(null!);
        icon.Width = 16;
        icon.Height = 16;
        return icon;
    }
}
