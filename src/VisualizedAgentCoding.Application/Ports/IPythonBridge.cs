using VisualizedAgentCoding.Application.Python;

namespace VisualizedAgentCoding.Application.Ports;

/// <summary>Syntax inspection only; never executes the submitted source.</summary>
public interface IPythonBridge : IAsyncDisposable
{
    Task<PythonHelperInfo> InitializeAsync(CancellationToken cancellationToken = default);
    Task<PythonParseResult> ParseAsync(PythonParseRequest request, CancellationToken cancellationToken = default);
}
