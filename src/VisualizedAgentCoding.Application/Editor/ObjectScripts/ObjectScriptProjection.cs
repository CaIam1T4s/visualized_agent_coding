namespace VisualizedAgentCoding.Application.Editor.ObjectScripts;

/// <summary>Exact UTF-16 slice projection. Never strips indentation or normalizes source.</summary>
public sealed class ObjectScriptProjection : IEditorProjection
{
    private readonly ObjectScriptDocument original;
    public ObjectScriptDocument Document
    {
        get
        {
            var source = FullSource;
            var span = new SourceSpan(prefix.Length, source.Length - prefix.Length - suffix.Length);
            return original with { FullSource = source, UserRegion = span,
                Regions = original.Regions?.Select(r => r with { Span = span }).ToArray() };
        }
    }
    private readonly string prefix;
    private readonly string suffix;
    public int FirstFullLine { get; }
    public string ViewSource { get; private set; }
    public string FullSource => MergeForAnalysis();

    public ObjectScriptProjection(ObjectScriptDocument document)
    {
        original = document;
        if (!document.HasUserRegion) throw new ArgumentException("A confirmed user region is required.", nameof(document));
        var region = document.UserRegion!;
        prefix = document.FullSource[..region.Offset];
        suffix = document.FullSource[(region.Offset + region.Length)..];
        ViewSource = document.UserSource!;
        FirstFullLine = 1 + prefix.Count(c => c == '\n');
    }

    public int ToFullOffset(int viewOffset) => prefix.Length + Math.Clamp(viewOffset, 0, ViewSource.Length);
    public int? ToViewOffset(int fullOffset) => fullOffset < prefix.Length || fullOffset > prefix.Length + ViewSource.Length
        ? null : fullOffset - prefix.Length;

    public void Update(string viewSource)
    {
        // A hidden separator keeps the system end marker on its own line, even
        // when the user hasn't entered a final newline. It is never shown.
        ViewSource = viewSource;
    }

    public (int Line, int Column) ToFullPosition(int line, int column) => (FirstFullLine + Math.Max(1, line) - 1, Math.Max(1, column));

    public bool TryToViewPosition(int line, int column, out int viewLine, out int viewColumn)
    {
        viewLine = line - FirstFullLine + 1;
        viewColumn = column;
        var lineCount = 1 + ViewSource.Count(c => c == '\n');
        // A trailing empty view line represents EOF, not the hidden end marker.
        if (ViewSource.EndsWith('\n')) lineCount--;
        return ViewSource.Length > 0 && viewLine >= 1 && viewLine <= lineCount && column >= 1;
    }

    public string MergeForAnalysis() => prefix + ViewSource
        + (ViewSource.Length > 0 && !ViewSource.EndsWith('\n') ? (prefix.EndsWith("\r\n") ? "\r\n" : "\n") : "") + suffix;
}
