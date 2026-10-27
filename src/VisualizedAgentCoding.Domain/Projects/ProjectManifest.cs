using System.Collections.Generic;

namespace VisualizedAgentCoding.Domain.Projects;

/// <summary>The persisted project manifest stored in <c>project.json</c>.</summary>
/// <remarks>
/// Declared with init properties rather than a positional record so older manifests that
/// lack a field still deserialize, and so newer fields can be added without breaking readers.
/// </remarks>
public sealed record ProjectManifest
{
    public int SchemaVersion { get; init; } = ProjectLayout.CurrentSchemaVersion;

    /// <summary>Stable project id; never derived from the folder name.</summary>
    public string ProjectId { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string MainScript { get; init; } = ProjectLayout.MainScriptRelativePath;

    /// <summary>Stable id of the entry script, so the runtime can address it like any other script.</summary>
    public string MainScriptId { get; init; } = string.Empty;

    public List<ProjectScriptEntry> Scripts { get; init; } = [];

    /// <summary>Unindexed files awaiting deletion; never adopt these as external scripts.</summary>
    public List<string> PendingScriptDeletions { get; init; } = [];

    public List<ProjectEventHandler> EventHandlers { get; init; } = [];
}
