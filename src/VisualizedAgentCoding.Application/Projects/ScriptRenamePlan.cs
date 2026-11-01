using VisualizedAgentCoding.Domain.Projects;

namespace VisualizedAgentCoding.Application.Projects;

public sealed record ScriptRenameDocument(string Path, string DiskSource, string Before, string After,
    string? BeforeVisible = null, string? AfterVisible = null, ProjectScriptKind? Kind = null,
    string? OldClassName = null, string? NewClassName = null, string? NewPath = null, bool IsTarget = false);
public sealed record ScriptRenamePlan(ProjectWorkspace Project, string ScriptId, string NewName, string NewPath,
    long Version, string ManifestSnapshot, ScriptRenameDocument[] Documents)
{
    public ScriptRenameDocument[] Changes => Documents.Where(d => d.Before != d.After).ToArray();
}
