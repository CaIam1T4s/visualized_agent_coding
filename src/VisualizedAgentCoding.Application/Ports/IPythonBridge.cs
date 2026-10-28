using VisualizedAgentCoding.Application.Python;
using VisualizedAgentCoding.Application.Editor.ObjectScripts;

namespace VisualizedAgentCoding.Application.Ports;

/// <summary>Static Python analysis only; never executes the submitted source.</summary>
public interface IPythonBridge : IAsyncDisposable
{
    Task<PythonHelperInfo> InitializeAsync(CancellationToken cancellationToken = default);
    Task<PythonParseResult> ParseAsync(PythonParseRequest request, CancellationToken cancellationToken = default);
    Task<ObjectScriptInspectionResult> InspectObjectScriptAsync(ObjectScriptInspectionRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Object script inspection is unavailable.");
    Task<PythonModuleRenameResult> PlanModuleRenameAsync(PythonModuleRenameRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Module rename analysis is unavailable.");
    Task<PythonReferencesResult> FindModuleDependenciesAsync(PythonModuleDependenciesRequest request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Module dependency analysis is unavailable.");
    Task<PythonReferencesResult> FindReferencesAsync(PythonReferencesRequest request, CancellationToken cancellationToken = default);
}
