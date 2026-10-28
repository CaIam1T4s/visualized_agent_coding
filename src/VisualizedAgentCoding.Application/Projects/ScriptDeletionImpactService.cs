using VisualizedAgentCoding.Application.Ports;
using VisualizedAgentCoding.Application.Python;

namespace VisualizedAgentCoding.Application.Projects;

public enum DeletionImpactState { Complete, Incomplete, Failed, Stale }
public sealed record ScriptDeletionImpact(ProjectWorkspace Project, long Version, PythonReferenceLocation[] References,
    PythonReferenceIssue[] Issues, DeletionImpactState State, string? Error = null)
{
    public int AffectedScriptCount => References.Select(item => item.Uri).Distinct(StringComparer.OrdinalIgnoreCase).Count();
    public bool RequiresOverride => State != DeletionImpactState.Complete || References.Length > 0;
    public ScriptDeletionImpact ForCurrent(ProjectWorkspace? project, long version)
        => ReferenceEquals(Project, project) && Version == version ? this
            : this with { References = [], State = DeletionImpactState.Stale, Error = "项目内容已变化，请重新检查引用。" };
}

/// <summary>Read-only deletion preflight. Failure means unknown impact, never zero dependencies.</summary>
public sealed class ScriptDeletionImpactService(IPythonBridge bridge, IProjectStore store)
{
    private readonly ProjectSourceService sources = new(store);
    public async Task<ScriptDeletionImpact> AnalyzeAsync(ProjectWorkspace project, string scriptId, long version,
        IReadOnlyList<PythonSourceDocument> editorDocuments, CancellationToken token = default)
    {
        try
        {
            var script = project.FindScript(scriptId) ?? throw new ArgumentException("Script is not indexed.");
            var uri = new Uri(project.ToAbsolutePath(script.Path)).AbsoluteUri;
            var documents = sources.Collect(project, editorDocuments, token);
            var result = await bridge.FindModuleDependenciesAsync(new(project.RootPath, uri, version, documents), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (result.Uri != uri || result.Version != version) throw new PythonBridgeException("Dependency result does not match the requested document.");
            return new(project, version, result.References, result.Issues,
                result.IsIncomplete ? DeletionImpactState.Incomplete : DeletionImpactState.Complete);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) { return new(project, version, [], [], DeletionImpactState.Failed, ex.Message); }
    }
}
