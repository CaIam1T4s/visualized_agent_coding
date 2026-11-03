using VisualizedAgentCoding.Blueprint.Core;
using VisualizedAgentCoding.Domain.Blueprint;

namespace VisualizedAgentCoding.App.Tests;

public sealed class BlueprintReconnectValidatorTests
{
    [Fact]
    public void RejectsSelfAndDifferentParents()
    {
        var parent = Node("control.if", null);
        var a = Node("motion.move", parent);
        var b = Node("motion.turn", parent);
        var other = Node("motion.turn", null);
        parent.Children.Add(a);
        parent.Children.Add(b);

        Assert.False(BlueprintReconnectValidator.Validate(a, a).IsValid);
        Assert.False(BlueprintReconnectValidator.Validate(a, other).IsValid);
        Assert.True(BlueprintReconnectValidator.Validate(a, b).IsValid);
    }

    [Fact]
    public void RejectsTerminalSourceAndBreakOutsideLoop()
    {
        var loop = Node("control.while", null);
        var inside = Node("control.break", loop);
        var outside = Node("motion.move", null);
        loop.Children.Add(inside);

        Assert.False(BlueprintReconnectValidator.Validate(inside, outside).IsValid);
        var ret = Node("control.return", null);
        Assert.False(BlueprintReconnectValidator.Validate(ret, outside).IsValid);
    }

    private static BlueprintNode Node(string type, BlueprintNode? parent)
    {
        var node = new BlueprintNode
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = type,
            Parent = parent,
            Ports =
            {
                new("exec-in", PortKind.Exec, NodeRelation.Next, IsInput: true),
                new("exec-out", PortKind.Exec, NodeRelation.Next, IsInput: false),
            },
        };
        return node;
    }
}
