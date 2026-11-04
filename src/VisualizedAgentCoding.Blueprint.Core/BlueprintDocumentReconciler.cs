using System;
using System.Collections.Generic;
using System.Linq;
using VisualizedAgentCoding.Domain.Blueprint;

namespace VisualizedAgentCoding.Blueprint.Core;

/// <summary>
/// Carries view-only layout from an old projection to a new one. Semantic order,
/// node existence and source ranges always come from the new Python projection.
/// </summary>
public static class BlueprintDocumentReconciler
{
    public static void Merge(BlueprintDocument? previous, BlueprintDocument next)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (previous is null) return;

        var oldNodes = Walk(previous.Roots).ToList();
        var newNodes = Walk(next.Roots).ToList();
        var oldToNew = new Dictionary<BlueprintNode, BlueprintNode>();
        var usedOld = new HashSet<BlueprintNode>();
        var usedNew = new HashSet<BlueprintNode>();

        foreach (var (oldIndex, newIndex) in LongestCommonSubsequence(oldNodes, newNodes))
        {
            var oldNode = oldNodes[oldIndex];
            var newNode = newNodes[newIndex];
            CopyViewState(oldNode, newNode);
            oldToNew[oldNode] = newNode;
            usedOld.Add(oldNode);
            usedNew.Add(newNode);
        }

        var oldByKey = ByStableKey(oldNodes);
        foreach (var newNode in newNodes)
        {
            if (usedNew.Contains(newNode)) continue;
            var key = StableKey(newNode, OccurrenceOf(newNode, newNodes));
            if (!oldByKey.TryGetValue(key, out var oldNode) || usedOld.Contains(oldNode)) continue;
            CopyViewState(oldNode, newNode);
            oldToNew[oldNode] = newNode;
            usedOld.Add(oldNode);
        }

        var oldByLine = oldNodes
            .Where(node => node.Source.StartLine > 0)
            .GroupBy(LineKey)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single());
        foreach (var newNode in newNodes)
        {
            if (usedNew.Contains(newNode) || newNode.Source.StartLine <= 0) continue;
            if (!oldByLine.TryGetValue(LineKey(newNode), out var oldNode) || usedOld.Contains(oldNode)) continue;
            CopyViewState(oldNode, newNode);
            oldToNew[oldNode] = newNode;
            usedOld.Add(oldNode);
        }

        foreach (var (oldNode, newNode) in oldToNew)
        {
            if (oldNode.Kind != NodeKind.Comment || oldNode.AnnotationTarget is null) continue;
            if (oldToNew.TryGetValue(oldNode.AnnotationTarget, out var target))
                newNode.AnnotationTarget = target;
        }
    }

    private static void CopyViewState(BlueprintNode oldNode, BlueprintNode newNode)
    {
        if (oldNode.HasPosition)
        {
            newNode.X = oldNode.X;
            newNode.Y = oldNode.Y;
            newNode.HasPosition = true;
        }
        if (oldNode.IsCollapsedExplicit || (oldNode.IsCollapsed && newNode.Kind != NodeKind.Comment))
        {
            newNode.IsCollapsed = oldNode.IsCollapsed;
            newNode.IsCollapsedExplicit = true;
        }
    }

    private static Dictionary<string, BlueprintNode> ByStableKey(IReadOnlyList<BlueprintNode> nodes)
    {
        var result = new Dictionary<string, BlueprintNode>(StringComparer.Ordinal);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            var occurrence = counts.GetValueOrDefault(Key(node));
            counts[Key(node)] = occurrence + 1;
            result[StableKey(node, occurrence)] = node;
        }
        return result;
    }

    private static int OccurrenceOf(BlueprintNode node, IReadOnlyList<BlueprintNode> nodes)
    {
        var count = 0;
        foreach (var candidate in nodes)
        {
            if (ReferenceEquals(candidate, node)) return count;
            if (Key(candidate) == Key(node)) count++;
        }
        return count;
    }

    private static string StableKey(BlueprintNode node, int occurrence) =>
        Key(node) + "#" + occurrence.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static List<(int OldIndex, int NewIndex)> LongestCommonSubsequence(
        IReadOnlyList<BlueprintNode> oldNodes, IReadOnlyList<BlueprintNode> newNodes)
    {
        var oldCount = oldNodes.Count;
        var newCount = newNodes.Count;
        var lengths = new int[oldCount + 1, newCount + 1];
        for (var i = 1; i <= oldCount; i++)
        {
            for (var j = 1; j <= newCount; j++)
            {
                lengths[i, j] = Key(oldNodes[i - 1]) == Key(newNodes[j - 1])
                    ? lengths[i - 1, j - 1] + 1
                    : Math.Max(lengths[i - 1, j], lengths[i, j - 1]);
            }
        }

        var result = new List<(int, int)>();
        var oldIndex = oldCount;
        var newIndex = newCount;
        while (oldIndex > 0 && newIndex > 0)
        {
            if (Key(oldNodes[oldIndex - 1]) == Key(newNodes[newIndex - 1]))
            {
                result.Add((oldIndex - 1, newIndex - 1));
                oldIndex--;
                newIndex--;
            }
            else if (lengths[oldIndex - 1, newIndex] >= lengths[oldIndex, newIndex - 1])
            {
                oldIndex--;
            }
            else
            {
                newIndex--;
            }
        }
        result.Reverse();
        return result;
    }

    private static string Key(BlueprintNode node) => BlueprintLayoutMapper.Fingerprint(node);

    private static string LineKey(BlueprintNode node) =>
        node.Type + "|" + node.DisplayName + "|" + node.Source.StartLine.ToString(
            System.Globalization.CultureInfo.InvariantCulture);

    private static IEnumerable<BlueprintNode> Walk(IEnumerable<BlueprintNode> roots)
    {
        foreach (var root in roots)
        {
            yield return root;
            foreach (var child in Walk(root.AllChildren)) yield return child;
        }
    }
}
