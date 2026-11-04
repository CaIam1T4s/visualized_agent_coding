using VisualizedAgentCoding.Domain.Blueprint;

namespace VisualizedAgentCoding.Blueprint.Core;

public readonly record struct NodeSize(double Width, double Height);

public sealed record NodeBounds(string Id, double X, double Y, double Width, double Height, int Rows);

/// <summary>
/// Deterministic layout for a projected document. Positions are view state only;
/// the engine never touches source order.
/// </summary>
public sealed class BlueprintLayoutEngine
{
    public const double MinWidth = 220;
    public const double HeaderHeight = 26;
    public const double RowHeight = 32;
    public const double Padding = 0;
    public const double ChainGap = 24;
    public const double BranchGap = 28;
    public const double RootGap = 28;
    public const double CommentPadding = 6;

    public NodeSize Measure(BlueprintNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.ValueInputPortId is not null)
        {
            var valueWidth = Math.Max(120, TextWidth(node.Text, 12) + 28);
            return new NodeSize(valueWidth, HeaderHeight + 22);
        }
        var rows = BlueprintNodeRows.Build(node);
        var rowCount = rows.Count;
        var titleWidth = TextWidth(node.DisplayName, 13, bold: true);
        var rowWidth = rows.Count == 0 ? 0 : rows.Max(RowWidth);
        var commentWidth = ContentWidth(node);
        var inlineWidth = string.IsNullOrWhiteSpace(node.InlineComment)
            ? 0
            : TextWidth(node.InlineComment, 9) + 12;
        var width = Math.Max(MinWidth, 28 + titleWidth + inlineWidth + 26);
        width = Math.Max(width, rowWidth + 34);
        width = Math.Max(width, commentWidth + 28);
        var body = node.IsCollapsed ? 0 : rowCount * RowHeight + (rowCount > 0 ? Padding : 0);
        if (!node.IsCollapsed && node.Kind == NodeKind.Comment)
            body = Math.Max(body, Math.Min(5, CommentLines(node)) * 18 + CommentPadding);
        return new NodeSize(width, HeaderHeight + body);
    }

    private static double RowWidth(BlueprintNodeRow row)
    {
        var label = TextWidth(row.Label, 12);
        var value = string.IsNullOrWhiteSpace(row.Value) ? 0 : Math.Max(48, TextWidth(row.Value, 11) + 16);
        var output = row.Output is null ? 0 : 18;
        return 28 + label + (value > 0 ? 8 + value : 0) + output;
    }

    private static double ContentWidth(BlueprintNode node)
    {
        if (node.Kind != NodeKind.Comment || string.IsNullOrWhiteSpace(node.Text)) return 0;
        return CommentText(node).Max(line => TextWidth(line, 13));
    }

    private static double TextWidth(string? text, double size, bool bold = false)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var width = 0.0;
        foreach (var character in text)
        {
            width += character switch
            {
                ' ' or '\t' => size * 0.34,
                _ when character >= 0x2E80 => size,
                _ => size * 0.56,
            };
        }
        return width * (bold ? 1.04 : 1.0);
    }

    private static int CommentLines(BlueprintNode node) => CommentText(node).Length;

    private static string[] CommentText(BlueprintNode node)
    {
        if (string.IsNullOrEmpty(node.Text)) return [];
        var lines = node.Text.Replace("\r\n", "\n").Split('\n')
            .Select(line => line.TrimStart()).ToList();
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[0])) lines.RemoveAt(0);
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1])) lines.RemoveAt(lines.Count - 1);
        return lines.ToArray();
    }
    public IReadOnlyList<NodeBounds> Layout(BlueprintDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var rootX = 0.0;
        foreach (var root in document.Roots)
        {
            var size = Measure(root);
            if (!root.HasPosition)
            {
                root.X = rootX;
                root.Y = 0;
            }
            LayoutChain(root, [], []);
            rootX = root.X + size.Width + RootGap;
        }
        PositionValueInputs(document);
        ResolveOverlaps(document);
        SettlePositions(document);
        return CollectBounds(document);
    }

    public BlueprintNode PlaceAfter(IList<BlueprintNode> siblings, int predecessorIndex, BlueprintNode node)
    {
        ArgumentNullException.ThrowIfNull(siblings);
        ArgumentNullException.ThrowIfNull(node);
        if (predecessorIndex < -1 || predecessorIndex >= siblings.Count)
            throw new ArgumentOutOfRangeException(nameof(predecessorIndex));

        var insertIndex = predecessorIndex + 1;
        var predecessor = predecessorIndex >= 0 ? siblings[predecessorIndex] : null;
        var predecessorSize = predecessor is null ? new NodeSize(0, 0) : Measure(predecessor);
        var newNodeSize = Measure(node);
        var x = predecessor is null ? 0 : predecessor.X + predecessorSize.Width + ChainGap;
        var y = predecessor?.Y ?? 0;

        if (predecessor is not null)
        {
            var shift = newNodeSize.Width + ChainGap;
            for (var index = insertIndex; index < siblings.Count; index++)
            {
                Shift(siblings[index], shift, 0);
            }
        }

        node.X = x;
        node.Y = y;
        node.HasPosition = true;
        siblings.Insert(insertIndex, node);
        return node;
    }

    public void AutoArrange(BlueprintDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        foreach (var node in Walk(document.Roots))
        {
            node.HasPosition = false;
            node.X = 0;
            node.Y = 0;
        }
        Layout(document);
    }

    private void PositionValueInputs(BlueprintDocument document)
    {
        foreach (var node in Walk(document.Roots))
        {
            if (node.ValueInputs.Count == 0) continue;
            var rows = BlueprintNodeRows.Build(node);
            foreach (var value in node.ValueInputs)
            {
                var index = rows.ToList().FindIndex(row => row.Input?.Id == value.ValueInputPortId);
                if (index < 0 || value.HasPosition) continue;
                var size = Measure(value);
                value.X = node.X - size.Width - 18;
                value.Y = node.Y + HeaderHeight + index * RowHeight + RowHeight / 2 - size.Height / 2;
                value.HasPosition = true;
            }
        }
    }

    private static void SettlePositions(BlueprintDocument document)
    {
        foreach (var node in Walk(document.Roots))
            node.HasPosition = true;
    }

    private IReadOnlyList<NodeBounds> CollectBounds(BlueprintDocument document)
    {
        var result = new List<NodeBounds>();
        foreach (var node in Walk(document.Roots))
        {
            if (node.Type == "control.branch") continue;
            var size = Measure(node);
            result.Add(new NodeBounds(node.Id, node.X, node.Y, size.Width, size.Height,
                Math.Max(1, BranchRowCount(node) + ValueRowCount(node))));
        }
        return result;
    }

    private void ResolveOverlaps(BlueprintDocument document)
    {
        var occupied = new List<NodeBounds>();
        foreach (var node in Walk(document.Roots))
        {
            if (node.Type == "control.branch") continue;
            var size = Measure(node);
            var rect = new NodeBounds(node.Id, node.X, node.Y, size.Width, size.Height, 1);
            if (!node.HasPosition)
            {
                var guard = 0;
                while (occupied.Any(other => Intersects(other, rect)) && guard++ < 200)
                {
                    var bottom = occupied.Where(other => Intersects(other, rect)).Max(other => other.Y + other.Height);
                    var dy = bottom + ChainGap - rect.Y;
                    ShiftSubtree(node, dy);
                    rect = new NodeBounds(node.Id, node.X, node.Y, size.Width, size.Height, 1);
                }
            }
            occupied.Add(rect);
        }
    }

    private static bool Intersects(NodeBounds a, NodeBounds b) =>
        a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;

    private static void ShiftSubtree(BlueprintNode node, double dy)
    {
        node.Y += dy;
        foreach (var child in node.AllChildren) ShiftSubtree(child, dy);
    }

    private static void LayoutChain(BlueprintNode first, ICollection<NodeBounds> bounds, HashSet<string> visited)
    {
        var node = first;
        while (node is not null)
        {
            if (!visited.Add(node.Id)) return;
            if (node.Type == "control.branch")
            {
                foreach (var child in node.Children)
                {
                    if (!child.HasPosition)
                    {
                        child.X = node.X;
                        child.Y = node.Y;
                    }
                    LayoutChain(child, bounds, visited);
                }
                return;
            }
            var engine = new BlueprintLayoutEngine();
            var size = engine.Measure(node);
            bounds.Add(new NodeBounds(node.Id, node.X, node.Y, size.Width, size.Height,
                Math.Max(1, engine.BranchRowCount(node) + engine.ValueRowCount(node))));

            var branchIndex = 0;
            foreach (var child in node.Children)
            {
                if (!child.HasPosition)
                {
                    child.X = node.X + size.Width + BranchGap;
                    child.Y = node.Y + HeaderHeight + branchIndex * RowHeight;
                }
                LayoutChain(child, bounds, visited);
                branchIndex++;
            }
            foreach (var child in node.ElseChildren)
            {
                if (!child.HasPosition)
                {
                    child.X = node.X + size.Width + BranchGap;
                    child.Y = node.Y + HeaderHeight + branchIndex * RowHeight;
                }
                LayoutChain(child, bounds, visited);
                branchIndex++;
            }

            var next = NextSibling(node);
            if (next is not null && !next.HasPosition)
            {
                next.X = node.X + size.Width + ChainGap;
                next.Y = node.Y;
            }
            node = next;
        }
    }

    private int BranchRowCount(BlueprintNode node) =>
        Math.Max(0, node.Ports.Count(port => port.Relation == NodeRelation.Exec && !port.IsInput));

    private int ValueRowCount(BlueprintNode node) =>
        node.Ports.Count(port => port.Kind == PortKind.Value && port.IsInput);

    private static BlueprintNode? NextSibling(BlueprintNode node)
    {
        var parent = node.Parent;
        if (parent is null) return null;
        var siblings = parent.Children.Contains(node) ? parent.Children : parent.ElseChildren;
        var index = siblings.IndexOf(node);
        return index >= 0 && index + 1 < siblings.Count ? siblings[index + 1] : null;
    }

    private static void Shift(BlueprintNode node, double dx, double dy)
    {
        node.X += dx;
        node.Y += dy;
        node.HasPosition = true;
        foreach (var child in node.AllChildren) Shift(child, dx, dy);
    }

    private static IEnumerable<BlueprintNode> Walk(IEnumerable<BlueprintNode> roots)
    {
        foreach (var root in roots)
        {
            yield return root;
            foreach (var descendant in Walk(root.AllChildren)) yield return descendant;
        }
    }
}
