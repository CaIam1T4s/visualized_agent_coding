using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace happy_agent_coding.Services;

public sealed record ProjectManifest(int SchemaVersion, string Name, string MainScript);

/// <summary>Raised when a folder is not a valid project workspace.</summary>
public sealed class ProjectFormatException(string message) : IOException(message);

public sealed class ProjectWorkspace
{
    private const string ManifestFileName = "project.json";
    private const string MainScriptRelativePath = "scripts/main.py";
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Directories created for every project workspace, relative to the project root.</summary>
    public static readonly IReadOnlyList<string> ProjectDirectories =
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

    public string RootPath { get; }
    public string Name { get; }
    public string MainScriptPath => Path.Combine(RootPath, "scripts", "main.py");
    public string UserScriptsPath => Path.Combine(RootPath, "scripts", "user");

    private ProjectWorkspace(string rootPath, string name)
    {
        RootPath = rootPath;
        Name = name;
    }

    public static ProjectWorkspace Create(string rootPath, string? name = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        var root = Path.GetFullPath(rootPath);
        if (File.Exists(root))
            throw new ProjectFormatException("The selected path is a file, not a project folder.");

        Directory.CreateDirectory(root);
        var manifestPath = Path.Combine(root, ManifestFileName);
        if (File.Exists(manifestPath))
            throw new ProjectFormatException("A project already exists at this location.");

        var projectName = string.IsNullOrWhiteSpace(name) ? new DirectoryInfo(root).Name : name.Trim();
        EnsureProjectDirectories(root);
        WriteAtomically(manifestPath, JsonSerializer.Serialize(
            new ProjectManifest(CurrentSchemaVersion, projectName, MainScriptRelativePath), JsonOptions));
        WriteAtomically(Path.Combine(root, "scripts", "main.py"), "def main():\n    pass\n\n\nif __name__ == \"__main__\":\n    main()\n");
        return new ProjectWorkspace(root, projectName);
    }

    /// <summary>Checks whether the folder holds a valid project workspace, without repairing it.</summary>
    public static bool IsProject(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            return false;
        try
        {
            var root = Path.GetFullPath(rootPath);
            if (!Directory.Exists(root))
                return false;

            ReadManifest(root);
            return File.Exists(Path.Combine(root, MainScriptRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        }
        catch (Exception ex) when (ex is ProjectFormatException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static ProjectWorkspace Open(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        var root = Path.GetFullPath(rootPath);
        if (!Directory.Exists(root))
            throw new ProjectFormatException("The selected folder does not exist.");

        var manifest = ReadManifest(root);
        var project = new ProjectWorkspace(root, manifest.Name);
        if (!File.Exists(project.MainScriptPath))
            throw new ProjectFormatException($"The project is missing its entry script: {MainScriptRelativePath}.");

        EnsureProjectDirectories(root);
        return project;
    }

    private static ProjectManifest ReadManifest(string root)
    {
        var manifestPath = Path.Combine(root, ManifestFileName);
        if (!File.Exists(manifestPath))
            throw new ProjectFormatException(
                $"The selected folder is not a project: {ManifestFileName} was not found.");

        ProjectManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ProjectManifest>(File.ReadAllText(manifestPath))
                ?? throw new ProjectFormatException($"{ManifestFileName} is empty.");
        }
        catch (JsonException ex)
        {
            throw new ProjectFormatException($"{ManifestFileName} is not valid project JSON: {ex.Message}");
        }

        if (manifest.SchemaVersion != CurrentSchemaVersion)
            throw new ProjectFormatException($"Unsupported project schema version: {manifest.SchemaVersion}.");
        if (string.IsNullOrWhiteSpace(manifest.Name))
            throw new ProjectFormatException($"{ManifestFileName} does not declare a project name.");
        if (!string.Equals(manifest.MainScript, MainScriptRelativePath, StringComparison.Ordinal))
            throw new ProjectFormatException($"{ManifestFileName} declares an unsupported entry script.");

        return manifest;
    }

    public string LoadMainScript() => File.ReadAllText(MainScriptPath);

    public void SaveMainScript(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        WriteAtomically(MainScriptPath, content);
    }

    private static void EnsureProjectDirectories(string root)
    {
        foreach (var directory in ProjectDirectories)
            Directory.CreateDirectory(Path.Combine(root, directory.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static void WriteAtomically(string path, string content)
    {
        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporaryPath, content);
            if (File.Exists(path))
                File.Move(temporaryPath, path, overwrite: true);
            else
                File.Move(temporaryPath, path);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
