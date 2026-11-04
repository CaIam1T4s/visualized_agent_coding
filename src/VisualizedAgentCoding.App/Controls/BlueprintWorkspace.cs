using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using VisualizedAgentCoding.Blueprint.Core;
using VisualizedAgentCoding.Domain.Blueprint;

namespace happy_agent_coding.Controls;

/// <summary>
/// Free-position node workspace. Python remains the semantic source; this control only
/// draws the projection and lets the user move nodes and pan/zoom the view.
/// </summary>
public sealed class BlueprintWorkspace : Control
{
    private static readonly Typeface TitleTypeface = new("Microsoft YaHei UI", FontStyle.Normal, FontWeight.Bold);
    private static readonly Typeface BodyTypeface = new("Inter", FontStyle.Normal, FontWeight.Normal);
    private static readonly Typeface CommentTypeface = new("Microsoft YaHei UI", FontStyle.Normal, FontWeight.Normal);
    private static readonly Typeface TinyTypeface = new("Inter", FontStyle.Normal, FontWeight.Medium);
    private static readonly IBrush FallbackBackground = new SolidColorBrush(Color.Parse("#171A20"));
    private static readonly IBrush FallbackGridMinor = new SolidColorBrush(Color.Parse("#20242C"));
    private static readonly IBrush FallbackGridMajor = new SolidColorBrush(Color.Parse("#2A303A"));
    private static readonly IBrush FallbackNodeBody = new SolidColorBrush(Color.Parse("#232832"));
    private static readonly IBrush FallbackRowFill = new SolidColorBrush(Color.Parse("#1C2129"));
    private static readonly IBrush FallbackBorder = new SolidColorBrush(Color.Parse("#3B4452"));
    private static readonly IBrush Wire = new SolidColorBrush(Color.Parse("#FF4D4D"));
    private static readonly IBrush SelectedBorder = new SolidColorBrush(Color.Parse("#FFD166"));
    private static readonly IBrush FallbackTextPrimary = new SolidColorBrush(Color.Parse("#F2F5F9"));
    private static readonly IBrush FallbackTextMuted = new SolidColorBrush(Color.Parse("#A8B3C4"));
    private static readonly IBrush ExecBrush = new SolidColorBrush(Color.Parse("#E5E9F0"));
    private static readonly IBrush ValueBrush = new SolidColorBrush(Color.Parse("#6FD3C7"));
    private static readonly IBrush CommentBrush = new SolidColorBrush(Color.Parse("#E2B85C"));
    private static readonly IBrush FallbackHoverBrush = new SolidColorBrush(Color.Parse("#33FFFFFF"));
    private static readonly Pen FallbackBorderPen = new(FallbackBorder, 1.2);
    private static readonly Pen WirePen = new(Wire, 2.2);
    private static readonly Pen PreviewPen = new(Wire, 2.4, new DashStyle([5, 4], 0));
    private static readonly Pen CommentPen = new(CommentBrush, 1.2, new DashStyle([4, 4], 0));
    private static readonly Dictionary<string, Rect> EmptyBounds = [];

    private IBrush BackgroundBrush => ThemeBrush("SolidBackgroundFillColorBaseBrush", FallbackBackground);
    private IBrush GridMinorBrush => ThemeBrush("ControlStrokeColorSecondaryBrush", FallbackGridMinor);
    private IBrush GridMajorBrush => ThemeBrush("ControlStrokeColorDefaultBrush", FallbackGridMajor);
    private IBrush NodeBodyBrush => ThemeBrush("SolidBackgroundFillColorBaseBrush", FallbackNodeBody);
    private IBrush RowFillBrush => ThemeBrush("ControlFillColorSecondaryBrush", FallbackRowFill);
    private IBrush BorderBrush => ThemeBrush("TextFillColorTertiaryBrush", FallbackBorder);
    private IBrush TextPrimaryBrush => ThemeBrush("TextFillColorPrimaryBrush", FallbackTextPrimary);
    private IBrush TextMutedBrush => ThemeBrush("TextFillColorSecondaryBrush", FallbackTextMuted);
    private IBrush HoverBrushBrush => ThemeBrush("SubtleFillColorSecondaryBrush", FallbackHoverBrush);
    private Pen BorderPen => new(BorderBrush, 1.8);

    public static readonly StyledProperty<BlueprintDocument?> DocumentProperty =
        AvaloniaProperty.Register<BlueprintWorkspace, BlueprintDocument?>(nameof(Document));

    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<BlueprintWorkspace, double>(nameof(Zoom), 1.0);

    public BlueprintDocument? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    public event Action<BlueprintNode>? NodeMoved;
    public event EventHandler<BlueprintNodeSelectedEventArgs>? NodeSelected;
    public event EventHandler<BlueprintReconnectEventArgs>? ReconnectRequested;
    public event EventHandler<BlueprintReconnectEventArgs>? AnnotationRequested;
    public event EventHandler<BlueprintValueEditedEventArgs>? ValueEdited;
    public event EventHandler<string>? InsertRequested;
    public event EventHandler? DeleteRequested;
    public event EventHandler? ArrangeRequested;

