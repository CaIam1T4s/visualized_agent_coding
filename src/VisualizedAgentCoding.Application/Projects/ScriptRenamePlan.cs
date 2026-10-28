namespace VisualizedAgentCoding.Application.Projects;

public sealed record ScriptRenameDocument(string Path, string DiskSource, string Before, string After);
public sealed record ScriptRenamePlan(ProjectWorkspace Project, string ScriptId, string NewName, string NewPath,
    long Version, string ManifestSnapshot, ScriptRenameDocument[] Documents)
{
    public ScriptRenameDocument[] Changes => Documents.Where(d => d.Before != d.After).ToArray();
}
