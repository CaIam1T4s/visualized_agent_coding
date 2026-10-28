using System.Threading;
using System.Threading.Tasks;
using VisualizedAgentCoding.Application.Editor.ObjectScripts;
using VisualizedAgentCoding.Domain.Projects;

namespace happy_agent_coding.ViewModels;

public partial class MainViewModel
{
    private ObjectScriptDocumentService? objectScriptDocumentService;
    public long EditorSourceRevision { get; private set; }

    /// <summary>Inspect the opened snapshot, never reinterpret a partially edited user region.</summary>
    public async Task<ObjectScriptDocument?> InspectEditorProtectionAsync(CancellationToken token)
    {
        if (activeProject is not { } project || OpenScript is not { } script
            || script.Kind == ProjectScriptKind.User || loadedScriptId != script.Id
            || loadedScriptSource is not { } source || objectScriptDocumentService is null) return null;
        var currentSource = PythonCode;
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
            UserRegion = new(region.Offset, currentSource.Length - prefix.Length - suffix.Length) };
    }

    private void NotifyEditorSourceChanged()
    {
        EditorSourceRevision++;
        OnPropertyChanged(nameof(EditorSourceRevision));
    }
}
