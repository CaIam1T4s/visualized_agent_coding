using System;
using AvaloniaEdit.Document;
using VisualizedAgentCoding.Application.Python;

namespace happy_agent_coding.Services;

public static class DiagnosticSelection
{
    /// <summary>Choose the token at the diagnostic, keeping selection within its visible line.</summary>
    public static SimpleSegment Locate(TextDocument document, PythonDiagnostic diagnostic)
    {
        var line = document.GetLineByNumber(Math.Clamp(diagnostic.Line, 1, document.LineCount));
        var text = document.GetText(line.Offset, line.Length);
        if (text.Length == 0) return new(line.Offset, 0);
        var index = Math.Clamp(diagnostic.Column - 1, 0, text.Length - 1);
        while (index < text.Length - 1 && char.IsWhiteSpace(text[index])) index++;
        var first = index;
        var end = index + 1;
        if (char.IsLetterOrDigit(text[index]) || text[index] == '_')
        {
            while (first > 0 && (char.IsLetterOrDigit(text[first - 1]) || text[first - 1] == '_')) first--;
            while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_')) end++;
        }
        // Do not split a UTF-16 surrogate pair when selecting a non-identifier symbol.
        if (first > 0 && char.IsLowSurrogate(text[first]) && char.IsHighSurrogate(text[first - 1])) first--;
        if (end < text.Length && char.IsHighSurrogate(text[end - 1]) && char.IsLowSurrogate(text[end])) end++;
        return new(line.Offset + first, end - first);
    }
}
