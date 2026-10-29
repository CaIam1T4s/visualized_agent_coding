using VisualizedAgentCoding.Application.Editor.ObjectScripts;

namespace VisualizedAgentCoding.Application.Editor;

public sealed record UserScriptImportDocument(string ScriptId, string Uri, string Source, ManagedImportsBlock? Block);

/// <summary>Protect the generated summary while preserving ordinary Python source verbatim.</summary>
public sealed class UserScriptImportProjection : ISourceProjection
{
    private string before;
    private string after;
    private readonly string separator;
    private readonly string blockSource;
    private readonly string summary;
    private readonly UserScriptImportDocument original;
    public string ViewSource => before + separator + summary + after;
    public string FullSource => before + separator + blockSource + after;
    public UserScriptImportDocument Document => original with { Source = FullSource,
        Block = original.Block is null ? null : original.Block with { Span = new(before.Length + separator.Length, blockSource.Length) } };
    public ProjectedRegion[] EditableRegions => original.Block is null ? [new("body", new(0, after.Length))]
        : [.. before.Length == 0 ? Array.Empty<ProjectedRegion>() : [new ProjectedRegion("prefix", new(0, before.Length))],
            new("body", new(before.Length + separator.Length + summary.Length, after.Length))];

    public UserScriptImportProjection(UserScriptImportDocument document)
    {
        original = document;
        if (document.Block is not { } block)
        { before = separator = blockSource = summary = ""; after = document.Source; return; }
        var prefix = document.Source[..block.Span.Offset];
        separator = prefix.EndsWith("\r\n") ? "\r\n" : prefix.EndsWith('\n') ? "\n" : "";
        before = prefix[..(prefix.Length - separator.Length)];
        after = document.Source[(block.Span.Offset + block.Span.Length)..];
        blockSource = document.Source.Substring(block.Span.Offset, block.Span.Length);
        summary = ImportSummary.Text(block) + (blockSource.Contains("\r\n") ? "\r\n" : "\n");
    }

    public void UpdateRegions(IReadOnlyDictionary<string, string> bodies)
    {
        if (bodies.TryGetValue("prefix", out var prefix)) before = prefix;
        if (bodies.TryGetValue("body", out var body)) after = body;
    }
    public (int Line, int Column) ToFullPosition(int line, int column)
    {
        var first = 1 + (before + separator).Count(c => c == '\n');
        if (original.Block is null || line < first) return (line, column);
        if (line == first) return (first, 1);
        return (line + blockSource.Count(c => c == '\n') - summary.Count(c => c == '\n'), column);
    }
    public bool TryToViewPosition(int line, int column, out int viewLine, out int viewColumn)
    {
        var first = 1 + (before + separator).Count(c => c == '\n');
        var count = blockSource.Count(c => c == '\n');
        viewColumn = column;
        viewLine = original.Block is null || line < first ? line : line < first + count
            ? first : line - count + summary.Count(c => c == '\n');
        if (original.Block is not null && line >= first && line < first + count) viewColumn = 1;
        return line > 0;
    }
}
