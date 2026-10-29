using System.Collections.Generic;
using System.Linq;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using VisualizedAgentCoding.Application.Editor.ObjectScripts;

namespace happy_agent_coding.Services;

/// <summary>Surviving anchors keep region boundaries through native undo/redo and text edits.</summary>
public sealed class MultiRegionReadOnlyProvider : IReadOnlySectionProvider
{
    private sealed record AnchoredRegion(string Id, TextAnchor Start, TextAnchor End);
    private readonly TextDocument document;
    private readonly AnchoredRegion[] regions;
    public MultiRegionReadOnlyProvider(TextDocument document, IEnumerable<ProjectedRegion> spans)
    {
        this.document = document;
        regions = spans.Select(region =>
        {
            var start = document.CreateAnchor(region.Span.Offset);
            start.MovementType = AnchorMovementType.BeforeInsertion;
            start.SurviveDeletion = true;
            var end = document.CreateAnchor(region.Span.Offset + region.Span.Length);
            end.MovementType = AnchorMovementType.AfterInsertion;
            end.SurviveDeletion = true;
            return new AnchoredRegion(region.Id, start, end);
        }).ToArray();
    }
    public bool CanInsert(int offset) => regions.Any(r => offset >= r.Start.Offset && offset <= r.End.Offset);
    public IEnumerable<ISegment> GetDeletableSegments(ISegment segment)
    {
        if (regions.Any(r => segment.Offset >= r.Start.Offset && segment.EndOffset <= r.End.Offset)) yield return segment;
    }
    public Dictionary<string, string> ReadBodies() => regions.ToDictionary(r => r.Id,
        r => document.GetText(r.Start.Offset, r.End.Offset - r.Start.Offset));

    public bool TryEnterFromTitle(int caretOffset, out int caret)
    {
        caret = caretOffset;
        foreach (var region in regions)
        {
            var header = document.GetLineByOffset(region.Start.Offset).PreviousLine;
            if (header is null || header.EndOffset != caretOffset) continue;
            caret = region.Start.Offset;
            document.Insert(caret, document.GetText(header.EndOffset, header.DelimiterLength));
            return true;
        }
        return false;
    }

    public bool TryBackspaceFirstBlankLine(int caretOffset, out int caret)
    {
        caret = caretOffset;
        foreach (var region in regions)
        {
            if (caretOffset != region.Start.Offset) continue;
            var line = document.GetLineByOffset(caretOffset);
            if (line.PreviousLine is not { } header || line.TotalLength == 0
                || line.EndOffset + line.DelimiterLength > region.End.Offset
                || document.GetText(line.Offset, line.Length).Any(c => c is not (' ' or '\t'))) continue;
            caret = header.EndOffset;
            document.Remove(line.Offset, line.TotalLength);
            return true;
        }
        return false;
    }
}
