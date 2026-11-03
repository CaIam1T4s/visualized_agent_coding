using VisualizedAgentCoding.Blueprint.Core;
using VisualizedAgentCoding.Domain.Blueprint;

namespace VisualizedAgentCoding.App.Tests;

public sealed class BlueprintDocumentReconcilerTests
{
    [Fact]
    public void CopiesPositionAndCollapseForMatchedNodes()
    {
        var oldDoc = Doc(Statement("a", 100, 200, collapsed: true), Statement("b", 0, 0));
        var newDoc = Doc(Statement("a", 0, 0), Statement("b", 0, 0));

        BlueprintDocumentReconciler.Merge(oldDoc, newDoc);

        Assert.True(newDoc.Roots[0].HasPosition);
        Assert.Equal(100, newDoc.Roots[0].X);
        Assert.Equal(200, newDoc.Roots[0].Y);
        Assert.True(newDoc.Roots[0].IsCollapsed);
        Assert.False(newDoc.Roots[1].HasPosition);
    }

    [Fact]
    public void NewNodeDoesNotBorrowOldPosition()
    {
        var oldDoc = Doc(Statement("a", 100, 200));
        var newDoc = Doc(Statement("a", 0, 0), Statement("new", 0, 0));

        BlueprintDocumentReconciler.Merge(oldDoc, newDoc);

        Assert.Equal(100, newDoc.Roots[0].X);
        Assert.False(newDoc.Roots[1].HasPosition);
    }

    [Fact]
    public void MergesNestedChildrenAndKeepsDuplicateOrder()
    {
        var oldParent = Statement("if", 10, 10);
        oldParent.Children.Add(Statement("move", 30, 30));
        var newParent = Statement("if", 0, 0);
        newParent.Children.Add(Statement("move", 0, 0));
        var oldDoc = Doc(oldParent);
        var newDoc = Doc(newParent);

        BlueprintDocumentReconciler.Merge(oldDoc, newDoc);

        Assert.Equal(30, newDoc.Roots[0].Children[0].X);
    }

    private static BlueprintNode Statement(string name, double x, double y, bool collapsed = false) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Type = "motion.move",
        Kind = NodeKind.Statement,
        DisplayName = name,
        X = x,
        Y = y,
        HasPosition = x != 0 || y != 0,
        IsCollapsed = collapsed,
    };

    private static BlueprintDocument Doc(params BlueprintNode[] roots)
    {
        var document = new BlueprintDocument { ScriptId = "s", RegionId = "r" };
        document.Roots.AddRange(roots);
        return document;
    }
}
