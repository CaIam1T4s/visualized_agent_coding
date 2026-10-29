using AvaloniaEdit.Document;
using VisualizedAgentCoding.Application.Editor.ObjectScripts;

namespace VisualizedAgentCoding.App.Tests;

public class PythonIndentationTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void ConvertsFourColumnTabStopsAndPreservesLiteralContents(string nl)
    {
        var source = "if True:" + nl + " \tvalue = '\t' # \tcomment" + nl
            + "\ttext = r'''first" + nl + "\t literal" + nl + "'''" + nl
            + "\tother = f'''{42}" + nl + "\tliteral'''" + nl
            + "\tlast = 1" + nl;
        var buffer = new TextDocument(source);
        using (buffer.RunUpdate())
            foreach (var edit in PythonWrapperIndentation.FindIndentationTabs(source).Reverse())
                buffer.Replace(edit.Offset, 1, edit.Replacement);
        Assert.Equal(source.Replace(" \tvalue", "    value").Replace("\ttext", "    text")
            .Replace("\tother", "    other").Replace("\tlast", "    last"), buffer.Text);
        buffer.UndoStack.Undo();
        Assert.Equal(source, buffer.Text);
        buffer.UndoStack.Redo();
        Assert.Empty(PythonWrapperIndentation.FindIndentationTabs(buffer.Text));
    }

    [Fact]
    public void ExistingObjectTabsAccountForHiddenWrapperColumns()
    {
        var edit = Assert.Single(PythonWrapperIndentation.FindIndentationTabs("\tvalue = 1\n", 4));
        Assert.Equal("    ", edit.Replacement);
        Assert.Equal("    ", Assert.Single(PythonWrapperIndentation.FindIndentationTabs("\tvalue = 1\n", 8)).Replacement);
    }

    [Fact]
    public void UnterminatedMultilineLiteralKeepsAllContinuationTabs()
    {
        Assert.Empty(PythonWrapperIndentation.FindIndentationTabs("s = '''\n\ttext\n\tmore"));
        Assert.Empty(PythonWrapperIndentation.FindIndentationTabs("s = 'abc\\\n\tcontinued'\n"));
        var edit = Assert.Single(PythonWrapperIndentation.FindIndentationTabs("  \t# comment"));
        Assert.Equal(2, edit.Offset);
        Assert.Equal("  ", edit.Replacement);
    }

    [Fact]
    public void NestedTabsEachAddFourColumnsAndExistingSpacesAreUnchanged()
    {
        var edits = PythonWrapperIndentation.FindIndentationTabs("\t\tvalue = 1\n").ToArray();
        Assert.Equal(2, edits.Length);
        Assert.All(edits, edit => Assert.Equal("    ", edit.Replacement));
        Assert.Empty(PythonWrapperIndentation.FindIndentationTabs("        value = 1\n"));
    }
}
