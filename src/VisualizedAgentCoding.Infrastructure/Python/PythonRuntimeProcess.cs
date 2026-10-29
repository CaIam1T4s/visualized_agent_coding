using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using VisualizedAgentCoding.Application.Ports;
using VisualizedAgentCoding.Application.Runtime;

namespace VisualizedAgentCoding.Infrastructure.Python;

/// <summary>Raw stdio for user interaction; authenticated loopback channel for runtime events.</summary>
public sealed class PythonRuntimeProcess(string? runtimeDirectory = null) : IPythonRuntime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string runtimeDirectory = runtimeDirectory ?? Path.Combine(AppContext.BaseDirectory, "python-runtime");
    private readonly SemaphoreSlim inputGate = new(1, 1);
    private Process? process;
    private int running;
    private bool ready;

    public async Task<int> RunAsync(PythonRunRequest request, Action<PythonRunEvent> publish, CancellationToken token)
    {
        if (Interlocked.Exchange(ref running, 1) != 0) throw new InvalidOperationException("Python 已在运行。");
        var temporary = Path.Combine(Path.GetTempPath(), "vac-run-" + Guid.NewGuid().ToString("N"));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        Process? child = null;
        ExecutionJob? job = null;
        TcpClient? control = null;
        Task? stdout = null, stderr = null, events = null;
        try
        {
            Directory.CreateDirectory(temporary);
            listener.Start();
            var secret = Guid.NewGuid().ToString("N");
            var requestPath = Path.Combine(temporary, "request.json");
            await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(new { request, port = ((IPEndPoint)listener.LocalEndpoint).Port, secret }, Json), token);
            var start = new ProcessStartInfo(request.Executable)
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = request.Root,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (var arg in new[] { "-E", "-s", "-B", "-u", "-X", "utf8", Path.Combine(runtimeDirectory, "runner.py"), requestPath }) start.ArgumentList.Add(arg);
            child = Process.Start(start) ?? throw new IOException("无法启动 Python 执行进程。");
            process = child;
            job = new ExecutionJob(child);
            stdout = DrainAsync(child.StandardOutput, "stdout", publish);
            stderr = DrainAsync(child.StandardError, "stderr", publish);
            using var handshake = CancellationTokenSource.CreateLinkedTokenSource(token);
            handshake.CancelAfter(TimeSpan.FromSeconds(10));
            control = await listener.AcceptTcpClientAsync(handshake.Token);
            listener.Stop();
            var reader = new StreamReader(control.GetStream(), Encoding.UTF8);
            var hello = await reader.ReadLineAsync(handshake.Token);
            if (hello != secret) throw new IOException("Python 运行握手失败。");
            events = ReadEventsAsync(reader, publish);
            var exit = child.WaitForExitAsync(token);
            if (await Task.WhenAny(exit, events) == events) await events;
            await exit;
            job.Dispose();
            await Task.WhenAll(stdout, stderr, events);
            return child.ExitCode;
        }
        finally
        {
            ready = false;
            listener.Stop();
            if (child is not null && !child.HasExited)
            {
                // EOF lets waiting input() exit; interrupt requested loops via the control channel.
                try
                {
                    child.StandardInput.Close();
                    if (control?.Connected == true)
                        await control.GetStream().WriteAsync(Encoding.UTF8.GetBytes("stop\n"));
                    using var grace = new CancellationTokenSource(500);
                    await child.WaitForExitAsync(grace.Token);
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or InvalidOperationException or SocketException) { }
                if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); }
            }
            control?.Dispose();
            job?.Dispose();
            if (stdout is not null && stderr is not null)
            {
                try { await Task.WhenAll(stdout, stderr); } catch (IOException) { }
            }
            if (events is not null) { try { await events; } catch (Exception ex) when (ex is IOException or ObjectDisposedException) { } }
            process = null;
            child?.Dispose();
            // The path is a uniquely created child of the system temp directory, never a user path.
            if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
            Volatile.Write(ref running, 0);
        }
    }

    private async Task ReadEventsAsync(StreamReader reader, Action<PythonRunEvent> publish)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            var message = JsonSerializer.Deserialize<PythonRunEvent>(line, Json) ?? throw new IOException("无效运行事件。");
            if (message.ProtocolVersion != 2) throw new IOException("Python 运行协议版本不兼容。");
            if (message.Kind == "exception" && message.Exception is null) throw new IOException("Python 异常事件缺少结构化数据。");
            if (message.Kind == "finished") return;
            if (message.Kind == "ready") ready = true;
            publish(message);
        }
    }

    private static async Task DrainAsync(StreamReader reader, string kind, Action<PythonRunEvent> publish)
    {
        var buffer = new char[2048];
        int count;
        while ((count = await reader.ReadAsync(buffer)) > 0) publish(new(kind, new string(buffer, 0, count)));
    }

    public async Task SendInputAsync(string text, CancellationToken token = default)
    {
        await inputGate.WaitAsync(token);
        try
        {
            var child = process;
            if (!ready || child is null || child.HasExited) throw new IOException("Python 当前不可输入。");
            await child.StandardInput.WriteLineAsync(text.AsMemory(), token);
            await child.StandardInput.FlushAsync(token);
        }
        finally { inputGate.Release(); }
    }

    public ValueTask DisposeAsync()
    {
        if (process is { HasExited: false } child) child.Kill(entireProcessTree: true);
        return ValueTask.CompletedTask;
    }
}
