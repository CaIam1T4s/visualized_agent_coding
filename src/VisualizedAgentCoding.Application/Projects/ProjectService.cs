using System;
using System.IO;
using VisualizedAgentCoding.Application.Ports;
using VisualizedAgentCoding.Domain.Projects;

namespace VisualizedAgentCoding.Application.Projects;

/// <summary>Use cases for creating, opening, saving and closing project workspaces.</summary>
public sealed class ProjectService
{
    private const string DefaultMainScript =
        "def main():\n    pass\n\n\nif __name__ == \"__main__\":\n    main()\n";

    private readonly IProjectStore store;

    public ProjectService(IProjectStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        this.store = store;
    }

    /// <summary>Creates a project in an empty folder and returns the opened workspace.</summary>
    /// <exception cref="ProjectFormatException">The folder cannot host a new project.</exception>
    public ProjectWorkspace Create(string rootPath, string? name = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        var root = Path.GetFullPath(rootPath);
        if (File.Exists(root))
            throw new ProjectFormatException("The selected path is a file, not a project folder.");

        var projectName = string.IsNullOrWhiteSpace(name) ? new DirectoryInfo(root).Name : name.Trim();
        if (string.IsNullOrWhiteSpace(projectName))
            throw new ProjectFormatException("The project name cannot be empty.");

        var manifest = new ProjectManifest(ProjectLayout.CurrentSchemaVersion, projectName, ProjectLayout.MainScriptRelativePath);
        store.CreateProject(root, manifest, DefaultMainScript);
        return ProjectWorkspace.FromManifest(root, manifest);
    }

    /// <summary>Opens an existing project folder, rejecting anything that is not a valid project.</summary>
    /// <exception cref="ProjectFormatException">The folder is not a valid project workspace.</exception>
    public ProjectWorkspace Open(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        var root = Path.GetFullPath(rootPath);
        if (!Directory.Exists(root))
            throw new ProjectFormatException("The selected folder does not exist.");

        var manifest = store.ReadManifest(root);
        if (!File.Exists(Path.Combine(root, ProjectLayout.MainScriptRelativePath.Replace('/', Path.DirectorySeparatorChar))))
            throw new ProjectFormatException(
                $"The project is missing its entry script: {ProjectLayout.MainScriptRelativePath}.");

        store.EnsureLayout(root);
        return ProjectWorkspace.FromManifest(root, manifest);
    }

    /// <summary>Checks whether a folder is a valid project without mutating it.</summary>
    public bool IsProject(string? rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            return false;
        try
        {
            var root = Path.GetFullPath(rootPath);
            if (!Directory.Exists(root))
                return false;

            store.ReadManifest(root);
            return File.Exists(Path.Combine(root, ProjectLayout.MainScriptRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        }
        catch (Exception ex) when (ex is ProjectFormatException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public string LoadMainScript(ProjectWorkspace project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return store.ReadMainScript(project.RootPath);
    }

    public void SaveMainScript(ProjectWorkspace project, string content)
    {
        ArgumentNullException.ThrowIfNull(project);
        store.WriteMainScript(project.RootPath, content);
    }
}
