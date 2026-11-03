using System;
using System.Collections.Generic;
using System.Linq;
using VisualizedAgentCoding.Domain.Blueprint;

namespace VisualizedAgentCoding.Blueprint.Core;

/// <summary>
/// Moves a statement range inside one region body. Comments on the moved lines and
/// consecutive leading standalone comments travel with the statement. The operation
/// returns source text; it is the caller's job to apply it through the region projection.
/// </summary>
public static class BlueprintSourceReorder
{
    public static string MoveAfter(string body, SourceSpan moving, SourceSpan target)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (moving.Length <= 0 || target.Length <= 0) return body;
        if (moving.Start <= target.Start && target.End <= moving.End) return body;
        if (target.Start <= moving.Start && moving.Start < target.End) return body;

        var moveLine = ExpandLines(body, moving, includeLeadingComments: true);
        var targetLine = ExpandLines(body, target, includeLeadingComments: false);
        if (moveLine.Start < 0 || targetLine.Start < 0) return body;

        var movedText = body.Substring(moveLine.Start, moveLine.Length);
        var targetIndent = IndentAt(body, targetLine.Start);
        var reindented = Reindent(movedText, targetIndent);
        var without = body.Remove(moveLine.Start, moveLine.Length);
        var targetEnd = targetLine.Start + targetLine.Length;
        if (moveLine.Start < targetLine.Start)
            targetEnd -= moveLine.Length;
        targetEnd = Math.Clamp(targetEnd, 0, without.Length);
        if (targetEnd > 0 && without[targetEnd - 1] is not ('\n' or '\r'))
            reindented = Newline(without) + reindented;
        return without.Insert(targetEnd, reindented);
    }

    public static string MoveBefore(string body, SourceSpan moving, SourceSpan target)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (moving.Length <= 0 || target.Length <= 0) return body;
        if (moving.Start <= target.Start && target.End <= moving.End) return body;
        if (target.Start <= moving.Start && moving.Start < target.End) return body;

        var moveLine = ExpandLines(body, moving, includeLeadingComments: true);
        var targetLine = ExpandLines(body, target, includeLeadingComments: false);
        if (moveLine.Start < 0 || targetLine.Start < 0) return body;

        var movedText = body.Substring(moveLine.Start, moveLine.Length);
        var targetIndent = IndentAt(body, targetLine.Start);
        var reindented = Reindent(movedText, targetIndent);
        var without = body.Remove(moveLine.Start, moveLine.Length);
        var insertAt = targetLine.Start;
        if (moveLine.Start < targetLine.Start) insertAt -= moveLine.Length;
        insertAt = Math.Clamp(insertAt, 0, without.Length);
        return without.Insert(insertAt, reindented);
    }

    private static (int Start, int Length) ExpandLines(string body, SourceSpan span, bool includeLeadingComments)
    {
        var start = Math.Clamp(span.Start, 0, body.Length);
        var end = Math.Clamp(span.Start + span.Length, start, body.Length);
        while (start > 0 && body[start - 1] is not ('\n' or '\r')) start--;
        while (end < body.Length && body[end] is not ('\n' or '\r')) end++;
        if (end < body.Length && body[end] == '\r') end++;
        if (end < body.Length && body[end] == '\n') end++;

        if (includeLeadingComments)
        {
            while (start > 0)
            {
                var previousEnd = start;
                if (previousEnd > 0 && body[previousEnd - 1] == '\n') previousEnd--;
                if (previousEnd > 0 && body[previousEnd - 1] == '\r') previousEnd--;
                var previousStart = previousEnd;
                while (previousStart > 0 && body[previousStart - 1] is not ('\n' or '\r')) previousStart--;
                var line = body.Substring(previousStart, previousEnd - previousStart).Trim();
                if (!line.StartsWith('#') || line.Length == 0) break;
                start = previousStart;
            }
        }
        return (start, end - start);
    }

    private static string IndentAt(string body, int lineStart)
    {
        var index = lineStart;
        while (index < body.Length && body[index] is ' ' or '\t') index++;
        return body.Substring(lineStart, index - lineStart);
    }

    private static string Reindent(string text, string targetIndent)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        while (lines.Length > 0 && lines[^1].Length == 0) lines = lines[..^1];
        if (lines.Length == 0) return string.Empty;
        var common = lines.Where(line => line.Trim().Length > 0)
            .Select(line => line.Length - line.TrimStart().Length)
            .DefaultIfEmpty(0).Min();
        var builder = new System.Text.StringBuilder();
        foreach (var line in lines)
        {
            if (line.Length == 0) { builder.Append('\n'); continue; }
            builder.Append(targetIndent).Append(line[Math.Min(common, line.Length)..]).Append('\n');
        }
        return builder.ToString();
    }

    private static string Newline(string body) =>
        body.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
}
