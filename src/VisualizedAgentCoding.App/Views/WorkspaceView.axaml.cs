using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using AtomUI.Icons.AntDesign;
using happy_agent_coding.Services;
using happy_agent_coding.ViewModels;

namespace happy_agent_coding.Views;

public partial class WorkspaceView : UserControl
{
    private bool applyingLayout;

    public WorkspaceView()
    {
        InitializeComponent();
        RunIcon.Content = CreateIcon(AntDesignIconKind.PlayCircleOutlined);
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
        Sidebar.SizeChanged += OnSidebarSizeChanged;
        HorizontalSplitter.PointerReleased += OnSplitterPointerReleased;
        VerticalSplitter.PointerReleased += OnSplitterPointerReleased;
    }

    private static Control CreateIcon(AntDesignIconKind kind)
    {
        var icon = (Control)new AntDesignIconProvider(kind).ProvideValue(null!);
        icon.Width = 16;
        icon.Height = 16;
        return icon;
    }

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            ApplySavedLayout(viewModel);
            UpdateSidebarMode();
        }
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateSidebarMode();
        UpdateResponsiveColumnWidths();
        UpdateStageToolbarWidth();
    }

    private void OnStageCanvasSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateStageToolbarWidth();
    }

    private void OnSidebarSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveColumnWidths();
        UpdateStageToolbarWidth();
    }

    private void UpdateResponsiveColumnWidths()
    {
        if (DataContext is not MainViewModel viewModel || WorkspaceGrid.Bounds.Width <= 0)
            return;

        var editorMinimumWidth = LayoutSettingValues.MinMainContentWidth;
        var splitterWidth = VerticalSplitter.Bounds.Width > 0
            ? VerticalSplitter.Bounds.Width
            : 8;
        var sidebarWidth = Sidebar.Bounds.Width;
        var availableAuxiliaryWidth = WorkspaceGrid.Bounds.Width
            - sidebarWidth
            - splitterWidth
            - editorMinimumWidth;
        var auxiliaryWidth = Math.Clamp(
            Math.Min(viewModel.AuxiliaryWidth, availableAuxiliaryWidth),
            LayoutSettingValues.MinAuxiliaryWidth,
            LayoutSettingValues.MaxAuxiliaryWidth);

        if (Math.Abs(WorkspaceGrid.ColumnDefinitions[1].ActualWidth - auxiliaryWidth) > 0.5)
            WorkspaceGrid.ColumnDefinitions[1].Width = new GridLength(auxiliaryWidth, GridUnitType.Pixel);
    }

    private void UpdateStageToolbarWidth()
    {
        var canvasWidth = StageCanvasViewbox.Bounds.Width;
        if (canvasWidth > 0)
            StageToolbar.Width = canvasWidth;
    }

    private void ApplySavedLayout(MainViewModel viewModel)
    {
        applyingLayout = true;
        WorkspaceGrid.ColumnDefinitions[1].Width = new GridLength(viewModel.AuxiliaryWidth, GridUnitType.Pixel);
        WorkspaceGrid.ColumnDefinitions[1].MinWidth = LayoutSettingValues.MinAuxiliaryWidth;
        WorkspaceGrid.ColumnDefinitions[3].Width = new GridLength(1, GridUnitType.Star);
        WorkspaceGrid.ColumnDefinitions[3].MinWidth = LayoutSettingValues.MinMainContentWidth;
        LeftWorkspaceGrid.RowDefinitions[0].Height = new GridLength(viewModel.TopPanelRatio, GridUnitType.Star);
        LeftWorkspaceGrid.RowDefinitions[2].Height = new GridLength(1 - viewModel.TopPanelRatio, GridUnitType.Star);
        applyingLayout = false;
    }

    private void OnSplitterPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (applyingLayout || DataContext is not MainViewModel viewModel)
            return;

        var auxiliaryWidth = WorkspaceGrid.ColumnDefinitions[1].ActualWidth;
        var topHeight = LeftWorkspaceGrid.RowDefinitions[0].ActualHeight;
        var bottomHeight = LeftWorkspaceGrid.RowDefinitions[2].ActualHeight;
        if (auxiliaryWidth <= 0 || topHeight <= 0 || bottomHeight <= 0)
            return;

        viewModel.UpdateLayout(
            topHeight / (topHeight + bottomHeight),
            auxiliaryWidth);
    }

    public void SaveCurrentLayout()
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        var auxiliaryWidth = WorkspaceGrid.ColumnDefinitions[1].ActualWidth;
        var topHeight = LeftWorkspaceGrid.RowDefinitions[0].ActualHeight;
        var bottomHeight = LeftWorkspaceGrid.RowDefinitions[2].ActualHeight;
        if (auxiliaryWidth <= 0 || topHeight <= 0 || bottomHeight <= 0)
            return;

        viewModel.UpdateLayout(
            topHeight / (topHeight + bottomHeight),
            auxiliaryWidth);
        viewModel.SaveLayout();
    }

    private void UpdateSidebarMode()
    {
        Sidebar.SetResponsiveCompact(Bounds.Width < 1180);
    }
}
