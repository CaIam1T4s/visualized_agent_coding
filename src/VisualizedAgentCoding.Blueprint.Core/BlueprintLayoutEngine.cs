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
    public const double HeaderHeight = 48;
    public const double RowHeight = 32;
    public const double Padding = 12;
    public const double ChainGap = 24;
    public const double BranchGap = 28;
    public const double RootGap = 28;

    public NodeSize Measure(BlueprintNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var rows = Math.Max(1, BranchRowCount(node) + ValueRowCount(node));
        var width = Math.Max(MinWidth, 150 + node.DisplayName.Length * 7.0);
        var body = node.IsCollapsed ? 0 : rows * RowHeight + Padding;
        return new NodeSize(width, HeaderHeight + body);
    }

    public IReadOnlyList<NodeBounds> Layout(BlueprintDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var bounds = new List<NodeBounds>();
        var rootY = 0.0;
        foreach (var root in document.Roots)
        {
            var size = Measure(root);
            if (!root.HasPosition)
            {
                root.X = 0;
                root.Y = rootY;
            }
            LayoutChain(root, bounds);
            rootY = Math.Max(rootY, root.Y + size.Height) + RootGap;
        }
        return bounds;
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

    private static void LayoutChain(BlueprintNode first, ICollection<NodeBounds> bounds)
    {
        var node = first;
        while (node is not null)
        {
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
                LayoutChain(child, bounds);
                branchIndex++;
            }
            foreach (var child in node.ElseChildren)
            {
                if (!child.HasPosition)
                {
                    child.X = node.X + size.Width + BranchGap;
                    child.Y = node.Y + HeaderHeight + branchIndex * RowHeight;
                }
                LayoutChain(child, bounds);
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