    private readonly BlueprintLayoutEngine engine = new();
    private Dictionary<string, Rect> bounds = EmptyBounds;
    private Dictionary<string, int> order = [];
    private Point pan;
    private BlueprintNode? dragging;
    private Point dragStart;
    private Point dragOrigin;
    private BlueprintNode? rewiring;
    private int rewireBranchRow;
    private Point? rewirePoint;
    private bool rewiringAnnotation;
    private (BlueprintNode Node, int Row)? snapTarget;
    private bool panning;
    private Point panStart;
    private Point panOrigin;
    private string? selectedId;
    private string? hoveredCollapseId;
    private BlueprintNode? editingValueNode;
    private NodePort? editingValuePort;
    private string? editingValueText;
    private readonly Dictionary<string, (double From, double To, DateTime Started)> chevronAnimations = [];
    private readonly DispatcherTimer chevronTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };

    static BlueprintWorkspace()
    {
        AffectsRender<BlueprintWorkspace>(DocumentProperty, ZoomProperty);
    }

    public BlueprintWorkspace()
    {
        Focusable = true;
        ClipToBounds = true;
        ContextRequested += OnContextRequested;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        chevronTimer.Tick += (_, _) => AdvanceChevronAnimations();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(BackgroundBrush, new Rect(Bounds.Size));
        var document = Document;
        if (document is null || document.Roots.Count == 0)
        {
            DrawText(context, document?.Error ?? "没有可显示的节点", 24, 24, 14, BodyTypeface, TextMutedBrush);
            return;
        }

        var layout = engine.Layout(document);
        bounds = layout.ToDictionary(item => item.Id, item => new Rect(item.X, item.Y, item.Width, item.Height));
        BuildOrder(document);
        var topLeft = ToWorld(new Point(0, 0));
        var bottomRight = ToWorld(new Point(Bounds.Width, Bounds.Height));

        using (context.PushTransform(Matrix.CreateScale(Zoom, Zoom) * Matrix.CreateTranslation(pan.X, pan.Y)))
        {
            DrawGrid(context, topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
            foreach (var node in WalkVisible(document.Roots))
                DrawNode(context, node);

            using (context.PushOpacity(0.58))
            {
                DrawWires(context, document);
                DrawValueWires(context, document);
                foreach (var node in WalkVisible(document.Roots))
                {
                    if (node.Kind == NodeKind.Comment && node.AnnotationTarget is { } target)
                        DrawAnnotation(context, node, target);
                }
                if (rewiring is not null && rewirePoint is { } point
                    && bounds.TryGetValue(rewiring.Id, out var source))
                {
                    var origin = rewiringAnnotation
                        ? new Point(source.Right - 6, source.Y + BlueprintLayoutEngine.HeaderHeight / 2)
                        : ExecOutputPoint(source, rewiring, rewireBranchRow);
                    context.DrawEllipse(null, new Pen(SelectedBorder, 2), origin, 9, 9);
                    var end = snapTarget is { } snap && bounds.TryGetValue(snap.Node.Id, out var snapRect)
                        ? new Point(snapRect.X + 6, snapRect.Y + BlueprintLayoutEngine.HeaderHeight / 2)
                        : point;
                    if (snapTarget is not null)
                        context.DrawEllipse(null, new Pen(SelectedBorder, 2), end, 9, 9);
                    DrawBezier(context, origin, end, rewiringAnnotation ? CommentPen : PreviewPen);
                }
            }
        }
    }

    protected override Size MeasureOverride(Size availableSize) => new(1200, 800);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        var screen = point.Position;
        if (point.Properties.IsMiddleButtonPressed)
        {
            panning = true;
            panStart = screen;
            panOrigin = pan;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed) return;
        var world = ToWorld(screen);
        if (editingValuePort is not null)
        {
            var inside = HitValueBox(world, out _, out var editingPort, out _)
                && editingPort.Id == editingValuePort.Id;
            if (inside)
            {
                e.Handled = true;
                return;
            }
            CommitValueEdit();
        }
        var annotation = HitAnnotationOutput(world);
        if (annotation is not null)
        {
            rewiring = annotation;
            rewiringAnnotation = true;
            rewireBranchRow = -1;
            rewirePoint = world;
            e.Pointer.Capture(this);
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        var output = HitExecOutput(world);
        if (output is { } hit)
        {
            rewiring = hit.Node;
            rewiringAnnotation = false;
            rewireBranchRow = hit.BranchRow;
            rewirePoint = world;
            e.Pointer.Capture(this);
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        var node = HitNode(world);
        if (node is not null && HitCollapse(node, world))
        {
            var from = node.IsCollapsed ? 0d : Math.PI;
            node.IsCollapsed = !node.IsCollapsed;
            node.IsCollapsedExplicit = true;
            chevronAnimations[node.Id] = (from, node.IsCollapsed ? 0d : Math.PI, DateTime.UtcNow);
            chevronTimer.Start();
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        if (node is not null && e.ClickCount >= 2
            && HitValueBox(world, out var valueNode, out var valuePort, out var valueRect))
        {
            dragging = null;
            BeginValueEdit(valueNode, valuePort, valueRect);
            e.Handled = true;
            return;
        }
        selectedId = node?.Id;
        NodeSelected?.Invoke(this, new BlueprintNodeSelectedEventArgs(node));
        if (node is not null)
        {
            dragging = node;
            dragStart = world;
            dragOrigin = new Point(node.X, node.Y);
            e.Pointer.Capture(this);
        }
        InvalidateVisual();
        e.Handled = node is not null;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var screen = e.GetPosition(this);
        if (panning)
        {
            pan = panOrigin + (screen - panStart);
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        if (rewiring is not null)
        {
            var rewireWorld = ToWorld(screen);
            snapTarget = rewiringAnnotation
                ? HitAnnotationTarget(rewireWorld, rewiring)
                : HitExecInput(rewireWorld, rewiring);
            rewirePoint = snapTarget is { } snap && bounds.TryGetValue(snap.Node.Id, out var rect)
                ? new Point(rect.X + 6, rect.Y + BlueprintLayoutEngine.HeaderHeight / 2)
                : rewireWorld;
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        if (dragging is null)
        {
            UpdateHover(ToWorld(screen));
            return;
        }
        var world = ToWorld(screen);
        dragging.X = Math.Round(dragOrigin.X + world.X - dragStart.X);
        dragging.Y = Math.Round(dragOrigin.Y + world.Y - dragStart.Y);
        dragging.HasPosition = true;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (hoveredCollapseId is null) return;
        hoveredCollapseId = null;
        InvalidateVisual();
    }

    private void UpdateHover(Point world)
    {
        var node = HitNode(world);
        var hovered = node is not null && HitCollapse(node, world) ? node.Id : null;
        if (hovered == hoveredCollapseId) return;
        hoveredCollapseId = hovered;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (panning)
        {
            panning = false;
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }
        if (rewiring is not null)
        {
            var source = rewiring;
            var annotation = rewiringAnnotation;
            rewiring = null;
            rewiringAnnotation = false;
            rewirePoint = null;
            var snapped = snapTarget;
            snapTarget = null;
            e.Pointer.Capture(null);
            var target = snapped?.Node ?? HitNode(ToWorld(e.GetPosition(this)));
            if (target is not null && target.Id != source.Id)
            {
                var args = new BlueprintReconnectEventArgs(source, target);
                if (annotation) AnnotationRequested?.Invoke(this, args);
                else ReconnectRequested?.Invoke(this, args);
            }
            e.Handled = true;
            return;
        }
        if (dragging is not null)
        {
            var moved = dragging;
            dragging = null;
            e.Pointer.Capture(null);
            NodeMoved?.Invoke(moved);
            e.Handled = true;
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        panning = false;
        dragging = null;
        rewiring = null;
        rewiringAnnotation = false;
        snapTarget = null;
        rewirePoint = null;
        InvalidateVisual();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var screen = e.GetPosition(this);
        var world = ToWorld(screen);
        var comment = HitNode(world);
        if (comment is { Kind: NodeKind.Comment, IsCollapsed: false } && !string.IsNullOrEmpty(comment.Text))
        {
            var lines = CommentText(comment);
            if (lines.Length > 5)
            {
                comment.CommentScroll = Math.Clamp(comment.CommentScroll + (e.Delta.Y < 0 ? 1 : -1), 0, lines.Length - 5);
                InvalidateVisual();
                e.Handled = true;
                return;
            }
        }
        var oldZoom = Zoom;
        var factor = e.Delta.Y > 0 ? 1.1 : 1 / 1.1;
        var newZoom = Math.Clamp(oldZoom * factor, 0.35, 2.5);
        if (Math.Abs(newZoom - oldZoom) < 0.0001) return;
        Zoom = newZoom;
        pan = new Point(screen.X - world.X * newZoom, screen.Y - world.Y * newZoom);
        InvalidateVisual();
        e.Handled = true;
    }

    private bool HitValueBox(Point world, out BlueprintNode node, out NodePort port, out Rect rect)
    {
        node = null!;
        port = null!;
        rect = default;
        foreach (var candidate in WalkVisible(Document!.Roots))
        {
            if (candidate.Kind == NodeKind.Comment || candidate.ValueInputPortId is not null
                || !bounds.TryGetValue(candidate.Id, out var nodeRect)) continue;
            var rows = BlueprintNodeRows.Build(candidate);
            for (var index = 0; index < rows.Count; index++)
            {
                var input = rows[index].Input;
                if (input is null || !BlueprintNodeRows.CanUseInlineValue(rows[index])
                    || candidate.ValueInputs.Any(value => value.ValueInputPortId == input.Id)) continue;
                var rowRect = new Rect(nodeRect.X + 1,
                    nodeRect.Y + BlueprintLayoutEngine.HeaderHeight + index * BlueprintLayoutEngine.RowHeight,
                    nodeRect.Width - 2, BlueprintLayoutEngine.RowHeight);
                var box = ValueBoxRect(rowRect, rows[index].Output is not null, rows[index].Value!);
                if (!box.Contains(world)) continue;
                node = candidate;
                port = input;
                rect = box;
                return true;
            }
        }
        return false;
    }

    private Rect ValueBoxRect(Rect rowRect, bool hasOutput, string value)
    {
        var formatted = new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            TinyTypeface, 11, TextPrimaryBrush);
        var available = rowRect.Width - 16 - (hasOutput ? 34 : 18);
        var width = Math.Max(48, Math.Min(available, formatted.Width + 16));
        return new Rect(rowRect.Right - (hasOutput ? 24 : 10) - width, rowRect.Y + 5, width, 22);
    }

    private void BeginValueEdit(BlueprintNode node, NodePort port, Rect worldRect)
    {
        editingValueNode = node;
        editingValuePort = port;
        editingValueText = port.DefaultText ?? string.Empty;
        Focus();
        InvalidateVisual();
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (editingValuePort is null) return;
        editingValueText = (editingValueText ?? string.Empty) + e.Text;
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (editingValuePort is null) return;
        if (e.Key == Key.Back)
        {
            var text = editingValueText ?? string.Empty;
            editingValueText = text.Length > 0 ? text[..^1] : string.Empty;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            CommitValueEdit();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            EndValueEdit();
            e.Handled = true;
        }
        else
        {
            return;
        }
        InvalidateVisual();
    }

    private void CommitValueEdit()
    {
        if (editingValueNode is null || editingValuePort is null) return;
        var value = editingValueText ?? string.Empty;
        var node = editingValueNode;
        var port = editingValuePort;
        EndValueEdit();
        ValueEdited?.Invoke(this, new BlueprintValueEditedEventArgs(node, port, value));
    }

    private void EndValueEdit()
    {
        editingValueNode = null;
        editingValuePort = null;
        editingValueText = null;
    }

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        ShowContextMenu();
        e.Handled = true;
    }

    private void ShowContextMenu()
    {
        var menu = new MenuFlyout();
        foreach (var (header, code) in new[]
        {
            ("插入移动 10 步", "self.move(10)"),
            ("插入旋转 15 度", "self.turn(15)"),
            ("插入设置变量", "self.health = 100"),
            ("插入增加变量", "self.health += 1"),
            ("插入如果", "if True:\n    pass"),
            ("插入循环", "while True:\n    pass"),
        })
        {
            var item = new MenuItem { Header = header, Tag = code };
            item.Click += (_, _) => InsertRequested?.Invoke(this, code);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var arrange = new MenuItem { Header = "重新排列节点" };
        arrange.Click += (_, _) => ArrangeRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(arrange);
        var delete = new MenuItem { Header = "删除选中节点" };
        delete.Click += (_, _) => DeleteRequested?.Invoke(this, EventArgs.Empty);
        menu.Items.Add(delete);
        menu.ShowAt(this, true);
    }

    private void DrawGrid(DrawingContext context, double minX, double minY, double maxX, double maxY)
    {
        const double step = 24;
        var startX = Math.Floor(minX / step) * step;
        var startY = Math.Floor(minY / step) * step;
        var columns = (int)Math.Min(400, Math.Ceiling((maxX - minX) / step) + 2);
        var rows = (int)Math.Min(400, Math.Ceiling((maxY - minY) / step) + 2);
        for (var index = 0; index < columns; index++)
        {
            var x = startX + index * step;
            var pen = ((int)Math.Round(x / step)) % 5 == 0 ? new Pen(GridMajorBrush, 1) : new Pen(GridMinorBrush, 1);
            context.DrawLine(pen, new Point(x, startY), new Point(x, startY + rows * step));
        }
        for (var index = 0; index < rows; index++)
        {
            var y = startY + index * step;
            var pen = ((int)Math.Round(y / step)) % 5 == 0 ? new Pen(GridMajorBrush, 1) : new Pen(GridMinorBrush, 1);
            context.DrawLine(pen, new Point(startX, y), new Point(startX + columns * step, y));
        }
    }

    private void DrawValueWires(DrawingContext context, BlueprintDocument document)
    {
        foreach (var node in WalkVisible(document.Roots))
        {
            if (node.IsCollapsed || node.ValueInputs.Count == 0) continue;
            if (!bounds.TryGetValue(node.Id, out var target)) continue;
            var rows = BlueprintNodeRows.Build(node);
            foreach (var value in node.ValueInputs)
            {
                if (!bounds.TryGetValue(value.Id, out var source)) continue;
                var index = rows.ToList().FindIndex(row => row.Input?.Id == value.ValueInputPortId);
                if (index < 0) continue;
                var start = new Point(source.Right - 6, source.Y + BlueprintLayoutEngine.HeaderHeight / 2);
                var end = new Point(target.X + 6,
                    target.Y + BlueprintLayoutEngine.HeaderHeight + index * BlueprintLayoutEngine.RowHeight
                    + BlueprintLayoutEngine.RowHeight / 2);
                DrawBezier(context, start, end, new Pen(ValueBrush, 2.2));
            }
        }
    }

    private void DrawWires(DrawingContext context, BlueprintDocument document)
    {
        DrawSiblingWires(context, document.Roots);

        foreach (var node in WalkVisible(document.Roots))
        {
            if (node.Kind == NodeKind.Comment) continue;
            if (node.Type == "control.branch")
            {
                if (!node.IsCollapsed) DrawSiblingWires(context, node.Children);
                continue;
            }
            if (node.IsCollapsed) continue;
            if (node.Type is not ("control.if" or "control.match" or "control.case"))
                DrawSiblingWires(context, node.Children);
            DrawSiblingWires(context, node.ElseChildren);
            if (node.Type == "control.if")
            {
                for (var index = 0; index < node.Children.Count; index++)
                {
                    var branch = node.Children[index];
                    var first = branch.Children.FirstOrDefault(child => child.Kind != NodeKind.Comment);
                    if (first is not null)
                        DrawWire(context, node, first, BranchRowFor(node, index));
                }
                var elseFirst = node.ElseChildren.FirstOrDefault(child => child.Kind != NodeKind.Comment);
                if (elseFirst is not null)
                    DrawWire(context, node, elseFirst, BranchRowFor(node, node.Children.Count));
                continue;
            }
            if (node.Type == "control.case")
            {
                DrawSiblingWires(context, node.Children);
                var first = node.Children.FirstOrDefault(child => child.Kind != NodeKind.Comment);
                if (first is not null) DrawWire(context, node, first, -1);
                continue;
            }
            if (node.Kind == NodeKind.Start)
            {
                var first = node.Children.FirstOrDefault(child => child.Kind != NodeKind.Comment);
                if (first is not null) DrawWire(context, node, first, BranchRowFor(node, 0));
            }
            else
            {
                for (var index = 0; index < node.Children.Count; index++)
                {
                    if (node.Children[index].Kind != NodeKind.Comment)
                        DrawWire(context, node, node.Children[index], BranchRowFor(node, index));
                }
            }
            for (var index = 0; index < node.ElseChildren.Count; index++)
            {
                if (node.ElseChildren[index].Kind != NodeKind.Comment)
                    DrawWire(context, node, node.ElseChildren[index], BranchRowFor(node, node.Children.Count + index));
            }
        }
    }

    private static int BranchRowFor(BlueprintNode node, int branchIndex)
    {
        var rows = BlueprintNodeRows.Build(node);
        var seen = 0;
        for (var index = 0; index < rows.Count; index++)
        {
            if (rows[index].Output?.IsExec != true) continue;
            if (seen++ == branchIndex) return index;
        }
        return Math.Max(0, branchIndex);
    }

    private void DrawSiblingWires(DrawingContext context, IReadOnlyList<BlueprintNode> nodes)
    {
        var executable = nodes.Where(node => node.Kind != NodeKind.Comment).ToList();
        for (var index = 0; index + 1 < executable.Count; index++)
            DrawWire(context, executable[index], executable[index + 1], branchRow: -1);
    }

    private void DrawWire(DrawingContext context, BlueprintNode from, BlueprintNode to, int branchRow)
    {
        if (!bounds.TryGetValue(from.Id, out var source) || !bounds.TryGetValue(to.Id, out var target))
            return;
        var startPoint = ExecOutputPoint(source, from, branchRow);
        var endPoint = new Point(target.X + 6, target.Y + BlueprintLayoutEngine.HeaderHeight / 2);
        DrawBezier(context, startPoint, endPoint, WirePenFor(from));
    }

    private static Point ExecOutputPoint(Rect source, BlueprintNode node, int branchRow)
    {
        if (branchRow < 0)
            return new Point(source.Right - 6, source.Y + BlueprintLayoutEngine.HeaderHeight / 2);
        return new Point(source.Right - 6,
            source.Y + BlueprintLayoutEngine.HeaderHeight + branchRow * BlueprintLayoutEngine.RowHeight + BlueprintLayoutEngine.RowHeight / 2);
    }

    private static Pen WirePenFor(BlueprintNode node) => new(HeaderBrush(node), 2.2);

    private static void DrawBezier(DrawingContext context, Point start, Point end, Pen pen)
    {
        var distance = Math.Clamp(Math.Abs(end.X - start.X) * 0.75, 72, 420);
        if (Math.Abs(end.Y - start.Y) <= 2.5)
        {
            context.DrawLine(FadingPen(pen), start, end);
            return;
        }
        var c1 = new Point(start.X + distance, start.Y);
        var c2 = new Point(end.X - distance, end.Y);
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(start, false);
            g.CubicBezierTo(c1, c2, end);
            g.EndFigure(false);
        }
        context.DrawGeometry(null, FadingPen(pen), geometry);
    }

    private static Pen FadingPen(Pen pen)
    {
        if (pen.Brush is not ISolidColorBrush solid) return pen;
        var color = solid.Color;
        var transparent = Color.FromArgb(0, color.R, color.G, color.B);
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(transparent, 0),
                new GradientStop(color, 0.16),
                new GradientStop(color, 0.84),
                new GradientStop(transparent, 1),
            },
        };
        return new Pen(brush, pen.Thickness, pen.DashStyle, pen.LineCap, pen.LineJoin, pen.MiterLimit);
    }

    private void DrawAnnotation(DrawingContext context, BlueprintNode comment, BlueprintNode target)
    {
        if (!bounds.TryGetValue(comment.Id, out var source) || !bounds.TryGetValue(target.Id, out var destination))
            return;
        DrawBezier(context, new Point(source.Right - 6, source.Y + BlueprintLayoutEngine.HeaderHeight / 2),
            new Point(destination.X + 6, destination.Y + BlueprintLayoutEngine.HeaderHeight / 2), CommentPen);
    }

    private void DrawNode(DrawingContext context, BlueprintNode node)
    {
        if (node.Type == "control.branch") return;
        if (!bounds.TryGetValue(node.Id, out var rect)) return;
        var header = new Rect(rect.X, rect.Y, rect.Width, BlueprintLayoutEngine.HeaderHeight);
        if (node.ValueInputPortId is not null)
        {
            context.DrawRectangle(NodeBodyBrush, null, new RoundedRect(rect, 10));
            context.DrawRectangle(HeaderBrush(node), null, new RoundedRect(header, 10, 10, 0, 0));
            var valueTitle = Format(node.DisplayName, 12, TitleTypeface, TextPrimaryBrush);
            context.DrawText(valueTitle, new Point(header.X + 14,
                header.Y + (BlueprintLayoutEngine.HeaderHeight - valueTitle.Height) / 2));
            DrawText(context, node.Text ?? string.Empty, rect.X + 14, header.Bottom + 4, 11, BodyTypeface, TextPrimaryBrush);
            DrawPort(context, header.Right - 2, header.Y + BlueprintLayoutEngine.HeaderHeight / 2,
                PortKind.Value, false, ValueBrush);
            DrawNodeOutline(context, node, rect);
            return;
        }
        var rows = node.Kind == NodeKind.Comment ? [] : BlueprintNodeRows.Build(node);
        var commentLines = node.Kind == NodeKind.Comment && !node.IsCollapsed
            ? CommentText(node).Length
            : 0;
        var hasBody = !node.IsCollapsed && (rows.Count > 0 || commentLines > 0);
        var radius = hasBody ? new CornerRadius(10, 10, 0, 0) : new CornerRadius(10);
        var fill = HeaderBrush(node);
        context.DrawRectangle(NodeBodyBrush, null, new RoundedRect(rect, 10, 10));
        context.DrawRectangle(fill, null, new RoundedRect(header, radius));
        if (node.Id == hoveredCollapseId && CanCollapse(node))
            context.DrawRectangle(HoverBrushBrush, null,
                new RoundedRect(new Rect(header.Right - 44, header.Y + 2, 28, BlueprintLayoutEngine.HeaderHeight - 4), 5));
        var title = Format(node.DisplayName, 13, TitleTypeface, TextPrimaryBrush);
        context.DrawText(title, new Point(header.X + 14, header.Y + (BlueprintLayoutEngine.HeaderHeight - title.Height) / 2));
        if (!string.IsNullOrWhiteSpace(node.InlineComment))
        {
            var inline = Format(node.InlineComment, 9, TinyTypeface, TextMutedBrush);
            context.DrawText(inline, new Point(header.X + 14 + title.Width + 8,
                header.Y + (BlueprintLayoutEngine.HeaderHeight - inline.Height) / 2));
        }
        if (order.TryGetValue(node.Id, out var number))
        {
            var orderText = Format($"#{number}", 10, TinyTypeface, TextMutedBrush);
            context.DrawText(orderText, new Point(header.Right - 58,
                header.Y + (BlueprintLayoutEngine.HeaderHeight - orderText.Height) / 2));
        }
        if (CanCollapse(node))
            DrawChevron(context, header.Right - 30,
                header.Y + BlueprintLayoutEngine.HeaderHeight / 2, ChevronAngle(node));

        if (node.Kind == NodeKind.Comment)
        {
            DrawPort(context, header.Right - 2, header.Y + BlueprintLayoutEngine.HeaderHeight / 2,
                PortKind.Value, false, CommentBrush);
            if (!node.IsCollapsed)
            {
                var lines = CommentText(node);
                var visible = Math.Min(5, lines.Length);
                var startLine = Math.Clamp(node.CommentScroll, 0, Math.Max(0, lines.Length - 5));
                var bodyHeight = rect.Height - BlueprintLayoutEngine.HeaderHeight;
                var contentHeight = visible * 18;
                var top = rect.Y + BlueprintLayoutEngine.HeaderHeight
                    + Math.Max(3, (bodyHeight - contentHeight) / 2);
                for (var index = 0; index < visible; index++)
                    DrawText(context, lines[startLine + index], header.X + 14, top + index * 18,
                        13, CommentTypeface, TextPrimaryBrush);
                if (lines.Length > 5)
                {
                    var track = new Rect(rect.Right - 7, rect.Y + BlueprintLayoutEngine.HeaderHeight + 4,
                        3, Math.Max(12, bodyHeight - 8));
                    context.DrawRectangle(ThemeBrush("ControlFillColorSecondaryBrush", FallbackRowFill), null,
                        new RoundedRect(track, 2));
                    var thumbHeight = Math.Max(18, track.Height * visible / lines.Length);
                    var maxOffset = Math.Max(1, lines.Length - visible);
                    var thumbY = track.Y + (track.Height - thumbHeight) * startLine / maxOffset;
                    context.DrawRectangle(ThemeBrush("TextFillColorTertiaryBrush", FallbackTextMuted), null,
                        new RoundedRect(new Rect(track.X, thumbY, track.Width, thumbHeight), 2));
                }
                DrawNodeOutline(context, node, rect);
                return;
            }
            DrawNodeOutline(context, node, rect);
            return;
        }

        if (node.Ports.Any(port => port.Id == "exec-in"))
            DrawPort(context, header.X + 2, header.Y + BlueprintLayoutEngine.HeaderHeight / 2, PortKind.Exec, true);
        if (node.Ports.Any(port => port.Id == "exec-out"))
            DrawPort(context, header.Right - 2, header.Y + BlueprintLayoutEngine.HeaderHeight / 2, PortKind.Exec, false);

        if (node.IsCollapsed)
        {
            DrawNodeOutline(context, node, rect);
            return;
        }
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var input = row.Input;
            var output = row.Output;
            var y = rect.Y + BlueprintLayoutEngine.HeaderHeight + index * BlueprintLayoutEngine.RowHeight;
            var rowRect = new Rect(rect.X + 1, y, rect.Width - 2, BlueprintLayoutEngine.RowHeight);
            using (context.PushClip(new RoundedRect(rect, 10, 10)))
                context.DrawRectangle(index % 2 == 0 ? RowFillBrush : NodeBodyBrush, null, rowRect);
            var label = row.Label;
            var type = row.Type;
            if (input is not null)
            {
                var labelX = rowRect.X + 13;
                DrawText(context, label, labelX, rowRect.Y + 5, 12, BodyTypeface, TextPrimaryBrush);
                DrawText(context, type, labelX, rowRect.Y + 19, 9, TinyTypeface, TextMutedBrush);
                if (BlueprintNodeRows.CanUseInlineValue(row)
                    && !node.ValueInputs.Any(value => value.ValueInputPortId == input.Id))
                {
                    var editing = editingValuePort?.Id == input.Id;
                    var displayValue = editing ? editingValueText ?? string.Empty : row.Value!;
                    var formatted = Format(displayValue, 11, TinyTypeface, TextPrimaryBrush);
                    var slot = ValueBoxRect(rowRect, output is not null, displayValue);
                    var slotBrush = editing
                        ? ThemeBrush("ControlFillColorTertiaryBrush", FallbackRowFill)
                        : RowFillBrush;
                    context.DrawRectangle(slotBrush, new Pen(editing ? TextPrimaryBrush : BorderBrush, editing ? 2 : 1),
                        new RoundedRect(slot, 4));
                    context.DrawText(formatted, new Point(slot.X + 8, slot.Y + 4));
                    if (editing)
                        context.DrawLine(new Pen(TextPrimaryBrush, 1),
                            new Point(slot.X + 8 + formatted.Width, slot.Y + 5),
                            new Point(slot.X + 8 + formatted.Width, slot.Bottom - 5));
                }
            }
            else
            {
                DrawText(context, label, rowRect.X + 13, rowRect.Y + 5, 12, BodyTypeface, TextPrimaryBrush);
                DrawText(context, type, rowRect.X + 13, rowRect.Y + 19, 9, TinyTypeface, TextMutedBrush);
            }
            if (input is not null)
                DrawPort(context, rowRect.X + 1, rowRect.Y + BlueprintLayoutEngine.RowHeight / 2, input.Kind, true);
            if (output is not null)
                DrawPort(context, rowRect.Right - 1, rowRect.Y + BlueprintLayoutEngine.RowHeight / 2, output.Kind, false);
        }
        DrawNodeOutline(context, node, rect);
    }

    private void DrawNodeOutline(DrawingContext context, BlueprintNode node, Rect rect)
    {
        var pen = node.Id == selectedId
            ? new Pen(SelectedBorder, 2)
            : node.Kind == NodeKind.Comment ? CommentPen : BorderPen;
        context.DrawRectangle(null, pen, new RoundedRect(rect, 10, 10));
    }

    private double ChevronAngle(BlueprintNode node)
    {
        if (!chevronAnimations.TryGetValue(node.Id, out var animation)) return node.IsCollapsed ? 0 : Math.PI;
        var progress = Math.Clamp((DateTime.UtcNow - animation.Started).TotalMilliseconds / 160d, 0d, 1d);
        var eased = 1 - Math.Pow(1 - progress, 3);
        return animation.From + (animation.To - animation.From) * eased;
    }

    private void AdvanceChevronAnimations()
    {
        var finished = new List<string>();
        foreach (var pair in chevronAnimations)
        {
            if ((DateTime.UtcNow - pair.Value.Started).TotalMilliseconds >= 160) finished.Add(pair.Key);
        }
        foreach (var id in finished) chevronAnimations.Remove(id);
        if (chevronAnimations.Count == 0) chevronTimer.Stop();
        InvalidateVisual();
    }

    private void DrawChevron(DrawingContext context, double x, double y, double angle)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(-4, -2), false);
            g.LineTo(new Point(0, 2));
            g.LineTo(new Point(4, -2));
            g.EndFigure(false);
        }
        using (context.PushTransform(Matrix.CreateRotation(angle) * Matrix.CreateTranslation(x, y)))
            context.DrawGeometry(null, new Pen(TextPrimaryBrush, 1.8), geometry);
    }

    private static string[] CommentText(BlueprintNode node)
    {
        if (string.IsNullOrEmpty(node.Text)) return [];
        var lines = node.Text.Replace("\r\n", "\n").Split('\n')
            .Select(line => line.TrimStart()).ToList();
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0])) lines.RemoveAt(0);
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1])) lines.RemoveAt(lines.Count - 1);
        return lines.ToArray();
    }
    private static void DrawPort(DrawingContext context, double x, double y, PortKind kind, bool isInput, IBrush? overrideBrush = null)
    {
        var brush = overrideBrush ?? (kind == PortKind.Exec ? ExecBrush : ValueBrush);
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            if (isInput)
            {
                g.BeginFigure(new Point(x, y - 6), true);
                g.LineTo(new Point(x + 8, y));
                g.LineTo(new Point(x, y + 6));
            }
            else
            {
                g.BeginFigure(new Point(x - 8, y - 6), true);
                g.LineTo(new Point(x, y));
                g.LineTo(new Point(x - 8, y + 6));
            }
            g.EndFigure(true);
        }
        context.DrawGeometry(brush, null, geometry);
    }

    private static void DrawText(DrawingContext context, string text, double x, double y, double size, Typeface typeface, IBrush brush)
    {
        context.DrawText(Format(text, size, typeface, brush), new Point(x, y));
    }

    private static FormattedText Format(string text, double size, Typeface typeface, IBrush brush) =>
        new(text ?? string.Empty, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, size, brush);

    private IBrush ThemeBrush(string key, IBrush fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush
            ? brush
            : fallback;

    private static IBrush HeaderBrush(BlueprintNode node) => node.Kind switch
    {
        NodeKind.Start => new SolidColorBrush(Color.Parse("#2E6FB7")),
        NodeKind.ControlFlow => new SolidColorBrush(Color.Parse("#6D5BD0")),
        NodeKind.Data or NodeKind.Expression => new SolidColorBrush(Color.Parse("#237C77")),
        NodeKind.Comment => new SolidColorBrush(Color.Parse("#7A5A1E")),
        NodeKind.Function or NodeKind.Class => new SolidColorBrush(Color.Parse("#8A4B3A")),
        NodeKind.Opaque => new SolidColorBrush(Color.Parse("#4A4F58")),
        _ => new SolidColorBrush(Color.Parse("#2E6FB7")),
    };

    private void BuildOrder(BlueprintDocument document)
    {
        order = [];
        var number = 0;
        foreach (var root in document.Roots)
        {
            if (root.Kind == NodeKind.Comment) continue;
            order[root.Id] = ++number;
            if (root.Kind != NodeKind.Start) continue;
            foreach (var child in root.Children)
            {
                if (child.Kind == NodeKind.Comment) continue;
                order[child.Id] = ++number;
            }
        }
    }

    private Point ToWorld(Point screen) => new((screen.X - pan.X) / Zoom, (screen.Y - pan.Y) / Zoom);

    private BlueprintNode? HitNode(Point world)
    {
        foreach (var pair in bounds.OrderByDescending(item => item.Value.Y))
        {
            if (pair.Value.Contains(world))
                return FindNode(Document!.Roots, pair.Key);
        }
        return null;
    }

    private BlueprintNode? HitAnnotationOutput(Point world)
    {
        foreach (var node in WalkVisible(Document!.Roots))
        {
            if (node.Kind != NodeKind.Comment || !bounds.TryGetValue(node.Id, out var rect)) continue;
            if (Near(world, new Point(rect.Right - 6, rect.Y + BlueprintLayoutEngine.HeaderHeight / 2), 14))
                return node;
        }
        return null;
    }

    private (BlueprintNode Node, int Row)? HitAnnotationTarget(Point world, BlueprintNode source)
    {
        foreach (var node in WalkVisible(Document!.Roots))
        {
            if (node.Id == source.Id || !bounds.TryGetValue(node.Id, out var rect)) continue;
            var header = new Rect(rect.X, rect.Y, rect.Width, BlueprintLayoutEngine.HeaderHeight);
            if (header.Contains(world)) return (node, -1);
        }
        return null;
    }

    private (BlueprintNode Node, int Row)? HitExecInput(Point world, BlueprintNode source)
    {
        foreach (var node in WalkVisible(Document!.Roots))
        {
            if (node.Id == source.Id || node.Kind == NodeKind.Comment) continue;
            if (!bounds.TryGetValue(node.Id, out var rect)) continue;
            if (!node.Ports.Any(port => port.Id == "exec-in")) continue;
            if (Near(world, new Point(rect.X + 6, rect.Y + BlueprintLayoutEngine.HeaderHeight / 2), 18))
                return (node, -1);
        }
        return null;
    }

    private (BlueprintNode Node, int BranchRow)? HitExecOutput(Point world)
    {
        foreach (var node in WalkVisible(Document!.Roots))
        {
            if (node.Kind == NodeKind.Comment || !bounds.TryGetValue(node.Id, out var rect)) continue;
            if (node.Ports.Any(port => port.Id == "exec-out") && Near(world, new Point(rect.Right - 6, rect.Y + BlueprintLayoutEngine.HeaderHeight / 2), 14))
                return (node, -1);
            var rows = BlueprintNodeRows.Build(node);
            for (var index = 0; index < rows.Count; index++)
            {
                if (rows[index].Output?.IsExec != true) continue;
                var y = rect.Y + BlueprintLayoutEngine.HeaderHeight + index * BlueprintLayoutEngine.RowHeight + BlueprintLayoutEngine.RowHeight / 2;
                if (Near(world, new Point(rect.Right - 6, y), 14)) return (node, index);
            }
        }
        return null;
    }

    private static bool Near(Point a, Point b, double distance) =>
        Math.Abs(a.X - b.X) <= distance && Math.Abs(a.Y - b.Y) <= distance;

    private bool HitCollapse(BlueprintNode node, Point world)
    {
        if (!CanCollapse(node) || !bounds.TryGetValue(node.Id, out var rect)) return false;
        return new Rect(rect.Right - 44, rect.Y, 28, BlueprintLayoutEngine.HeaderHeight).Contains(world);
    }

    private static bool CanCollapse(BlueprintNode node) =>
        node.Kind == NodeKind.Comment
            ? !string.IsNullOrWhiteSpace(node.Text)
            : node.Children.Count > 0 || node.ElseChildren.Count > 0
              || node.ValueInputs.Count > 0 || BlueprintNodeRows.Count(node) > 0;

    private static BlueprintNode? FindNode(IEnumerable<BlueprintNode> nodes, string id)
    {
        foreach (var node in nodes)
        {
            if (node.Id == id) return node;
            var found = FindNode(node.AllChildren, id);
            if (found is not null) return found;
        }
        return null;
    }

    private static IEnumerable<BlueprintNode> Walk(IEnumerable<BlueprintNode> roots) =>
        Walk(roots, []);

    private static IEnumerable<BlueprintNode> WalkVisible(IEnumerable<BlueprintNode> roots) =>
        WalkVisible(roots, []);

    private static IEnumerable<BlueprintNode> WalkVisible(IEnumerable<BlueprintNode> roots, HashSet<string> visited)
    {
        foreach (var root in roots)
        {
            if (!visited.Add(root.Id)) continue;
            yield return root;
            if (root.IsCollapsed) continue;
            foreach (var child in WalkVisible(root.AllChildren, visited)) yield return child;
        }
    }

    private static IEnumerable<BlueprintNode> Walk(IEnumerable<BlueprintNode> roots, HashSet<string> visited)
    {
        foreach (var root in roots)
        {
            if (!visited.Add(root.Id)) continue;
            yield return root;
            foreach (var child in Walk(root.AllChildren, visited)) yield return child;
        }
    }

    public sealed class BlueprintNodeSelectedEventArgs(BlueprintNode? node) : EventArgs
    {
        public BlueprintNode? Node { get; } = node;
    }

    public sealed class BlueprintReconnectEventArgs(BlueprintNode source, BlueprintNode target) : EventArgs
    {
        public BlueprintNode Source { get; } = source;
        public BlueprintNode Target { get; } = target;
    }

    public sealed class BlueprintValueEditedEventArgs(BlueprintNode node, NodePort port, string value) : EventArgs
    {
        public BlueprintNode Node { get; } = node;
        public NodePort Port { get; } = port;
        public string Value { get; } = value;
    }
}
