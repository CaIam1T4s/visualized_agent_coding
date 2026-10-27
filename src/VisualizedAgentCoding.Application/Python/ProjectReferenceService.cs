using VisualizedAgentCoding.Application.Ports;
using VisualizedAgentCoding.Application.Projects;

namespace VisualizedAgentCoding.Application.Python;

/// <summary>Builds an on-demand source snapshot. Editor overlays always win, even when empty.</summary>
public sealed class ProjectReferenceService(IPythonBridge bridge, IProjectStore store)
{
    public Task<PythonReferencesResult> FindAsync(ProjectWorkspace project, string uri, long version,
        int line, int column, IReadOnlyList<PythonSourceDocument> editorDocuments, CancellationToken token = default)
    {
        var root = Path.GetFullPath(project.RootPath);
        var pending = project.Manifest.PendingScriptDeletions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var paths = project.Manifest.Scripts.Select(script => script.Path)
            .Concat(store.ListScriptFiles(root)).Append(project.Manifest.MainScript)
            .Concat(new[] { "runtime/element_base.py", "runtime/backdrop_base.py" }.Where(path => store.ScriptExists(root, path)))
            .Where(path => !pending.Contains(path)).Distinct(StringComparer.OrdinalIgnoreCase);
        var overlays = editorDocuments.ToDictionary(document => DocumentPath(root, document.Uri), StringComparer.OrdinalIgnoreCase);
        var documents = new Dictionary<string, PythonSourceDocument>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            token.ThrowIfCancellationRequested();
            var absolute = Path.GetFullPath(project.ToAbsolutePath(path));
            var documentUri = new Uri(absolute).AbsoluteUri;
            DocumentPath(root, documentUri);
            documents[absolute] = overlays.TryGetValue(absolute, out var overlay)
                ? overlay : new PythonSourceDocument(documentUri, 0, store.ReadScript(root, path));
        }
        foreach (var overlay in overlays) documents[overlay.Key] = overlay.Value;
        if (!documents.ContainsKey(DocumentPath(root, uri))) throw new ArgumentException("Target document is not part of the project.");
        return bridge.FindReferencesAsync(new(root, uri, version, line, column, documents.Values.ToArray()), token);
    }

    private static string DocumentPath(string root, string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || !parsed.IsFile)
            throw new ArgumentException("Expected a file URI.");
        var path = Path.GetFullPath(parsed.LocalPath);
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !path.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Document must be a Python file inside the project.");
        return path;
    }
}
