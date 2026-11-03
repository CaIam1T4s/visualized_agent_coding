using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using VisualizedAgentCoding.Blueprint.Core;
using VisualizedAgentCoding.Domain.Blueprint;

namespace happy_agent_coding.Controls;

/// <summary>
/// Free-position node workspace. Python remains the semantic source; this control only
/// draws the projection and lets the user move nodes and pan/zoom the view.
/// </summary>
public sealed class BlueprintWorkspace : Control
{
    private static readonly Typeface TitleTypeface = new("Inter", FontStyle.Normal, FontWeight.SemiBold);
    private static readonly Typeface BodyTypeface = new("Inter", FontStyle.Normal, FontWeight.Normal);
    private static readonly Typeface TinyTypeface = new("Inter", FontStyle.Normal, FontWeight.Medium);
    private static readonly IBrush Background = new SolidColorBrush(Color.Parse("#171A20"));
    private static readonly IBrush GridMinor = new SolidColorBrush(Color.Parse("#20242C"));
    private static readonly IBrush GridMajor = new SolidColorBrush(Color.Parse("#2A303A"));
    private static readonly IBrush NodeBody = new SolidColorBrush(Color.Parse("#232832"));
    private static readonly IBrush RowFill = new SolidColorBrush(Color.Parse("#1C2129"));
    private static readonly IBrush Border = new SolidColorBrush(Color.Parse("#3B4452"));
    private static readonly IBrush Wire = new SolidColorBrush(Color.Parse("#7C8BA1"));
    private static readonly IBrush SelectedBorder = new SolidColorBrush(Color.Parse("#FFD166"));
    private static readonly IBrush TextPrimary = new SolidColorBrush(Color.Parse("#F2F5F9"));
    private static readonly IBrush TextMuted = new SolidColorBrush(Color.Parse("#A8B3C4"));
    private static readonly IBrush ExecBrush = new SolidColorBrush(Color.Parse("#E5E9F0"));
    private static readonly IBrush ValueBrush = new SolidColorBrush(Color.Parse("#6FD3C7"));
    private static readonly IBrush CommentBrush = new SolidColorBrush(Color.Parse("#E2B85C"));
    private static readonly Pen BorderPen = new(Border, 1.2);
    private static readonly Pen WirePen = new(Wire, 1.6);
    private static readonly Pen CommentPen = new(CommentBrush, 1.2, new DashStyle([4, 4], 0));
    private static readonly Dictionary<string, Rect> EmptyBounds = [];

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

    private readonly BlueprintLayoutEngine engine = new();
    private Dictionary<string, Rect> bounds = EmptyBounds;
    private Dictionary<string, int> order = [];
    private Point pan;
    private BlueprintNode? dragging;
    private BlueprintNode? rewiring;
    private Point dragStart;
    private Point dragOrigin;
    private bool panning;
    private Point panStart;
    private Point panOrigin;
    private string? selectedId;

    static BlueprintWorkspace()
    {
        AffectsRender<BlueprintWorkspace>(DocumentProperty, ZoomProperty);
    }

