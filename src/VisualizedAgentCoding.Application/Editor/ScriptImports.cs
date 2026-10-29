using VisualizedAgentCoding.Application.Editor.ObjectScripts;
using VisualizedAgentCoding.Application.Python;

namespace VisualizedAgentCoding.Application.Editor;

public sealed record ScriptImport(string Module, string? Name = null, string? Alias = null, string Source = "standard", string? ScriptId = null)
{
    public string Binding => Alias ?? Name ?? Module.Split('.')[0];
    public string Summary => (Source == "project" && (Alias is not null || Name is not null) ? Module.Split('.').Last() : Module)
        + (Name is null ? "" : "." + Name)
        + (Alias is null || Source == "project" && Name is null && Alias == Module.Split('.').Last() ? "" : "（别名 " + Alias + "）");
}
public sealed record ManagedImportsBlock(SourceSpan Span, ScriptImport[] Entries);
public sealed record ImportScriptTarget(string Id, string Module, string Kind, string DisplayName);
public sealed record ScriptImportContext(string ScriptId, string Kind, ImportScriptTarget[] Scripts, SourceSpan[] Regions);
public sealed record ScriptImportsRequest(string Uri, long Version, string Source, ScriptImportContext Context,
    ScriptImport[]? Imports = null, int? ConvertLine = null);
public sealed record ScriptImportsResult(string Uri, long Version, string Source, ManagedImportsBlock? Block,
    string[] StandardModules, PythonDiagnostic[] Diagnostics, bool SyntaxValid, int InsertOffset, string? Problem);

public interface ISourceProjection
{
    string ViewSource { get; }
    string FullSource { get; }
    (int Line, int Column) ToFullPosition(int line, int column);
    bool TryToViewPosition(int line, int column, out int viewLine, out int viewColumn);
}

public static class ImportSummary
{
    public static string Text(ManagedImportsBlock block) => "# 已导入：" + (block.Entries.Length == 0 ? "无" : string.Join("、", block.Entries.Select(i => i.Summary)));
}
