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

        Assert.False(node.HasPosition);
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
