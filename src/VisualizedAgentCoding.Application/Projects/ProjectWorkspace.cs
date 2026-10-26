using System;
using VisualizedAgentCoding.Domain.Projects;

namespace VisualizedAgentCoding.Application.Projects;

/// <summary>An opened project workspace: its root path plus the validated manifest.</summary>
public sealed class ProjectWorkspace
{
    public string RootPath { get; }
    public string Name { get; }
    public string ProjectId { get; }
    public ProjectManifest Manifest { get; }

    public string MainScriptPath =>
        System.IO.Path.Combine(RootPath, ProjectLayout.MainScriptRelativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));

    internal ProjectWorkspace(string rootPath, ProjectManifest manifest)
    {
        RootPath = rootPath;
        Name = manifest.Name;
        ProjectId = manifest.ProjectId;
        Manifest = manifest;
    }

    /// <summary>Resolves a project-relative path to an absolute file system path.</summary>
    public string ToAbsolutePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        return System.IO.Path.Combine(RootPath, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
    }

    /// <summary>Returns the indexed script with the given stable id, or null.</summary>
    public ProjectScriptEntry? FindScript(string? scriptId)
    {
        if (string.IsNullOrWhiteSpace(scriptId))
            return null;

        foreach (var script in Manifest.Scripts)
            if (string.Equals(script.Id, scriptId, StringComparison.Ordinal))
                return script;

        return null;
    }

    internal ProjectWorkspace WithManifest(ProjectManifest manifest) => new(RootPath, manifest);
}
