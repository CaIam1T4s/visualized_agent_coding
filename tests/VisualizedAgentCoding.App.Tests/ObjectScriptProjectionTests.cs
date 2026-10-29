using VisualizedAgentCoding.Application.Editor.ObjectScripts;

namespace VisualizedAgentCoding.App.Tests;

public class ObjectScriptProjectionTests
{
    private static ObjectScriptDocument Document(string user, string newline)
    {
        var prefix = "# 系统😀" + newline + "    # begin" + newline;
        var suffix = "    # end" + newline;
        return new("hero", "file:///hero.py", 1, prefix + user + suffix,
            ObjectScriptStatus.Recognized, 1, new(prefix.Length, user.Length), []);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void ExactSlicePreservesIndentationStringsAndUtf16Positions(string newline)
    {
        var user = "    text = \"\"\"你好😀" + newline + " unindented string content" + newline + "\"\"\"" + newline;
        var document = Document(user, newline);
        var projection = new ObjectScriptProjection(document);
        Assert.Equal(user, projection.ViewSource);
        Assert.Equal(document.FullSource, projection.FullSource);
        Assert.Equal(document.UserRegion!.Offset + user.IndexOf("😀", StringComparison.Ordinal),
            projection.ToFullOffset(user.IndexOf("😀", StringComparison.Ordinal)));
        Assert.Equal(0, projection.ToViewOffset(document.UserRegion.Offset));
        Assert.Null(projection.ToViewOffset(0));
        Assert.Equal((4, 5), projection.ToFullPosition(2, 5));
        Assert.True(projection.TryToViewPosition(4, 5, out var line, out var column));
        Assert.Equal((2, 5), (line, column));
        Assert.False(projection.TryToViewPosition(2, 1, out _, out _));
        Assert.False(projection.TryToViewPosition(6, 5, out _, out _));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void EditingEmptyViewKeepsWrapperAndSeparatesEndMarker(string newline)
    {
        var document = Document("", newline);
        var projection = new ObjectScriptProjection(document);
        Assert.Equal("", projection.ViewSource);
        projection.Update("    value = 1");
        Assert.Equal("    value = 1", projection.ViewSource);
        Assert.Equal(document.FullSource.Insert(document.UserRegion!.Offset, "    value = 1" + newline), projection.FullSource);
        projection.Update("");
        Assert.Equal(document.FullSource, projection.FullSource);
    }
}
