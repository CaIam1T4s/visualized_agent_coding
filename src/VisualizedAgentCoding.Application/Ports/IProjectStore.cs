using VisualizedAgentCoding.Domain.Projects;

namespace VisualizedAgentCoding.Application.Ports;

/// <summary>Reads and writes project manifests and entry scripts on a storage medium.</summary>
public interface IProjectStore
{
    /// <summary>Reads and validates the manifest of an existing project root.</summary>
    /// <exception cref="ProjectFormatException">The folder is not a valid project.</exception>
    ProjectManifest ReadManifest(string rootPath);

    /// <summary>Writes the manifest and the initial entry script of a new project.</summary>
    /// <exception cref="ProjectFormatException">A project already exists at the location.</exception>
    void CreateProject(string rootPath, ProjectManifest manifest, string mainScriptContent);

    string ReadMainScript(string rootPath);

    void WriteMainScript(string rootPath, string content);

    /// <summary>Creates any missing directories so an existing project keeps the documented layout.</summary>
    void EnsureLayout(string rootPath);
}
