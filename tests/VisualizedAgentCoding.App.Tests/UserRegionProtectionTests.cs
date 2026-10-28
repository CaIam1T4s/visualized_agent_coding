using AvaloniaEdit.Document;
using happy_agent_coding.Services;
using VisualizedAgentCoding.Application.Editor.ObjectScripts;

namespace VisualizedAgentCoding.App.Tests;

public class UserRegionProtectionTests
{
    [Theory]
    [InlineData("\n", "")]
    [InlineData("\r\n", "    ")]
    [InlineData("\n", "\t")]
    public void BackspaceAtFirstBlankLineRemovesOnlyUserLineAndCanBeUndone(string newline, string whitespace)
    {
        var prefix = "    # vac:user-code-begin" + newline;
        var suffix = "    # vac:user-code-end" + newline;
        var body = whitespace + newline;
        var document = new TextDocument(prefix + body + suffix);
        var protection = new UserRegionProtection(document, new(prefix.Length, body.Length));
        Assert.True(protection.TryRemoveFirstBlankLine(prefix.Length, out var caret));
        Assert.Equal(prefix.Length - newline.Length, caret);
        Assert.Equal(prefix + suffix, document.Text);
        Assert.Equal(new SourceSpan(prefix.Length, 0), protection.Region);
        Assert.False(protection.TryRemoveFirstBlankLine(prefix.Length, out _));
        document.UndoStack.Undo();
        Assert.Equal(prefix + body + suffix, document.Text);
        Assert.Equal(new SourceSpan(prefix.Length, body.Length), protection.Region);
    }

    [Fact]
    public void BackspaceCannotMergeFirstCodeLineIntoSystemMarker()
    {
        var prefix = "    # vac:user-code-begin\n";
        var body = "    value = 1\n";
        var source = prefix + body + "    # vac:user-code-end\n";
        var document = new TextDocument(source);
        var protection = new UserRegionProtection(document, new(prefix.Length, body.Length));
        Assert.False(protection.TryRemoveFirstBlankLine(prefix.Length, out _));
        Assert.False(protection.TryRemoveFirstBlankLine(prefix.Length + 4, out _));
        Assert.Equal(source, document.Text);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void EnterAtOpeningMarkerCreatesEditableIndentedLine(string newline)
    {
        var prefix = "    # vac:user-code-begin" + newline;
        var suffix = "    # vac:user-code-end" + newline;
        var document = new TextDocument(prefix + suffix);
        var protection = new UserRegionProtection(document, new(prefix.Length, 0));
        Assert.False(protection.TryInsertEntryLine(5, out _));
        Assert.True(protection.TryInsertEntryLine(prefix.Length - newline.Length, out var caret));
        Assert.Equal(prefix + "    " + newline + suffix, document.Text);
        Assert.Equal(prefix.Length + 4, caret);
        Assert.True(protection.CanInsert(caret));
        document.Insert(caret, "value = 1");
        Assert.Equal(prefix + "    value = 1" + newline + suffix, document.Text);
        document.UndoStack.Undo();
        document.UndoStack.Undo();
        Assert.Equal(prefix + suffix, document.Text);
        Assert.Equal(new SourceSpan(prefix.Length, 0), protection.Region);
        Assert.True(protection.TryInsertEntryLine(prefix.Length + 4, out caret));
        Assert.Equal(prefix.Length + 4, caret);
    }

    [Fact]
    public void EmptyRegionExpandsAtBothBoundariesAndSurvivesUndoRedo()
    {
        var document = new TextDocument("prefixsuffix");
        var protection = new UserRegionProtection(document, new(6, 0));
        Assert.True(protection.CanInsert(6));
        document.Insert(6, "😀\r\n");
        Assert.Equal(new SourceSpan(6, 4), protection.Region);
        document.Insert(6, "start");
        document.Insert(protection.Region.Offset + protection.Region.Length, "end");
        Assert.Equal(new SourceSpan(6, 12), protection.Region);
        document.UndoStack.Undo();
        Assert.Equal(new SourceSpan(6, 9), protection.Region);
        document.UndoStack.Redo();
        Assert.Equal(new SourceSpan(6, 12), protection.Region);
        document.Remove(6, 12);
        Assert.Equal(new SourceSpan(6, 0), protection.Region);
        document.UndoStack.Undo();
        Assert.Equal(new SourceSpan(6, 12), protection.Region);
    }

    [Fact]
    public void SystemAndCrossBoundaryEditsAreRejectedButUserReplacementIsAllowed()
    {
        var document = new TextDocument("sysUSERend");
        var protection = new UserRegionProtection(document, new(3, 4));
        Assert.False(protection.CanInsert(2));
        Assert.False(protection.CanInsert(8));
        Assert.True(protection.CanInsert(3));
        Assert.True(protection.CanInsert(7));
        Assert.Empty(protection.GetDeletableSegments(new SimpleSegment(0, 10)));
        Assert.Empty(protection.GetDeletableSegments(new SimpleSegment(2, 2)));
        Assert.Empty(protection.GetDeletableSegments(new SimpleSegment(6, 2)));
        Assert.Single(protection.GetDeletableSegments(new SimpleSegment(3, 4)));
        document.Replace(3, 4, "new\ncode\n");
        Assert.Equal(new SourceSpan(3, 9), protection.Region);
        Assert.Equal("sysnew\ncode\nend", document.Text);
    }
}
