using System.Diagnostics;
using System.Text.Json;
using VisualizedAgentCoding.Application.Ports;
using VisualizedAgentCoding.Application.Runtime;

namespace VisualizedAgentCoding.Infrastructure.Python;

public sealed class PythonEnvironment : IPythonEnvironment
{
    public static string DefaultExecutable => System.Environment.GetEnvironmentVariable("VISUALIZED_AGENT_PYTHON") ?? "python";

    public async Task<PythonEnvironmentInfo> ValidateAsync(string executable, CancellationToken token = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var start = new ProcessStartInfo(executable.Trim())
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        foreach (var arg in new[] { "-I", "-B", "-c", "import sys,json;print(json.dumps({'executable':sys.executable,'version':'.'.join(map(str,sys.version_info[:3])),'supported':sys.version_info >= (3,10)}))" })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("无法启动 Python。");
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var stderr = await error;
            if (process.ExitCode != 0) throw new IOException("Python 验证失败：" + stderr);
            using var json = JsonDocument.Parse(await output);
            if (!json.RootElement.GetProperty("supported").GetBoolean()) throw new IOException("需要 Python 3.10 或更高版本。");
            return new(json.RootElement.GetProperty("executable").GetString()!, json.RootElement.GetProperty("version").GetString()!);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new IOException("Python 验证超时，请选择有效的 Python 可执行文件。"); }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        }
    }
}
