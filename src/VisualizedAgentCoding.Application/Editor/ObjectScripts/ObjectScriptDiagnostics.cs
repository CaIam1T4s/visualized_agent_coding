using System.Text.RegularExpressions;
using VisualizedAgentCoding.Application.Python;

namespace VisualizedAgentCoding.Application.Editor.ObjectScripts;

/// <summary>Present diagnostics against the trusted user projection, without guessing template boundaries.</summary>
public static class ObjectScriptDiagnostics
{
    private static readonly Regex MissingBody = new(@"^expected an indented block after .+ on line (\d+)$",
        RegexOptions.CultureInvariant);

    public static PythonDiagnostic[] Present(IEnumerable<PythonDiagnostic> diagnostics, VisualizedAgentCoding.Application.Editor.ISourceProjection? projection)
    {
        return diagnostics.Select(d => Map(d, projection)).OfType<PythonDiagnostic>()
            .DistinctBy(d => (d.Severity, MessageIdentity(d.Message), d.Line, d.Column)).ToArray();
    }

    private static string MessageIdentity(string message) => message.StartsWith("expected an indented block", StringComparison.Ordinal)
        ? "expected an indented block" : message;

    private static PythonDiagnostic? Map(PythonDiagnostic diagnostic, VisualizedAgentCoding.Application.Editor.ISourceProjection? projection)
    {
        if (projection is null) return diagnostic;
        if (projection is not IEditorProjection objectProjection)
            return projection.TryToViewPosition(diagnostic.Line, diagnostic.Column, out var userLine, out var userColumn)
                ? diagnostic with { Line = userLine, Column = userColumn } : diagnostic;
        var document = objectProjection.Document;
        var trusted = document.Status == ObjectScriptStatus.Recognized;
        var missing = MissingBody.Match(diagnostic.Message);
        var ownerLine = diagnostic.RelatedLine ?? (missing.Success && int.TryParse(missing.Groups[1].Value, out var parsedLine) ? parsedLine : 0);
        if (trusted && ownerLine > 0 && diagnostic.Message.StartsWith("expected an indented block", StringComparison.Ordinal))
        {
            // CPython points at the next statement (often a hidden handler), but
            // explicitly names the definition whose body is missing.
            var text = LineText(document.FullSource, ownerLine);
            var column = text.Length - text.TrimStart(' ', '\t').Length + 1;
            if (projection.TryToViewPosition(ownerLine, column, out var line, out var viewColumn))
                return diagnostic with { Line = line, Column = viewColumn,
                    Message = missing.Success ? diagnostic.Message[..missing.Groups[1].Index] + line : diagnostic.Message };
            if (projection is MultiRegionProjection multi)
            {
                var region = document.Regions!.FirstOrDefault(r => r.Kind != "members"
                    && LineAt(document.FullSource, r.Span.Offset) - 2 == ownerLine);
                var visible = multi.EditableRegions.FirstOrDefault(r => r.Id == region?.Id);
                if (visible is not null)
                    return diagnostic with { Line = LineAt(multi.ViewSource, visible.Span.Offset), Column = 1,
                        Message = "该区域需要至少一条语句；请填写代码或添加 pass。" };
            }
        }
        if (projection.TryToViewPosition(diagnostic.Line, diagnostic.Column, out var mappedLine, out var mappedColumn))
            return diagnostic with { Line = mappedLine, Column = mappedColumn };
        // Only discard recovery diagnostics outside editable slices of a verified
        // shell. Native/unknown diagnostics are never hidden by this rule.
        if (trusted && diagnostic.Parser == "recovery") return null;
        return diagnostic with { Line = 0, Column = 0,
            Message = "语法错误（暂无法定位）：" + diagnostic.Message };
    }

    private static int LineAt(string source, int offset) => 1 + source[..offset].Count(c => c == '\n');
    private static string LineText(string source, int line) => source.Split('\n').ElementAtOrDefault(line - 1)?.TrimEnd('\r') ?? "";
}
