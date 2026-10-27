using System.Collections.Generic;
using VisualizedAgentCoding.Domain.Projects;

namespace VisualizedAgentCoding.Application.Ports;

/// <summary>Reads and writes project manifests, entry script and indexed project scripts.</summary>
public interface IProjectStore
{
    /// <summary>Reads the manifest of an existing project root without validating project rules.</summary>
    /// <exception cref="ProjectFormatException">The folder has no readable manifest.</exception>
    ProjectManifest ReadManifest(string rootPath);

    /// <summary>Overwrites the manifest of an existing project.</summary>
    void WriteManifest(string rootPath, ProjectManifest manifest);

    /// <summary>Writes the manifest and the initial entry script of a new project.</summary>
    /// <exception cref="ProjectFormatException">A project already exists at the location.</exception>
    void CreateProject(string rootPath, ProjectManifest manifest, string mainScriptContent);

    string ReadMainScript(string rootPath);

    void WriteMainScript(string rootPath, string content);

    bool ScriptExists(string rootPath, string relativePath);

    string ReadScript(string rootPath, string relativePath);

    void WriteScript(string rootPath, string relativePath, string content);

    /// <summary>Project-relative paths of every <c>.py</c> file inside the script directories.</summary>
    IReadOnlyList<string> ListScriptFiles(string rootPath);

    /// <summary>Creates any missing directories so an existing project keeps the documented layout.</summary>
    void EnsureLayout(string rootPath);

    /// <summary>Deletes a project script file. The caller is responsible for removing its index entry.</summary>
    void DeleteScript(string rootPath, string relativePath);
}
