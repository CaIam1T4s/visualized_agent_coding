using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using VisualizedAgentCoding.Application.Ports;
using VisualizedAgentCoding.Domain.Projects;

namespace VisualizedAgentCoding.Infrastructure.Projects;

/// <summary>JSON-file based <see cref="IProjectStore"/> implementation.</summary>
public sealed class JsonProjectStore : IProjectStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        RespectNullableAnnotations = true,
    };

    public ProjectManifest ReadManifest(string rootPath)
    {
        var manifestPath = ManifestPath(rootPath);
        if (!File.Exists(manifestPath))
            throw new ProjectFormatException(
                $"The selected folder is not a project: {ProjectLayout.ManifestFileName} was not found.");

        try
        {
            return JsonSerializer.Deserialize<ProjectManifest>(File.ReadAllText(manifestPath), JsonOptions)
                ?? throw new ProjectFormatException($"{ProjectLayout.ManifestFileName} is empty.");
        }
        catch (JsonException ex)
        {
            throw new ProjectFormatException(
                $"{ProjectLayout.ManifestFileName} is not valid project JSON: {ex.Message}", ex);
        }
    }

    public void WriteManifest(string rootPath, ProjectManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        WriteAtomically(ManifestPath(rootPath), JsonSerializer.Serialize(manifest, JsonOptions));
    }

    public void CreateProject(string rootPath, ProjectManifest manifest, string mainScriptContent)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(mainScriptContent);

        var root = Path.GetFullPath(rootPath);
        Directory.CreateDirectory(root);
        if (File.Exists(ManifestPath(root)))
            throw new ProjectFormatException("A project already exists at this location.");

        EnsureLayout(root);
        WriteAtomically(ManifestPath(root), JsonSerializer.Serialize(manifest, JsonOptions));
        WriteAtomically(ToPlatformPath(root, ProjectLayout.MainScriptRelativePath), mainScriptContent);
    }

    public string ReadMainScript(string rootPath) =>
        File.ReadAllText(ToPlatformPath(Path.GetFullPath(rootPath), ProjectLayout.MainScriptRelativePath));

    public void WriteMainScript(string rootPath, string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        WriteAtomically(ToPlatformPath(Path.GetFullPath(rootPath), ProjectLayout.MainScriptRelativePath), content);
    }

    public bool ScriptExists(string rootPath, string relativePath)
    {
        var path = ResolveScriptPath(rootPath, relativePath);
        return path is not null && File.Exists(path);
    }

    public string ReadScript(string rootPath, string relativePath)
    {
        var path = ResolveScriptPath(rootPath, relativePath)
            ?? throw new ProjectFormatException($"Script path escapes the project: {relativePath}");
        return File.ReadAllText(path);
    }

    public void WriteScript(string rootPath, string relativePath, string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var path = ResolveScriptPath(rootPath, relativePath)
            ?? throw new ProjectFormatException($"Script path escapes the project: {relativePath}");

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        WriteAtomically(path, content);
    }

    public IReadOnlyList<string> ListScriptFiles(string rootPath)
    {
        var root = Path.GetFullPath(rootPath);
        var results = new List<string>();
        foreach (var relativeDirectory in new[]
                 {
                     ProjectLayout.UserScriptDirectory,
                     ProjectLayout.ObjectScriptDirectory,
                     ProjectLayout.BackdropScriptDirectory,
                 })
        {
            var directory = ToPlatformPath(root, relativeDirectory);
            if (!Directory.Exists(directory))
                continue;

            results.AddRange(Directory
                .EnumerateFiles(directory, "*.py", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/')));
        }

        results.Sort(StringComparer.OrdinalIgnoreCase);
        return results;
    }

    public void EnsureLayout(string rootPath)
    {
        var root = Path.GetFullPath(rootPath);
        foreach (var directory in ProjectLayout.Directories)
            Directory.CreateDirectory(ToPlatformPath(root, directory));
    }

    private static string ManifestPath(string rootPath) =>
        Path.Combine(Path.GetFullPath(rootPath), ProjectLayout.ManifestFileName);

    private static string ToPlatformPath(string root, string relativePath) =>
        Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Resolves a project-relative script path, refusing traversal outside the project.</summary>
    private static string? ResolveScriptPath(string rootPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        var root = Path.GetFullPath(rootPath);
        var candidate = Path.GetFullPath(ToPlatformPath(root, relativePath));
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? candidate : null;
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
