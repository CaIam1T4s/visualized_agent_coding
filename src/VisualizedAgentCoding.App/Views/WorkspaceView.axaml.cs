using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using AtomUI.Icons.AntDesign;
using happy_agent_coding.Services;
using happy_agent_coding.ViewModels;

namespace happy_agent_coding.Views;

public partial class WorkspaceView : UserControl
{
    private bool applyingLayout;
    private StageObjectItem? draggingSprite;
    private Point dragOffset;
    private Point pressPoint;
    private bool stageDragMoved;

    public WorkspaceView()
    {
        InitializeComponent();
        RunIcon.Content = CreateIcon(AntDesignIconKind.PlayCircleOutlined);
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
        Sidebar.SizeChanged += OnSidebarSizeChanged;
        StagePreviewHost.SizeChanged += OnStagePreviewHostSizeChanged;
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

    private void OnUpgradeWarningAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is ContentControl host) host.Content = CreateIcon(AntDesignIconKind.WarningOutlined);
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
    }

    private void OnSidebarSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateResponsiveColumnWidths();
    }

    private void OnStagePreviewHostSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateStageToolbarWidth();
    }

    /// <summary>Matches the toolbar width to the stage's on-screen width after the 480x360 canvas is uniformly scaled.</summary>
    private void UpdateStageToolbarWidth()
    {
        const double stageDesignWidth = 480;
        const double stageDesignHeight = 360;

        var host = StagePreviewHost.Bounds;
        if (host.Width <= 0 || host.Height <= 0)
            return;

        var scale = Math.Min(host.Width / stageDesignWidth, host.Height / stageDesignHeight);
        StageToolbar.Width = stageDesignWidth * scale;
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

    /// <summary>
    /// Shows the category menu only on empty space. Right-clicking a script row keeps the row's
    /// own context (with its own menu) so the row is never acted on by accident.
    /// </summary>
    private void OnCategoryContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not Control { DataContext: ScriptCategory category } source)
            return;

        if (e.Source is Visual hit && IsWithinScriptRow(hit, source))
        {
            e.Handled = true;
            return;
        }

        var menu = new MenuFlyout();
        menu.Items.Add(new MenuItem
        {
            Header = category.CreateMenuHeader,
            IsEnabled = category.CanCreate,
            Command = category.StartCreateCommand,
            Focusable = false,
        });
        menu.ShowAt(source, true);

        e.Handled = true;
    }

    private void OnScriptContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is Control { DataContext: ProjectScriptItem item } source && DataContext is MainViewModel viewModel)
        {
            ScriptActionMenu.Show(source, viewModel, item);
            e.Handled = true;
        }
    }

    private void OnRenameAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not TextBox textBox) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (textBox.DataContext is ScriptRenameState state &&
                DataContext is MainViewModel vm && !vm.IsRenamePreviewOpen && ReferenceEquals(vm.ScriptRename, state) && textBox.IsEffectivelyVisible)
            {
                textBox.Focus();
                textBox.SelectAll();
            }
        });
    }

    private void OnRenameLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: ScriptRenameState state } &&
            DataContext is MainViewModel vm && !vm.IsRenamePreviewOpen && ReferenceEquals(vm.ScriptRename, state))
            state.CancelCommand.Execute(null);
    }

    private void OnStageCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsWithinStageSprite(e.Source as Visual))
            return;
        if (DataContext is not MainViewModel viewModel)
            return;

        if (viewModel.CurrentBackdrop is { } backdrop)
            viewModel.SelectStageObjectCommand.Execute(backdrop);
        else
            viewModel.ClearStageSelection();
    }

    private void OnStageSpritePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(StageCanvas).Properties.IsLeftButtonPressed) return;
        if (sender is not Control { Tag: StageObjectItem item })
            return;
        if (DataContext is not MainViewModel viewModel)
            return;

        viewModel.SelectStageObjectCommand.Execute(item);
        var canvasPos = e.GetPosition(StageCanvas);
        draggingSprite = item;
        dragOffset = new Point(canvasPos.X - item.CanvasLeft, canvasPos.Y - item.CanvasTop);
        pressPoint = canvasPos;
        stageDragMoved = false;
        e.Pointer.Capture(StageCanvas);
        e.Handled = true;
    }

    private void OnStageCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        if (draggingSprite is null)
            return;
        if (!e.GetCurrentPoint(StageCanvas).Properties.IsLeftButtonPressed)
            return;

        var canvasPos = e.GetPosition(StageCanvas);
        if (!stageDragMoved && Distance(canvasPos, pressPoint) < 4)
            return;

        stageDragMoved = true;
        draggingSprite.SetPositionFromCanvasTopLeft(
            canvasPos.X - dragOffset.X,
            canvasPos.Y - dragOffset.Y);
        e.Handled = true;
    }

    private void OnStageCanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        EndStageDrag();
    }

    private void OnStageCanvasPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        EndStageDrag();
    }

    private void EndStageDrag()
    {
        draggingSprite = null;
        stageDragMoved = false;
    }

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static bool IsWithinStageSprite(Visual? hit)
    {
        for (var current = hit; current is not null; current = current.GetVisualParent())
        {
            if (current is Control { Name: "StageCanvas" })
                return false;
            if (current is Control { Classes: var classes } && classes.Contains("stage-sprite"))
                return true;
        }

        return false;
    }

    /// <summary>Opens the clicked indexed script in the editor. Bound in code so nested items
    /// do not have to walk the visual tree for the workspace view model.</summary>
    private void OnScriptItemClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ProjectScriptItem item })
            return;
        if (DataContext is not MainViewModel viewModel)
            return;

        viewModel.OpenScriptFromExplorerCommand.Execute(item);
    }

    /// <summary>True when the hit control lives inside a script row rather than the category header.</summary>
    private static bool IsWithinScriptRow(Visual hit, Control categoryRoot)
    {
        for (var current = hit; current is not null && current != categoryRoot; current = current.GetVisualParent())
        {
            if (current is Control { Classes: var classes } && (classes.Contains("explorer-item") || classes.Contains("script-row")))
                return true;
        }

        return false;
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
