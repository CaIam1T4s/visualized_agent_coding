using System.Text;

namespace VisualizedAgentCoding.Application.Editor.ObjectScripts;

public sealed record ProjectedRegion(string Id, SourceSpan Span);

/// <summary>One view, multiple stable regions. Titles and separators are generated, never parsed as identity.</summary>
public sealed class MultiRegionProjection : IEditorProjection
{
    private sealed record Mapping(int ViewLine, int FullLine, int Shift, int VisibleLength, int RawLength);
    private readonly Dictionary<string, PythonWrapperIndentation> codecs;
    private readonly Dictionary<string, string> bodies;
    private readonly List<Mapping> mappings = [];
    private readonly string newline;
    public ObjectScriptDocument BaselineDocument { get; private set; }
    public ObjectScriptDocument Document { get; private set; }
    public string ViewSource { get; private set; } = "";
    public string FullSource => Document.FullSource;
    public ProjectedRegion[] EditableRegions { get; private set; } = [];

    public MultiRegionProjection(ObjectScriptDocument document)
    {
        if (document.TemplateVersion is not (2 or 3) || !document.HasRegions) throw new ArgumentException("Confirmed v2 regions required.");
        BaselineDocument = Document = document;
        newline = document.FullSource.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        codecs = document.Regions!.ToDictionary(r => r.Id, r => new PythonWrapperIndentation(
            document.FullSource.Substring(r.Span.Offset, r.Span.Length), r.Indentation));
        bodies = codecs.ToDictionary(p => p.Key, p => p.Value.ViewSource);
        Rebuild();
    }

    public void UpdateRegions(IReadOnlyDictionary<string, string> changes)
    {
        if (changes.Keys.Any(id => !bodies.ContainsKey(id))) throw new ArgumentException("Unknown region ID.");
        foreach (var (id, body) in changes) bodies[id] = body;
        Rebuild();
    }

    public Dictionary<string, string> FullRegionSources() => Document.Regions!.ToDictionary(r => r.Id,
        r => Document.FullSource.Substring(r.Span.Offset, r.Span.Length));

    public void AcceptSavedDocument(ObjectScriptDocument document)
    {
        if (document.FullSource != FullSource || document.ScriptId != Document.ScriptId)
            throw new ArgumentException("Saved source differs from the current projection.");
        BaselineDocument = Document = document;
    }

    private void Rebuild()
    {
        // Always merge against the original shell. Region identities and system
        // fragments are not inferred from user-entered title-like comments.
        var original = BaselineDocument;
        var full = new StringBuilder();
        var view = new StringBuilder();
        var regions = new List<ObjectScriptRegion>();
        var editable = new List<ProjectedRegion>();
        var blocks = new Dictionary<string, (int Offset, int Length, int Line, int Lines)>();
        mappings.Clear();
        var previousEnd = 0;
        var fullLine = 1;
        var viewLine = 1;
        foreach (var region in original.Regions!)
        {
            var blockOffset = view.Length;
            var blockLine = viewLine;
            var shell = original.FullSource.Substring(previousEnd, region.Span.Offset - previousEnd);
            full.Append(shell);
            fullLine += shell.Count(c => c == '\n');
            var title = region.Kind switch
            {
                "members" => "# 类定义",
                "start" => "# 程序启动时运行",
                "initialize" => "# 初始化",
                _ => "# " + VisualizedAgentCoding.Domain.Projects.MessageEvents.Title(region.EventName ?? region.Id).Replace("\r", " ").Replace("\n", " "),
            };
            view.Append(title).Append(newline);
            viewLine++;
            var body = bodies[region.Id];
            editable.Add(new(region.Id, new(view.Length, body.Length)));
            view.Append(body).Append(newline); // protected boundary separator
            var first = full.Length;
            foreach (var line in codecs[region.Id].Encode(body))
            {
                var visibleLength = line.Visible.TrimEnd('\r', '\n').Length;
                var rawLength = line.Raw.TrimEnd('\r', '\n').Length;
                mappings.Add(new(viewLine, fullLine, rawLength - visibleLength, visibleLength, rawLength));
                full.Append(line.Raw);
                if (line.Raw.EndsWith('\n')) fullLine++;
                if (line.Visible.EndsWith('\n')) viewLine++;
            }
            if (body.Length == 0 || body.EndsWith('\n'))
                mappings.Add(new(viewLine, fullLine, region.Indentation, 0, region.Indentation));
            if (body.Length > 0 && !body.EndsWith('\n')) { full.Append(newline); fullLine++; }
            viewLine++; // separator newline
            regions.Add(region with { Span = new(first, full.Length - first) });
            previousEnd = region.Span.Offset + region.Span.Length;
            blocks[region.Id] = (blockOffset, view.Length - blockOffset, blockLine, viewLine - blockLine);
        }
        full.Append(original.FullSource, previousEnd, original.FullSource.Length - previousEnd);
        // Presentation order is independent of disk order. Existing files keep
        // their shell byte-for-byte, including files with initialize at the end.
        var orderedView = new StringBuilder();
        if (original.Imports is { Entries.Length: > 0 } imports)
            orderedView.Append(VisualizedAgentCoding.Application.Editor.ImportSummary.Text(imports)).Append(newline).Append(newline);
        var orderedEditable = new List<ProjectedRegion>();
        var orderedMappings = new List<Mapping>();
        var nextLine = 1 + orderedView.ToString().Count(c => c == '\n');
        foreach (var region in original.Regions!.OrderBy(r => r.Kind == "members" ? 0 : r.Kind == "initialize" ? 1 : 2))
        {
            var block = blocks[region.Id];
            var body = editable.First(r => r.Id == region.Id);
            orderedEditable.Add(body with { Span = new(orderedView.Length + body.Span.Offset - block.Offset, body.Span.Length) });
            orderedView.Append(view.ToString(block.Offset, block.Length));
            orderedMappings.AddRange(mappings.Where(m => m.ViewLine >= block.Line && m.ViewLine < block.Line + block.Lines)
                .Select(m => m with { ViewLine = nextLine + m.ViewLine - block.Line }));
            nextLine += block.Lines;
        }
        mappings.Clear();
        mappings.AddRange(orderedMappings);
        ViewSource = orderedView.ToString();
        EditableRegions = orderedEditable.ToArray();
        Document = original with { FullSource = full.ToString(), Regions = regions.ToArray() };
    }

    public (int Line, int Column) ToFullPosition(int line, int column)
    {
        var mapping = mappings.FirstOrDefault(m => m.ViewLine == line);
        return mapping is null ? (0, 0) : (mapping.FullLine, Math.Clamp(column + mapping.Shift, 1, mapping.RawLength + 1));
    }

    public bool TryToViewPosition(int line, int column, out int viewLine, out int viewColumn)
    {
        if (Document.Imports is { Entries.Length: > 0 } imports)
        {
            var first = 1 + FullSource[..imports.Span.Offset].Count(c => c == '\n');
            var last = first + FullSource.Substring(imports.Span.Offset, imports.Span.Length).Count(c => c == '\n');
            if (line >= first && line < last) { viewLine = viewColumn = 1; return true; }
        }
        var mapping = mappings.FirstOrDefault(m => m.FullLine == line);
        viewLine = mapping?.ViewLine ?? 0;
        viewColumn = mapping is null ? 0 : Math.Clamp(column - mapping.Shift, 1, mapping.VisibleLength + 1);
        return mapping is not null;
    }
}
