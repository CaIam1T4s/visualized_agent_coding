using System;

namespace VisualizedAgentCoding.Domain.Projects;

/// <summary>
/// Upgrades older manifests so projects created by earlier builds keep opening.
/// Migration only fills in what newer schemas require; it never rewrites user content.
/// </summary>
public static class ProjectManifestMigration
{
    /// <summary>
    /// Returns a manifest at <see cref="ProjectLayout.CurrentSchemaVersion"/>.
    /// Version 1 manifests gain a project id, an entry script id and empty index lists.
    /// Unknown or future versions are returned unchanged so validation rejects them.
    /// </summary>
    public static ProjectManifest Upgrade(ProjectManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        return manifest.SchemaVersion switch
        {
            1 => manifest with
            {
                SchemaVersion = ProjectLayout.CurrentSchemaVersion,
                ProjectId = string.IsNullOrWhiteSpace(manifest.ProjectId) ? NewId("project") : manifest.ProjectId,
                MainScript = string.IsNullOrWhiteSpace(manifest.MainScript)
                    ? ProjectLayout.MainScriptRelativePath
                    : manifest.MainScript,
                MainScriptId = string.IsNullOrWhiteSpace(manifest.MainScriptId) ? NewId("script") : manifest.MainScriptId,
                Scripts = manifest.Scripts ?? [],
                EventHandlers = manifest.EventHandlers ?? [],
            },
            _ => manifest,
        };
    }

    /// <summary>True when <see cref="Upgrade"/> would change the manifest.</summary>
    public static bool NeedsUpgrade(ProjectManifest? manifest) =>
        manifest is not null && manifest.SchemaVersion < ProjectLayout.CurrentSchemaVersion;

    private static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";
}
