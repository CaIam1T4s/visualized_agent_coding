using System.Collections.Generic;

namespace VisualizedAgentCoding.Domain.Projects;

/// <summary>Fixed project workspace layout and format constants.</summary>
public static class ProjectLayout
{
    public const string ManifestFileName = "project.json";
    public const string MainScriptRelativePath = "scripts/main.py";
    public const string UserScriptDirectory = "scripts/user";
    public const string ObjectScriptDirectory = "scripts/objects";
    public const string BackdropScriptDirectory = "scripts/backdrops";

    /// <summary>Version 2 adds stable project/script ids and the event handler index.</summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>Directories created for every project, relative to the project root.</summary>
    public static readonly IReadOnlyList<string> Directories =
    [
        "scenes",
        "scripts",
        UserScriptDirectory,
        ObjectScriptDirectory,
        BackdropScriptDirectory,
        "blocks",
        "assets",
        "assets/images",
        "assets/objects",
    ];

    /// <summary>Directory that owns scripts of the given kind.</summary>
    public static string DirectoryFor(ProjectScriptKind kind) => kind switch
    {
        ProjectScriptKind.User => UserScriptDirectory,
        ProjectScriptKind.Object => ObjectScriptDirectory,
        ProjectScriptKind.Backdrop => BackdropScriptDirectory,
        _ => throw new System.ArgumentOutOfRangeException(nameof(kind), kind, "Unknown script kind."),
    };

    /// <summary>Resolves the script kind from a project-relative path, or null when unrecognized.</summary>
    public static ProjectScriptKind? KindOfScript(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        if (normalized.StartsWith(UserScriptDirectory + "/", System.StringComparison.Ordinal))
            return ProjectScriptKind.User;
        if (normalized.StartsWith(ObjectScriptDirectory + "/", System.StringComparison.Ordinal))
            return ProjectScriptKind.Object;
        if (normalized.StartsWith(BackdropScriptDirectory + "/", System.StringComparison.Ordinal))
            return ProjectScriptKind.Backdrop;
        return null;
    }
}
