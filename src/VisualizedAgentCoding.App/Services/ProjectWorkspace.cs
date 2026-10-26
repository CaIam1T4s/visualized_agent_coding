using System;
using System.IO;
using System.Text.Json;

namespace happy_agent_coding.Services;

public sealed record ProjectManifest(int SchemaVersion, string Name, string MainScript);

public sealed class ProjectWorkspace
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

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
        Directory.CreateDirectory(root);
        var manifestPath = Path.Combine(root, "project.json");
        if (File.Exists(manifestPath))
            throw new IOException("A project already exists at this location.");

        var projectName = string.IsNullOrWhiteSpace(name) ? new DirectoryInfo(root).Name : name.Trim();
        Directory.CreateDirectory(Path.Combine(root, "scripts"));
        EnsureScriptDirectories(root);
        WriteAtomically(manifestPath, JsonSerializer.Serialize(
            new ProjectManifest(CurrentSchemaVersion, projectName, "scripts/main.py"), JsonOptions));
        WriteAtomically(Path.Combine(root, "scripts", "main.py"), "def main():\n    pass\n\n\nif __name__ == \"__main__\":\n    main()\n");
        return new ProjectWorkspace(root, projectName);
    }

    public static ProjectWorkspace Open(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        var root = Path.GetFullPath(rootPath);
        var manifestPath = Path.Combine(root, "project.json");
        var manifest = JsonSerializer.Deserialize<ProjectManifest>(File.ReadAllText(manifestPath))
            ?? throw new InvalidDataException("The project manifest is empty or invalid.");
        if (manifest.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidDataException($"Unsupported project schema version: {manifest.SchemaVersion}.");
        if (string.IsNullOrWhiteSpace(manifest.Name) || manifest.MainScript != "scripts/main.py")
            throw new InvalidDataException("The project manifest contains unsupported project paths.");

        var project = new ProjectWorkspace(root, manifest.Name);
        if (!File.Exists(project.MainScriptPath))
            throw new FileNotFoundException("The project main script is missing.", project.MainScriptPath);
        EnsureScriptDirectories(root);
        return project;
    }

    public string LoadMainScript() => File.ReadAllText(MainScriptPath);

    public void SaveMainScript(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        WriteAtomically(MainScriptPath, content);
    }

    private static void EnsureScriptDirectories(string root)
    {
        foreach (var directory in new[] { "user", "objects", "backdrops" })
            Directory.CreateDirectory(Path.Combine(root, "scripts", directory));
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
