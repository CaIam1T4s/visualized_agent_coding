using VisualizedAgentCoding.Application.Ports;
using VisualizedAgentCoding.Application.Projects;

namespace VisualizedAgentCoding.Application.Python;

public sealed class ProjectReferenceService(IPythonBridge bridge, IProjectStore store)
{
    private readonly ProjectSourceService sources = new(store);
    public Task<PythonReferencesResult> FindAsync(ProjectWorkspace project, string uri, long version,
        int line, int column, IReadOnlyList<PythonSourceDocument> editorDocuments, CancellationToken token = default)
        => bridge.FindReferencesAsync(new(project.RootPath, uri, version, line, column,
            sources.Collect(project, editorDocuments, token)), token);
}
