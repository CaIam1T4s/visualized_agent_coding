using System.Text.RegularExpressions;
using VisualizedAgentCoding.Application.Ports;
using VisualizedAgentCoding.Application.Python;
using VisualizedAgentCoding.Domain.Projects;

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
    private static readonly Regex BackdropCall = new(
        "\\bswitch_backdrop\\s*\\(\\s*(['\"])(?<name>[^'\"]*)\\1", RegexOptions.Compiled);
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
            var references = result.References.ToList();
            if (script.Kind == ProjectScriptKind.Backdrop && script.ObjectId is { } targetId)
                references.AddRange(FindBackdropReferences(script, targetId, uri, documents));
            return new(project, version, references.ToArray(), result.Issues,
                result.IsIncomplete ? DeletionImpactState.Incomplete : DeletionImpactState.Complete);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) { return new(project, version, [], [], DeletionImpactState.Failed, ex.Message); }
    }

    /// <summary>Finds string references to a backdrop that the Python helper cannot resolve.</summary>
    private static IEnumerable<PythonReferenceLocation> FindBackdropReferences(ProjectScriptEntry target,
        string targetId, string targetUri, IEnumerable<PythonSourceDocument> documents)
    {
        var name = Path.GetFileNameWithoutExtension(target.Path);
        var marker = BackdropSwitchEvents.Prefix + targetId;
        foreach (var document in documents)
        {
            if (string.Equals(document.Uri, targetUri, StringComparison.OrdinalIgnoreCase) || document.Source.Length == 0)
                continue;
            foreach (Match match in BackdropCall.Matches(document.Source))
            {
                if (match.Groups["name"].Value == name)
                    yield return Locate(document.Uri, document.Source, match.Index, match.Length, "switch_backdrop");
            }
            for (var index = document.Source.IndexOf(marker, StringComparison.Ordinal); index >= 0;
                 index = document.Source.IndexOf(marker, index + marker.Length, StringComparison.Ordinal))
                yield return Locate(document.Uri, document.Source, index, marker.Length, "backdrop-switch");
        }
    }

    private static PythonReferenceLocation Locate(string uri, string source, int index, int length, string name)
    {
        var line = 1;
        var lineStart = 0;
        for (var offset = 0; offset < index; offset++)
        {
            if (source[offset] != '\n') continue;
            line++;
            lineStart = offset + 1;
        }
        var column = index - lineStart + 1;
        return new(uri, new(line, column, line, column + length), false, name);
    }
}
