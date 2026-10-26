namespace VisualizedAgentCoding.Domain.Projects;

/// <summary>A project script with a stable identity that survives renames.</summary>
/// <param name="Id">Stable script id, never derived from the file name.</param>
/// <param name="Path">Project-relative path using forward slashes.</param>
/// <param name="Kind">Directory family the script lives in.</param>
/// <param name="ObjectId">Stable stage object id for object and backdrop scripts.</param>
public sealed record ProjectScriptEntry(string Id, string Path, ProjectScriptKind Kind, string? ObjectId = null);
