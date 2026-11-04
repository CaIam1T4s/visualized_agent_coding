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
    public void ReorderedNodesKeepTheirPositions()
    {
        var oldDoc = Doc(Statement("a", 100, 20), Statement("b", 300, 40));
        var newDoc = Doc(Statement("b", 0, 0), Statement("a", 0, 0));

        BlueprintDocumentReconciler.Merge(oldDoc, newDoc);

        Assert.Equal(300, newDoc.Roots[0].X);
        Assert.Equal(40, newDoc.Roots[0].Y);
        Assert.Equal(100, newDoc.Roots[1].X);
        Assert.Equal(20, newDoc.Roots[1].Y);
    }

    [Fact]
    public void CommentKeepsBindingToReorderedTarget()
    {
        var oldComment = Statement("comment", 0, 0);
        oldComment.Kind = NodeKind.Comment;
        oldComment.Text = "note";
        var oldTarget = Statement("target", 100, 20);
        oldComment.AnnotationTarget = oldTarget;
        var oldDoc = Doc(oldComment, oldTarget);

        var newTarget = Statement("target", 0, 0);
        var newComment = Statement("comment", 0, 0);
        newComment.Kind = NodeKind.Comment;
        newComment.Text = "note";
        newComment.AnnotationTarget = newTarget;
        var newDoc = Doc(newTarget, newComment);

        BlueprintDocumentReconciler.Merge(oldDoc, newDoc);

        Assert.Same(newTarget, newComment.AnnotationTarget);
    }


    [Fact]
    public void EditedValueKeepsPositionOnSameSourceLine()
    {
        var oldNode = Statement("移动", 100, 20);
        oldNode.Source = new SourceSpan(0, 12, 3, 3);
        oldNode.Arguments["steps"] = "10";
        var newNode = Statement("移动", 0, 0);
        newNode.Source = new SourceSpan(0, 12, 3, 3);
        newNode.Arguments["steps"] = "20";

        BlueprintDocumentReconciler.Merge(Doc(oldNode), Doc(newNode));

        Assert.Equal(100, newNode.X);
        Assert.Equal(20, newNode.Y);
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
