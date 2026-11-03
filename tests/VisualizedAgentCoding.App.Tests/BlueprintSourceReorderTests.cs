using VisualizedAgentCoding.Blueprint.Core;
using VisualizedAgentCoding.Domain.Blueprint;

namespace VisualizedAgentCoding.App.Tests;

public sealed class BlueprintSourceReorderTests
{
    [Fact]
    public void MovesStatementWithLeadingCommentAfterTarget()
    {
        var body = "# keep\nself.move(1)\nself.turn(2)\n";
        var move = new SourceSpan(body.IndexOf("self.move", StringComparison.Ordinal), "self.move(1)".Length, 2, 2);
        var target = new SourceSpan(body.IndexOf("self.turn", StringComparison.Ordinal), "self.turn(2)".Length, 3, 3);

        var updated = BlueprintSourceReorder.MoveAfter(body, move, target);

        Assert.Equal("self.turn(2)\n# keep\nself.move(1)\n", updated);
    }

    [Fact]
    public void ReindentsWhenMovingIntoNestedBlock()
    {
        var body = "while ready:\n    self.move(1)\nself.turn(2)\n";
        var move = new SourceSpan(body.IndexOf("self.turn", StringComparison.Ordinal), "self.turn(2)".Length, 3, 3);
        var target = new SourceSpan(body.IndexOf("self.move", StringComparison.Ordinal), "self.move(1)".Length, 2, 2);

        var updated = BlueprintSourceReorder.MoveAfter(body, move, target);

        Assert.Contains("    self.turn(2)", updated);
    }

    [Fact]
    public void MovingIntoItselfIsRejected()
    {
        var body = "self.move(1)\n";
        var span = new SourceSpan(0, "self.move(1)".Length, 1, 1);
        Assert.Equal(body, BlueprintSourceReorder.MoveAfter(body, span, span));
    }
}
