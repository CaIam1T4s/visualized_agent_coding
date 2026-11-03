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
        MergeLists(previous.Roots, next.Roots);
    }

    private static void MergeLists(IReadOnlyList<BlueprintNode> oldNodes, IReadOnlyList<BlueprintNode> newNodes)
    {
        var matches = LongestCommonSubsequence(oldNodes, newNodes);
        foreach (var (oldIndex, newIndex) in matches)
            MergeNode(oldNodes[oldIndex], newNodes[newIndex]);
    }

    private static void MergeNode(BlueprintNode oldNode, BlueprintNode newNode)
    {
        if (oldNode.HasPosition)
        {
            newNode.X = oldNode.X;
            newNode.Y = oldNode.Y;
            newNode.HasPosition = true;
        }
        newNode.IsCollapsed = oldNode.IsCollapsed;
        MergeLists(oldNode.Children, newNode.Children);
        MergeLists(oldNode.ElseChildren, newNode.ElseChildren);
    }

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

    private static string Key(BlueprintNode node) =>
        node.Type + "|" + node.DisplayName + "|" + (node.Text ?? "");
}
