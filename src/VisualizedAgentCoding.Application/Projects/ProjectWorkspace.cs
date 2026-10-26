using System;
using VisualizedAgentCoding.Domain.Projects;

namespace VisualizedAgentCoding.Application.Projects;

/// <summary>An opened project workspace: its root path plus the validated manifest.</summary>
public sealed class ProjectWorkspace
{
    public string RootPath { get; }
    public string Name { get; }
    public ProjectManifest Manifest { get; }

    public string MainScriptPath =>
        System.IO.Path.Combine(RootPath, ProjectLayout.MainScriptRelativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));

    internal ProjectWorkspace(string rootPath, string name, ProjectManifest manifest)
    {
        RootPath = rootPath;
        Name = name;
        Manifest = manifest;
    }

    internal static ProjectWorkspace FromManifest(string rootPath, ProjectManifest manifest) =>
        new(rootPath, manifest.Name, manifest);
}
