using System;
using System.IO;
using System.Text.Json;
using VisualizedAgentCoding.Application.Ports;
using VisualizedAgentCoding.Domain.Projects;

namespace VisualizedAgentCoding.Infrastructure.Projects;

/// <summary>JSON-file based <see cref="IProjectStore"/> implementation.</summary>
public sealed class JsonProjectStore : IProjectStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public ProjectManifest ReadManifest(string rootPath)
    {
        var manifestPath = Path.Combine(Path.GetFullPath(rootPath), ProjectLayout.ManifestFileName);
        if (!File.Exists(manifestPath))
            throw new ProjectFormatException(
                $"The selected folder is not a project: {ProjectLayout.ManifestFileName} was not found.");

        ProjectManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<ProjectManifest>(File.ReadAllText(manifestPath))
                ?? throw new ProjectFormatException($"{ProjectLayout.ManifestFileName} is empty.");
        }
        catch (JsonException ex)
        {
            throw new ProjectFormatException(
                $"{ProjectLayout.ManifestFileName} is not valid project JSON: {ex.Message}", ex);
        }

        if (manifest.SchemaVersion != ProjectLayout.CurrentSchemaVersion)
            throw new ProjectFormatException($"Unsupported project schema version: {manifest.SchemaVersion}.");
        if (string.IsNullOrWhiteSpace(manifest.Name))
            throw new ProjectFormatException($"{ProjectLayout.ManifestFileName} does not declare a project name.");
        if (!string.Equals(manifest.MainScript, ProjectLayout.MainScriptRelativePath, StringComparison.Ordinal))
            throw new ProjectFormatException($"{ProjectLayout.ManifestFileName} declares an unsupported entry script.");

        return manifest;
    }

    public void CreateProject(string rootPath, ProjectManifest manifest, string mainScriptContent)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(mainScriptContent);

        var root = Path.GetFullPath(rootPath);
        Directory.CreateDirectory(root);
        var manifestPath = Path.Combine(root, ProjectLayout.ManifestFileName);
        if (File.Exists(manifestPath))
            throw new ProjectFormatException("A project already exists at this location.");

        EnsureLayout(root);
        WriteAtomically(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
        WriteAtomically(ToPlatformPath(root, ProjectLayout.MainScriptRelativePath), mainScriptContent);
    }

    public string ReadMainScript(string rootPath) =>
        File.ReadAllText(ToPlatformPath(Path.GetFullPath(rootPath), ProjectLayout.MainScriptRelativePath));

    public void WriteMainScript(string rootPath, string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        WriteAtomically(ToPlatformPath(Path.GetFullPath(rootPath), ProjectLayout.MainScriptRelativePath), content);
    }

    public void EnsureLayout(string rootPath)
    {
        var root = Path.GetFullPath(rootPath);
        foreach (var directory in ProjectLayout.Directories)
            Directory.CreateDirectory(ToPlatformPath(root, directory));
    }

    private static string ToPlatformPath(string root, string relativePath) =>
        Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));

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
