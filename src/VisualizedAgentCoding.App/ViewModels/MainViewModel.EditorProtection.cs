using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;
using VisualizedAgentCoding.Application.Python;
using VisualizedAgentCoding.Application.Editor.ObjectScripts;
using VisualizedAgentCoding.Domain.Projects;

namespace happy_agent_coding.ViewModels;

public partial class MainViewModel
{
    private ObjectScriptDocumentService? objectScriptDocumentService;
    public long EditorSourceRevision { get; private set; }
    private IEditorProjection? editorProjection;
    public void AddInitializeRegion()
    {
        if (editorProjection is not MultiRegionProjection multi || OpenScript is not { Kind: not ProjectScriptKind.User } script
            || multi.Document.ScriptId != script.Id || multi.FullSource != PythonCode) return;
        var updated = ObjectScriptFeatures.AddInitialize(multi.Document);
        if (ReferenceEquals(updated, multi.Document)) return;
        RestoreEditorStructure(new MultiRegionProjection(updated));
        NotifyEditorSourceChanged();
    }
    /// <summary>Restore only editor-owned, trusted structural snapshots; disk baseline remains independent.</summary>
    public void RestoreEditorStructure(IEditorProjection restored)
    {
        if (activeProject is not { } project || OpenScript is not { Kind: not ProjectScriptKind.User } script
            || loadedScriptId != script.Id || restored.Document.ScriptId != script.Id
            || restored.Document.Uri != new System.Uri(project.ToAbsolutePath(script.Path)).AbsoluteUri)
            throw new System.InvalidOperationException("结构操作的脚本已改变，不能恢复到当前编辑器。");
        hasStructuralEdit = true;
        SetEditorProjection(restored);
        PythonCode = restored.FullSource;
    }
    public IEnumerable<PythonDiagnostic> EditorDiagnostics => ObjectScriptDiagnostics.Present(PythonDiagnostics, CurrentEditorProjection);

    public void SetEditorProjection(IEditorProjection? projection)
    {
        editorProjection = projection;
        editorUserProjection = null;
        NotifyEditorDiagnosticsChanged();
        if (projection is not null && projection.FullSource == PythonCode) SchedulePythonAnalysis();
    }
    /// <summary>Inspect the opened snapshot, never reinterpret a partially edited user region.</summary>
    public async Task<ObjectScriptDocument?> InspectEditorProtectionAsync(CancellationToken token)
    {
        if (activeProject is not { } project || OpenScript is not { } script
            || script.Kind == ProjectScriptKind.User || loadedScriptId != script.Id
            || loadedScriptSource is not { } source || objectScriptDocumentService is null) return null;
        var currentSource = PythonCode;
        if (editorProjection is { } trusted && trusted.Document.ScriptId == script.Id
            && trusted.Document.Uri == new System.Uri(project.ToAbsolutePath(script.Path)).AbsoluteUri
            && trusted.FullSource == currentSource) return trusted.Document;
        var document = savedObjectDocument ?? await objectScriptDocumentService.InspectAsync(
            project, script.Id, pythonDocumentVersion, source, token);
        if (!document.HasUserRegion || document.FullSource == currentSource) return document;
        var region = document.UserRegion!;
        var prefix = document.FullSource[..region.Offset];
        var suffix = document.FullSource[(region.Offset + region.Length)..];
        if (currentSource.Length < prefix.Length + suffix.Length
            || !currentSource.StartsWith(prefix, System.StringComparison.Ordinal)
            || !currentSource.EndsWith(suffix, System.StringComparison.Ordinal)) return null;
        return document with { FullSource = currentSource,
            UserRegion = new(region.Offset, currentSource.Length - prefix.Length - suffix.Length),
            Regions = document.Regions?.Select(r => r with { Span = new(region.Offset, currentSource.Length - prefix.Length - suffix.Length) }).ToArray() };
    }

    private void NotifyEditorSourceChanged()
    {
        EditorSourceRevision++;
        OnPropertyChanged(nameof(EditorSourceRevision));
    }
}
