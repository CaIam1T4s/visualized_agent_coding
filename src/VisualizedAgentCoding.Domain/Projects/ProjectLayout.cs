using System.Collections.Generic;

namespace VisualizedAgentCoding.Domain.Projects;

/// <summary>Fixed project workspace layout and format constants.</summary>
public static class ProjectLayout
{
    public const string ManifestFileName = "project.json";
    public const string MainScriptRelativePath = "scripts/main.py";
    public const int CurrentSchemaVersion = 1;

    /// <summary>Directories created for every project, relative to the project root.</summary>
    public static readonly IReadOnlyList<string> Directories =
    [
        "scenes",
        "scripts",
        "scripts/user",
        "scripts/objects",
        "scripts/backdrops",
        "runtime",
        "blocks",
        "assets",
        "assets/images",
        "assets/objects",
    ];
}
