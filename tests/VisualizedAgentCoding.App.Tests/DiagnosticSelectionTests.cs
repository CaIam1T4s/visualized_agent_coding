using AvaloniaEdit.Document;
using happy_agent_coding.Services;
using VisualizedAgentCoding.Application.Python;

namespace VisualizedAgentCoding.App.Tests;

public class DiagnosticSelectionTests
{
    [Theory]
    [InlineData("self.walk(", 10, "(")]
    [InlineData("value = other", 11, "other")]
    [InlineData("变量 = 1", 1, "变量")]
    [InlineData("😀", 1, "😀")]
    [InlineData("", 1, "")]
    public void DiagnosticSelectsTokenOrSymbolWithoutSplittingUnicode(string text, int column, string expected)
    {
        var document = new TextDocument(text);
        var segment = DiagnosticSelection.Locate(document, new PythonDiagnostic("error", "example", 1, column));
        Assert.Equal(expected, document.GetText(segment));
    }
}