    public BlueprintWorkspace()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Background, new Rect(Bounds.Size));
        var document = Document;
        if (document is null || document.Roots.Count == 0)
        {
            DrawText(context, document?.Error ?? "没有可显示的节点", 24, 24, 14, BodyTypeface, TextMuted);
            return;
        }

        var layout = engine.Layout(document);
        bounds = layout.ToDictionary(item => item.Id, item => new Rect(item.X, item.Y, item.Width, item.Height));
        BuildOrder(document);
        var all = bounds.Values.ToList();
        var minX = all.Count == 0 ? 0 : all.Min(rect => rect.X) - 80;
        var minY = all.Count == 0 ? 0 : all.Min(rect => rect.Y) - 80;
        var maxX = all.Count == 0 ? 1200 : all.Max(rect => rect.Right) + 160;
        var maxY = all.Count == 0 ? 800 : all.Max(rect => rect.Bottom) + 160;

        using (context.PushTransform(Matrix.CreateScale(Zoom, Zoom) * Matrix.CreateTranslation(pan.X, pan.Y)))
        {
            DrawGrid(context, minX, minY, maxX, maxY);
            DrawWires(context, document);
            foreach (var node in Walk(document.Roots))
                DrawNode(context, node);
            foreach (var node in Walk(document.Roots))
                if (node.Kind == NodeKind.Comment && node.AnnotationTarget is { } target)
                    DrawAnnotation(context, node, target);
        }
    }

    protected override Size MeasureOverride(Size availableSize) => new(1200, 800);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        var screen = point.Position;
        if (point.Properties.IsMiddleButtonPressed || point.Properties.IsRightButtonPressed)
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
        var portNode = HitExecOutput(world);
        if (portNode is not null)
        {
            rewiring = portNode;
            selectedId = portNode.Id;
            e.Pointer.Capture(this);
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        var node = HitNode(world);
        if (node is not null && HitCollapse(node, world))
        {
            node.IsCollapsed = !node.IsCollapsed;
            InvalidateVisual();
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
        if (dragging is null) return;
        var world = ToWorld(screen);
        dragging.X = Math.Round(dragOrigin.X + world.X - dragStart.X);
        dragging.Y = Math.Round(dragOrigin.Y + world.Y - dragStart.Y);
        dragging.HasPosition = true;
        InvalidateVisual();
        e.Handled = true;
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
            rewiring = null;
            e.Pointer.Capture(null);
            var target = HitNode(ToWorld(e.GetPosition(this)));
            if (target is not null && target.Id != source.Id)
                ReconnectRequested?.Invoke(this, new BlueprintReconnectEventArgs(source, target));
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

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var screen = e.GetPosition(this);
        var oldZoom = Zoom;
        var factor = e.Delta.Y > 0 ? 1.1 : 1 / 1.1;
        var newZoom = Math.Clamp(oldZoom * factor, 0.35, 2.5);
        if (Math.Abs(newZoom - oldZoom) < 0.0001) return;
        var world = ToWorld(screen);
        Zoom = newZoom;
        pan = new Point(screen.X - world.X * newZoom, screen.Y - world.Y * newZoom);
        InvalidateVisual();
        e.Handled = true;
    }

    private void DrawGrid(DrawingContext context, double minX, double minY, double maxX, double maxY)
    {
        const double step = 24;
        var startX = Math.Floor(minX / step) * step;
        var startY = Math.Floor(minY / step) * step;
        for (var x = startX; x <= maxX; x += step)
        {
            var pen = ((int)Math.Round(x / step)) % 5 == 0 ? new Pen(GridMajor, 1) : new Pen(GridMinor, 1);
            context.DrawLine(pen, new Point(x, startY), new Point(x, maxY));
        }
        for (var y = startY; y <= maxY; y += step)
        {
            var pen = ((int)Math.Round(y / step)) % 5 == 0 ? new Pen(GridMajor, 1) : new Pen(GridMinor, 1);
            context.DrawLine(pen, new Point(startX, y), new Point(maxX, y));
        }
    }

    private void DrawWires(DrawingContext context, BlueprintDocument document)
    {
        for (var index = 0; index + 1 < document.Roots.Count; index++)
            DrawWire(context, document.Roots[index], document.Roots[index + 1], branchRow: -1);

        foreach (var node in Walk(document.Roots))
        {
            var row = 0;
            foreach (var child in node.Children)
                DrawWire(context, node, child, row++);
            foreach (var child in node.ElseChildren)
                DrawWire(context, node, child, row++);
        }
    }

    private void DrawWire(DrawingContext context, BlueprintNode from, BlueprintNode to, int branchRow)
    {
        if (!bounds.TryGetValue(from.Id, out var source) || !bounds.TryGetValue(to.Id, out var target))
            return;
        var start = branchRow < 0
            ? new Point(source.Right, source.Y + 24)
            : new Point(source.Right - 10, source.Y + BlueprintLayoutEngine.HeaderHeight + branchRow * BlueprintLayoutEngine.RowHeight + BlueprintLayoutEngine.RowHeight / 2);
        var end = new Point(target.X, target.Y + 24);
        var middle = Math.Max(start.X + 20, (start.X + end.X) / 2);
        context.DrawLine(WirePen, start, new Point(middle, start.Y));
        context.DrawLine(WirePen, new Point(middle, start.Y), new Point(middle, end.Y));
        context.DrawLine(WirePen, new Point(middle, end.Y), end);
        DrawTriangle(context, end, 6, Wire);
    }

    private void DrawAnnotation(DrawingContext context, BlueprintNode comment, BlueprintNode target)
    {
        if (!bounds.TryGetValue(comment.Id, out var source) || !bounds.TryGetValue(target.Id, out var destination))
            return;
        context.DrawLine(CommentPen, new Point(source.X + 18, source.Bottom),
            new Point(destination.X + 18, destination.Y));
    }

    private void DrawNode(DrawingContext context, BlueprintNode node)
    {
        if (!bounds.TryGetValue(node.Id, out var rect)) return;
        var header = new Rect(rect.X, rect.Y, rect.Width, BlueprintLayoutEngine.HeaderHeight);
        var fill = HeaderBrush(node);
        context.DrawRectangle(NodeBody, BorderPen, new RoundedRect(rect, 10, 10));
        context.DrawRectangle(fill, null, new RoundedRect(header, 10, 10));
        if (node.Id == selectedId)
            context.DrawRectangle(null, new Pen(SelectedBorder, 2), new RoundedRect(rect, 10, 10));
        else if (node.Kind == NodeKind.Comment)
            context.DrawRectangle(null, CommentPen, new RoundedRect(rect, 10, 10));

        DrawText(context, node.DisplayName, header.X + 12, header.Y + 14, 14, TitleTypeface, TextPrimary);
        var orderText = order.TryGetValue(node.Id, out var number) ? $"#{number}" : "";
        DrawText(context, orderText, header.Right - 74, header.Y + 16, 11, TinyTypeface, TextMuted);
        DrawText(context, node.IsCollapsed ? "+" : "−", header.Right - 34, header.Y + 12, 16, TitleTypeface, TextPrimary);
        DrawText(context, "⋯", header.Right - 18, header.Y + 12, 14, TitleTypeface, TextPrimary);

        DrawPort(context, header.X, header.Y + 24, PortKind.Exec, isInput: true);
        DrawPort(context, header.Right, header.Y + 24, PortKind.Exec, isInput: false);

        if (node.IsCollapsed) return;

        var row = 0;
        foreach (var port in node.Ports)
        {
            if (port.Id is "exec-in" or "exec-out") continue;
            var y = rect.Y + BlueprintLayoutEngine.HeaderHeight + row * BlueprintLayoutEngine.RowHeight;
            var rowRect = new Rect(rect.X + 1, y, rect.Width - 2, BlueprintLayoutEngine.RowHeight);
            context.DrawRectangle(row % 2 == 0 ? RowFill : NodeBody, null, rowRect);
            var label = port.Label ?? port.Id;
            var type = port.Type ?? (port.IsExec ? "exec" : "any");
            DrawText(context, label, rowRect.X + 28, rowRect.Y + 8, 12, BodyTypeface, TextPrimary);
            DrawText(context, type, rowRect.X + 28, rowRect.Y + 21, 9, TinyTypeface, TextMuted);
            if (port.IsInput)
                DrawPort(context, rowRect.X, rowRect.Y + 16, port.Kind, true);
            else
                DrawPort(context, rowRect.Right, rowRect.Y + 16, port.Kind, false);
            row++;
        }
    }

    private static void DrawPort(DrawingContext context, double x, double y, PortKind kind, bool isInput)
    {
        var brush = kind == PortKind.Exec ? ExecBrush : ValueBrush;
        var direction = isInput ? 1 : -1;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(x, y), true);
            g.LineTo(new Point(x + 8 * direction, y - 6));
            g.LineTo(new Point(x + 8 * direction, y + 6));
            g.EndFigure(true);
        }
        context.DrawGeometry(brush, null, geometry);
    }

    private static void DrawTriangle(DrawingContext context, Point point, double size, IBrush brush)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(point, true);
            g.LineTo(new Point(point.X - size, point.Y - size / 2));
            g.LineTo(new Point(point.X - size, point.Y + size / 2));
            g.EndFigure(true);
        }
        context.DrawGeometry(brush, null, geometry);
    }

    private static void DrawText(DrawingContext context, string text, double x, double y, double size, Typeface typeface, IBrush brush)
    {
        var formatted = new FormattedText(text ?? string.Empty, CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, typeface, size, brush);
        context.DrawText(formatted, new Point(x, y));
    }

    private static IBrush HeaderBrush(BlueprintNode node) => node.Kind switch
    {
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
        foreach (var node in Walk(document.Roots))
            order[node.Id] = ++number;
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

    private BlueprintNode? HitExecOutput(Point world)
    {
        foreach (var node in Walk(Document!.Roots))
        {
            if (!bounds.TryGetValue(node.Id, out var rect)) continue;
            if (Near(world, new Point(rect.Right, rect.Y + 24), 12)) return node;
            var row = 0;
            foreach (var port in node.Ports)
            {
                if (port.Id is "exec-in" or "exec-out") continue;
                if (port.IsExec && !port.IsInput)
                {
                    var y = rect.Y + BlueprintLayoutEngine.HeaderHeight + row * BlueprintLayoutEngine.RowHeight + 16;
                    if (Near(world, new Point(rect.Right - 10, y), 12)) return node;
                }
                row++;
            }
        }
        return null;
    }

    private static bool Near(Point a, Point b, double distance) =>
        Math.Abs(a.X - b.X) <= distance && Math.Abs(a.Y - b.Y) <= distance;

    private bool HitCollapse(BlueprintNode node, Point world)
    {
        if (!bounds.TryGetValue(node.Id, out var rect)) return false;
        return new Rect(rect.Right - 44, rect.Y, 34, BlueprintLayoutEngine.HeaderHeight).Contains(world);
    }

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

    public sealed class BlueprintNodeSelectedEventArgs(BlueprintNode? node) : EventArgs
    {
        public BlueprintNode? Node { get; } = node;
    }

    public sealed class BlueprintReconnectEventArgs(BlueprintNode source, BlueprintNode target) : EventArgs
    {
        public BlueprintNode Source { get; } = source;
        public BlueprintNode Target { get; } = target;
    }

    private static IEnumerable<BlueprintNode> Walk(IEnumerable<BlueprintNode> roots)
    {
        foreach (var root in roots)
        {
            yield return root;
            foreach (var child in Walk(root.AllChildren)) yield return child;
        }
    }
}
