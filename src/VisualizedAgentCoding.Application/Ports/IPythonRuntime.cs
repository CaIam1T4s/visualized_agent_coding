using VisualizedAgentCoding.Application.Runtime;

namespace VisualizedAgentCoding.Application.Ports;

public interface IPythonEnvironment
{
    Task<PythonEnvironmentInfo> ValidateAsync(string executable, CancellationToken token = default);
}

/// <summary>One isolated execution session, separate from the static analysis bridge.</summary>
public interface IPythonRuntime : IAsyncDisposable
{
    Task<int> RunAsync(PythonRunRequest request, Action<PythonRunEvent> publish, CancellationToken token);
    Task SendInputAsync(string text, CancellationToken token = default);
}
