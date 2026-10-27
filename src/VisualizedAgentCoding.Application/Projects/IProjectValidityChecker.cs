namespace VisualizedAgentCoding.Application.Projects;

public interface IProjectValidityChecker
{
    bool IsProject(string? rootPath);
}
