namespace VisualizedAgentCoding.Infrastructure.Python;

public sealed record PythonBridgeOptions
{
    /// <summary>Executable path or command name, never a shell command.</summary>
    public string PythonExecutable { get; init; } =
        System.Environment.GetEnvironmentVariable("VISUALIZED_AGENT_PYTHON") ?? "python";
    public string HelperDirectory { get; init; } = Path.Combine(AppContext.BaseDirectory, "python-helper");
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public int[] ProtocolVersions { get; init; } = [1];
}
