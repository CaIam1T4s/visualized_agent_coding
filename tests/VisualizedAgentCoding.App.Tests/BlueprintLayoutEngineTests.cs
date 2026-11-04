using VisualizedAgentCoding.Blueprint.Core;
using VisualizedAgentCoding.Domain.Blueprint;

namespace VisualizedAgentCoding.App.Tests;

public sealed class BlueprintLayoutEngineTests
{
    [Fact]
    public void LayoutPlacesChainToTheRightAndBranchesRightDown()
    {
        var first = Statement("a");
        var second = Statement("b");
        first.Parent = null;
        first.Children.Add(second);
        second.Parent = first;
        var branch = Statement("branch");
        first.ElseChildren.Add(branch);
        branch.Parent = first;
        first.Ports.Add(new("branch-0", PortKind.Exec, NodeRelation.Exec, IsInput: false));
        var document = new BlueprintDocument { ScriptId = "s", RegionId = "r" };
        document.Roots.Add(first);

        new BlueprintLayoutEngine().Layout(document);

        Assert.True(second.X > first.X);
        Assert.True(branch.X > first.X);
        Assert.True(branch.Y > first.Y);
    }

    [Fact]
    public void CommentHeightMatchesVisibleLinesAndInlineCommentWidensNode()
    {
        var comment = new BlueprintNode
        {
            Id = "c",
            Type = "comment",
            Kind = NodeKind.Comment,
            DisplayName = "注释",
            Text = "one\ntwo",
        };
        var measure = new BlueprintLayoutEngine().Measure(comment);
        Assert.Equal(BlueprintLayoutEngine.HeaderHeight + 2 * 18 + 6, measure.Height);
        comment.Text = "\none\ntwo\n";
        Assert.Equal(measure.Height, new BlueprintLayoutEngine().Measure(comment).Height);

        var node = Statement("移动");
        var plain = new BlueprintLayoutEngine().Measure(node);
        node.InlineComment = string.Concat(Enumerable.Repeat("long inline comment ", 8));
        var widened = new BlueprintLayoutEngine().Measure(node);
        Assert.True(widened.Width > plain.Width);
    }


    [Fact]
    public void BranchChildPositionStaysStableAcrossRepeatedLayout()
    {
        var ifNode = new BlueprintNode
        {
            Id = "if",
            Type = "control.if",
            Kind = NodeKind.ControlFlow,
            DisplayName = "如果",
            HasPosition = true,
        };
        var branch = new BlueprintNode
        {
            Id = "branch",
            Type = "control.branch",
            Kind = NodeKind.ControlFlow,
            DisplayName = "条件",
            Parent = ifNode,
        };
        var body = Statement("body");
        body.Parent = branch;
        branch.Children.Add(body);
        ifNode.Children.Add(branch);
        var document = new BlueprintDocument { ScriptId = "s", RegionId = "r" };
        document.Roots.Add(ifNode);

        var engine = new BlueprintLayoutEngine();
        engine.Layout(document);
        var firstY = body.Y;
        engine.Layout(document);

        Assert.Equal(firstY, body.Y);
    }

    [Fact]
    public void PlaceAfterShiftsFollowingSiblings()
    {
        var a = Statement("a");
        var b = Statement("b");
        a.HasPosition = true;
        b.HasPosition = true;
        b.X = 260;
        var list = new List<BlueprintNode> { a, b };
        var engine = new BlueprintLayoutEngine();

        var inserted = Statement("new");
        engine.PlaceAfter(list, 0, inserted);

        Assert.Equal(3, list.Count);
        Assert.True(inserted.X > a.X);
        Assert.True(b.X > inserted.X);
    }

    [Fact]
    public void ManualPositionIsPreserved()
    {
        var node = Statement("a");
        node.X = 400;
        node.Y = 200;
        node.HasPosition = true;
        var document = new BlueprintDocument { ScriptId = "s", RegionId = "r" };
        document.Roots.Add(node);

        new BlueprintLayoutEngine().Layout(document);

        Assert.Equal(400, node.X);
        Assert.Equal(200, node.Y);
    }

    [Fact]
    public void AutoArrangeResetsManualPositions()
    {
        var node = Statement("a");
        node.HasPosition = true;
        node.X = 100;
        node.Y = 100;
        var document = new BlueprintDocument { ScriptId = "s", RegionId = "r" };
        document.Roots.Add(node);

        new BlueprintLayoutEngine().AutoArrange(document);

        Assert.True(node.HasPosition);
        Assert.Equal(0, node.X);
        Assert.Equal(0, node.Y);
    }

    private static BlueprintNode Statement(string name) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        Type = "motion.move",
        Kind = NodeKind.Statement,
        DisplayName = name,
        Ports =
        {
            new("exec-in", PortKind.Exec, NodeRelation.Next, IsInput: true),
            new("exec-out", PortKind.Exec, NodeRelation.Next, IsInput: false),
        },
    };
}

public sealed class BlueprintLayoutMatchTests
{
    [Fact]
    public void MatchCasesDoNotRecurseForever()
    {
        var match = new BlueprintNode
        {
            Id = "match",
            Type = "control.match",
            Kind = NodeKind.ControlFlow,
            DisplayName = "匹配",
            Ports =
            {
                new("exec-in", PortKind.Exec, NodeRelation.Next, IsInput: true),
                new("exec-out", PortKind.Exec, NodeRelation.Next, IsInput: false),
                new("branch-0", PortKind.Exec, NodeRelation.Exec, IsInput: false),
            },
        };
        var caseNode = new BlueprintNode
        {
            Id = "case",
            Type = "control.case",
            Kind = NodeKind.ControlFlow,
            DisplayName = "case",
            Parent = match,
            Ports =
            {
                new("exec-in", PortKind.Exec, NodeRelation.Next, IsInput: true),
                new("exec-out", PortKind.Exec, NodeRelation.Next, IsInput: false),
            },
        };
        match.Children.Add(caseNode);
        var document = new BlueprintDocument { ScriptId = "s", RegionId = "r" };
        document.Roots.Add(match);

        var bounds = new BlueprintLayoutEngine().Layout(document);

        Assert.Contains(bounds, item => item.Id == "match");
        Assert.Contains(bounds, item => item.Id == "case");
    }
}
