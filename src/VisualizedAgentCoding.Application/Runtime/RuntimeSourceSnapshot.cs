using System.Security.Cryptography;
using System.Text;
using VisualizedAgentCoding.Application.Editor;
using VisualizedAgentCoding.Application.Editor.ObjectScripts;
using VisualizedAgentCoding.Domain.Projects;

namespace VisualizedAgentCoding.Application.Runtime;

public sealed record RuntimeMappedPosition(int ViewLine, int ViewColumn, string Code, string RegionTitle, string Navigation = "code");
public sealed record RuntimeRegionSnapshot(string Id, string Kind, string? EventName, SourceSpan Span);

/// <summary>Immutable coordinates frozen from a fresh projection; never keeps the live editor.</summary>
public sealed class RuntimeSourceSnapshot
{
    private sealed record Line(int FullLine, int ViewLine, int Shift, int Length, string Region);
    private readonly Line[] lines;
    private readonly string[] viewLines;
    private readonly (int First, int Last)? importLines;
    private readonly int importViewLine;
    private readonly Dictionary<int, string> hiddenRegionTitles = [];
    public string ScriptId { get; }
    public string? ObjectId { get; }
    public string Path { get; }
    public string AbsolutePath { get; }
    public string Source { get; }
    public string Fingerprint { get; }
    public string ViewSource { get; }
    public string Label { get; }
    public System.Collections.ObjectModel.ReadOnlyCollection<RuntimeRegionSnapshot> Regions { get; }
    public int? ImportFullLine => importLines?.First;

    public static ISourceProjection? CreateProjection(ProjectScriptEntry script, string uri, string source,
        ObjectScriptDocument? document, ManagedImportsBlock? imports) => script.Kind == ProjectScriptKind.User
        ? new UserScriptImportProjection(new(script.Id, uri, source, imports))
        : document?.HasUserRegion == true || document?.HasRegions == true ? EditorProjection.Create(document) : null;

    public RuntimeSourceSnapshot(string root, ProjectScriptEntry script, string source, ISourceProjection? projection,
        ObjectScriptDocument? document = null, ManagedImportsBlock? imports = null)
    {
        if (projection is not null && projection.FullSource != source)
            throw new ArgumentException("运行快照与投影源码不一致。");
        ScriptId = script.Id; ObjectId = script.ObjectId; Path = script.Path; Source = source;
        Regions = Array.AsReadOnly((document?.Regions ?? []).Select(r => new RuntimeRegionSnapshot(r.Id, r.Kind, r.EventName, r.Span)).ToArray());
        AbsolutePath = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, script.Path));
        Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
        Label = (script.Kind switch { ProjectScriptKind.Object => "角色 ", ProjectScriptKind.Backdrop => "背景 ", _ => "用户脚本 " })
            + System.IO.Path.GetFileNameWithoutExtension(script.Path);
        ViewSource = projection?.ViewSource ?? "";
        viewLines = ViewSource.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        var fullLines = source.Split('\n');
        var fullOffsets = new int[fullLines.Length];
        for (var i = 1; i < fullLines.Length; i++) fullOffsets[i] = fullOffsets[i - 1] + fullLines[i - 1].Length + 1;
        var block = document?.Imports ?? imports;
        if (block is not null)
        {
            var first = 1 + source[..block.Span.Offset].Count(c => c == '\n');
            importLines = (first, first + source.Substring(block.Span.Offset, block.Span.Length).Count(c => c == '\n'));
            if (projection?.TryToViewPosition(first, 1, out var row, out _) == true) importViewLine = row;
        }
        var frozen = new List<Line>();
        foreach (var region in document?.Regions ?? [])
        {
            // In confirmed v2/v3, the handler and begin marker immediately precede its body.
            if (document!.TemplateVersion is 2 or 3 && region.Kind != "members")
            {
                var first = 1 + source[..region.Span.Offset].Count(c => c == '\n');
                hiddenRegionTitles[first - 2] = RegionTitle(region, document.BackdropNames) + "（系统包装）";
                hiddenRegionTitles[first - 1] = RegionTitle(region, document.BackdropNames) + "（系统标记）";
            }
        }
        if (projection is not null)
        {
            for (var row = 1; row <= viewLines.Length; row++)
            {
                var (fullLine, column) = projection.ToFullPosition(row, 1);
                if (fullLine < 1 || fullLine > fullLines.Length || IsImport(fullLine)) continue;
                var offset = fullOffsets[fullLine - 1] + Math.Max(0, column - 1);
                var region = document?.Regions?.FirstOrDefault(r => offset >= r.Span.Offset && offset < r.Span.Offset + r.Span.Length);
                if (script.Kind != ProjectScriptKind.User && region is null) continue;
                frozen.Add(new(fullLine, row, Math.Max(0, column - 1), viewLines[row - 1].Length,
                    region is null ? "" : RegionTitle(region, document?.BackdropNames)));
            }
        }
        lines = frozen.ToArray();
    }

    private bool IsImport(int line) => importLines is { } span && line >= span.First && line < span.Last;
    public string? HiddenRegionTitle(int fullLine) => hiddenRegionTitles.GetValueOrDefault(fullLine);
    public RuntimeMappedPosition? Map(int fullLine, int fullColumn = 1)
    {
        if (IsImport(fullLine)) return new(importViewLine, 1, "", "受管理导入", importViewLine > 0 ? "imports" : "none");
        var line = lines.FirstOrDefault(l => l.FullLine == fullLine);
        return line is null ? null : new(line.ViewLine, Math.Clamp(fullColumn - line.Shift, 1, line.Length + 1),
            viewLines[line.ViewLine - 1], line.Region);
    }
    public (int Line, int Column) ToFullPosition(int viewLine, int viewColumn)
    {
        if (viewLine == importViewLine && importLines is { } span) return (span.First, 1);
        var line = lines.FirstOrDefault(l => l.ViewLine == viewLine);
        return line is null ? (0, 0) : (line.FullLine, line.Shift + Math.Clamp(viewColumn, 1, line.Length + 1));
    }
    public static string RegionTitle(ObjectScriptRegion region, IReadOnlyDictionary<string, string>? backdropNames = null) => region.Kind switch
    {
        "members" => "类定义", "initialize" => "初始化", "start" => "程序启动",
        "event" => MessageEvents.Title(region.EventName ?? region.Id, backdropNames).Replace('\r', ' ').Replace('\n', ' '), _ => "用户区域",
    };
}
