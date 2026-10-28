using System.Collections.Generic;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using VisualizedAgentCoding.Application.Editor.ObjectScripts;

namespace happy_agent_coding.Services;

internal sealed class LockedEditorProtection : IReadOnlySectionProvider
{
    public static readonly LockedEditorProtection Instance = new();
    public bool CanInsert(int offset) => false;
    public IEnumerable<ISegment> GetDeletableSegments(ISegment segment) => System.Array.Empty<ISegment>();
}

/// <summary>Editor-only anchors; offsets are UTF-16 and never depend on reparsing in-progress code.</summary>
public sealed class UserRegionProtection : IReadOnlySectionProvider
{
    private readonly TextDocument document;
    private readonly TextAnchor start;
    private readonly TextAnchor end;

    public UserRegionProtection(TextDocument document, SourceSpan region)
    {
        this.document = document;
        start = document.CreateAnchor(region.Offset);
        start.MovementType = AnchorMovementType.BeforeInsertion;
        start.SurviveDeletion = true;
        end = document.CreateAnchor(region.Offset + region.Length);
        end.MovementType = AnchorMovementType.AfterInsertion;
        end.SurviveDeletion = true;
    }

    public SourceSpan Region => new(start.Offset, end.Offset - start.Offset);
    public bool CanInsert(int offset) => offset >= start.Offset && offset <= end.Offset;

    public bool TryRemoveFirstBlankLine(int caretOffset, out int newCaretOffset)
    {
        newCaretOffset = caretOffset;
        if (caretOffset != start.Offset || Region.Length == 0) return false;
        var line = document.GetLineByOffset(start.Offset);
        if (line.Offset != start.Offset || line.DelimiterLength == 0
            || line.TotalLength > Region.Length) return false;
        var text = document.GetText(line.Offset, line.Length);
        foreach (var character in text)
            if (character != ' ' && character != '\t') return false;
        var previous = line.PreviousLine;
        if (previous is null) return false;
        newCaretOffset = previous.EndOffset;
        document.Remove(line.Offset, line.TotalLength);
        return true;
    }

    /// <summary>Enter at the opening marker creates a user line without editing that marker.</summary>
    public bool TryInsertEntryLine(int caretOffset, out int newCaretOffset)
    {
        newCaretOffset = caretOffset;
        var firstLine = document.GetLineByOffset(start.Offset);
        var previous = firstLine.PreviousLine;
        if (previous is null) return false;
        var atOpeningEnd = caretOffset == previous.EndOffset;
        var atEmptyClosingIndent = Region.Length == 0 && caretOffset >= start.Offset
            && caretOffset <= start.Offset + 4;
        if (!atOpeningEnd && !atEmptyClosingIndent) return false;
        var newline = document.GetText(previous.EndOffset, previous.DelimiterLength);
        if (newline.Length == 0) return false;
        var insertionOffset = start.Offset;
        document.Insert(insertionOffset, "    " + newline);
        newCaretOffset = insertionOffset + 4;
        return true;
    }

    public IEnumerable<ISegment> GetDeletableSegments(ISegment segment)
    {
        // Reject an entire cross-boundary deletion rather than silently cutting part of a selection.
        if (segment.Offset >= start.Offset && segment.EndOffset <= end.Offset)
            yield return segment;
    }
}
