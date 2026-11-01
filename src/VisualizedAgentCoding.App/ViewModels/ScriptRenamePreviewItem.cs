using System;
using System.Collections.Generic;
using System.Linq;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Domain.Projects;

namespace happy_agent_coding.ViewModels;

public sealed record RenameDiffLine(string Text, bool Added)
{
    public string Display => (Added ? "+ " : "- ") + Text;
}

/// <summary>A rename preview over editor-visible source, never the raw template.</summary>
public sealed class ScriptRenamePreviewItem
{
    public ScriptRenamePreviewItem(ScriptRenameDocument document)
    {
        Document = document;
        VisibleDiff = BuildDiff(document.BeforeVisible ?? document.Before,
            document.AfterVisible ?? document.After);
    }

    public ScriptRenameDocument Document { get; }
    public string Path => Document.Path.Replace('\\', '/');
    public string PathDisplay => string.Equals(Document.Path, Document.NewPath, StringComparison.Ordinal)
        ? Path : $"{Path} → {Document.NewPath?.Replace('\\', '/')}";
    public bool ShowPath => !Document.IsTarget;
    public bool HasClassChange => Document.OldClassName is not null && Document.NewClassName is not null
        && !string.Equals(Document.OldClassName, Document.NewClassName, StringComparison.Ordinal);
    public string ClassChange => HasClassChange
        ? $"类名：{Document.OldClassName} → {Document.NewClassName}" : string.Empty;
    public IReadOnlyList<RenameDiffLine> VisibleDiff { get; }
    public bool HasVisibleChanges => VisibleDiff.Count > 0;

    private static IReadOnlyList<RenameDiffLine> BuildDiff(string before, string after)
    {
        var oldLines = SplitLines(before);
        var newLines = SplitLines(after);
        if (oldLines.SequenceEqual(newLines)) return [];
        if ((long)oldLines.Length * newLines.Length > 4_000_000)
            return PrefixSuffixDiff(oldLines, newLines);
        var lengths = new int[oldLines.Length + 1, newLines.Length + 1];
        for (var oldIndex = oldLines.Length - 1; oldIndex >= 0; oldIndex--)
            for (var newIndex = newLines.Length - 1; newIndex >= 0; newIndex--)
                lengths[oldIndex, newIndex] = oldLines[oldIndex] == newLines[newIndex]
                    ? lengths[oldIndex + 1, newIndex + 1] + 1
                    : Math.Max(lengths[oldIndex + 1, newIndex], lengths[oldIndex, newIndex + 1]);
        var changes = new List<RenameDiffLine>();
        var left = 0;
        var right = 0;
        while (left < oldLines.Length && right < newLines.Length)
        {
            if (oldLines[left] == newLines[right]) { left++; right++; }
            else if (lengths[left + 1, right] >= lengths[left, right + 1])
                changes.Add(new(oldLines[left++], false));
            else
                changes.Add(new(newLines[right++], true));
        }
        while (left < oldLines.Length) changes.Add(new(oldLines[left++], false));
        while (right < newLines.Length) changes.Add(new(newLines[right++], true));
        return changes;
    }

    private static IReadOnlyList<RenameDiffLine> PrefixSuffixDiff(string[] before, string[] after)
    {
        var prefix = 0;
        while (prefix < before.Length && prefix < after.Length && before[prefix] == after[prefix]) prefix++;
        var suffix = 0;
        while (suffix < before.Length - prefix && suffix < after.Length - prefix
            && before[^(suffix + 1)] == after[^(suffix + 1)]) suffix++;
        return [.. before[prefix..^suffix].Select(line => new RenameDiffLine(line, false)),
            .. after[prefix..^suffix].Select(line => new RenameDiffLine(line, true))];
    }

    private static string[] SplitLines(string source)
    {
        var lines = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        return lines.Length > 0 && lines[^1].Length == 0 ? lines[..^1] : lines;
    }

    public ProjectScriptKind? Kind => Document.Kind;
}
