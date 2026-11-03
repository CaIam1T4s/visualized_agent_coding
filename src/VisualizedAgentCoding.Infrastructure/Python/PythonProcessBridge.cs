using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VisualizedAgentCoding.Application.Ports;
using VisualizedAgentCoding.Application.Python;
using VisualizedAgentCoding.Application.Editor.ObjectScripts;

namespace VisualizedAgentCoding.Infrastructure.Python;

/// <summary>A single lazy helper process. Requests are serialized; failed requests are never replayed.</summary>
public sealed class PythonProcessBridge : IPythonBridge
{
    private const int MaxMessageBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.Strict,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };
    private readonly PythonBridgeOptions options;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private Process? process;
    private Stream? output;
    private readonly byte[] receiveBuffer = new byte[4096];
    private int receiveOffset;
    private int receiveCount;
    private Task? stderrTask;
    private readonly object stderrLock = new();
    private string stderrTail = string.Empty;
    private PythonHelperInfo? info;
    private long nextId;
    private int disposed;

    public PythonProcessBridge(PythonBridgeOptions? options = null)
    {
        this.options = options ?? new PythonBridgeOptions();
        if (this.options.RequestTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options));
        if (this.options.ProtocolVersions.Length == 0) throw new ArgumentException("No supported protocol versions.", nameof(options));
    }

    public Task<PythonHelperInfo> InitializeAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(async token => { await EnsureStartedAsync(token).ConfigureAwait(false); return info!; }, cancellationToken);

    public Task<PythonParseResult> ParseAsync(PythonParseRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Uri);
        ArgumentNullException.ThrowIfNull(request.Source);
        if (request.Version < 0) throw new ArgumentOutOfRangeException(nameof(request));
        return ExecuteAsync(async token =>
        {
            await EnsureStartedAsync(token).ConfigureAwait(false);
            var result = await SendAsync<PythonParseResult>("python.parse", request, token).ConfigureAwait(false);
            ValidateResult(request, result);
            return result;
        }, cancellationToken);
    }

    public Task<PythonNodeTree> BuildNodeTreeAsync(PythonNodeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Source);
        return ExecuteAsync(async token =>
        {
            await EnsureStartedAsync(token).ConfigureAwait(false);
            var supportsNodes = info!.Capabilities.Contains("nodes");
            if (!supportsNodes && !info!.Capabilities.Contains("blocks"))
                throw new PythonBridgeException("Python Helper 不支持节点投影。");
            var method = supportsNodes ? "python.nodes" : "python.blocks";
            return await SendAsync<PythonNodeTree>(method, new { source = request.Source, mode = request.Mode }, token).ConfigureAwait(false);
        }, cancellationToken);
    }

    public Task<VisualizedAgentCoding.Application.Editor.ScriptImportsResult> InspectImportsAsync(
        VisualizedAgentCoding.Application.Editor.ScriptImportsRequest request, CancellationToken cancellationToken = default)
        => ExecuteAsync(async token =>
        {
            await EnsureStartedAsync(token).ConfigureAwait(false);
            if (!info!.Capabilities.Contains("imports")) throw new PythonBridgeException("Python Helper 不支持导入管理。");
            var result = await SendAsync<VisualizedAgentCoding.Application.Editor.ScriptImportsResult>("python.imports", request, token).ConfigureAwait(false);
            if (result.Uri != request.Uri || result.Version != request.Version)
                throw new PythonBridgeException("导入分析返回了过期文档。");
            return result;
        }, cancellationToken);

    public Task<ObjectScriptInspectionResult> InspectObjectScriptAsync(ObjectScriptInspectionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Version < 0) throw new ArgumentOutOfRangeException(nameof(request));
        return ExecuteAsync(async token =>
        {
            await EnsureStartedAsync(token).ConfigureAwait(false);
            if (!info!.Capabilities.Contains("inspectObjectScript")) throw new PythonBridgeException("Python Helper 不支持对象脚本区域识别。");
            var result = await SendAsync<ObjectScriptInspectionResult>("python.inspectObjectScript", request, token).ConfigureAwait(false);
            if (result.Uri != request.Uri || result.Version != request.Version)
                throw new PythonBridgeException("对象脚本识别返回了不匹配的文档版本。");
            return result;
        }, cancellationToken);
    }

    public Task<PythonReferencesResult> FindReferencesAsync(PythonReferencesRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Line < 1 || request.Column < 1) throw new ArgumentOutOfRangeException(nameof(request));
        return QueryReferencesAsync(request.ProjectRoot, request.Uri, request.Version, request.Documents,
            "python.findReferences", "findReferences", cancellationToken, request.Line, request.Column);
    }

    public Task<PythonReferencesResult> FindModuleDependenciesAsync(PythonModuleDependenciesRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return QueryReferencesAsync(request.ProjectRoot, request.Uri, request.Version, request.Documents,
            "python.findModuleDependencies", "findModuleDependencies", cancellationToken);
    }

    public async Task<PythonModuleRenameResult> PlanModuleRenameAsync(PythonModuleRenameRequest request, CancellationToken cancellationToken = default)
    {
        if (request.Version < 0) throw new ArgumentOutOfRangeException(nameof(request));
        using var snapshot = await PythonSourceSnapshot.CreateAsync(request.ProjectRoot, request.Uri, request.Documents, cancellationToken).ConfigureAwait(false);
        var uri = snapshot.ToSnapshotUri(request.Uri);
        var result = await ExecuteAsync(async token =>
        {
            await EnsureStartedAsync(token).ConfigureAwait(false);
            if (!info!.Capabilities.Contains("planModuleRename")) throw new PythonBridgeException("Python Helper 不支持重命名预览。");
            var response = await SendAsync<PythonModuleRenameResult>("python.planModuleRename",
                new { projectRoot = snapshot.Root, uri, request.Version, request.NewName,
                    request.OldClassName, request.NewClassName }, token).ConfigureAwait(false);
            if (response.Uri != uri || response.Version != request.Version) throw new PythonBridgeException("重命名结果版本不匹配。");
            return response;
        }, cancellationToken, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        return result with { Uri = request.Uri,
            Changes = result.Changes.Select(change => change with { Uri = snapshot.ToOriginalUri(change.Uri) }).ToArray(),
            Issues = result.Issues.Select(issue => issue with { Uri = snapshot.ToOriginalUri(issue.Uri) }).ToArray() };
    }

    private async Task<PythonReferencesResult> QueryReferencesAsync(string projectRoot, string originalUri, long version,
        PythonSourceDocument[] documents, string method, string capability, CancellationToken cancellationToken,
        int? line = null, int? column = null)
    {
        if (version < 0) throw new ArgumentOutOfRangeException(nameof(version));
        using var snapshot = await PythonSourceSnapshot.CreateAsync(projectRoot, originalUri, documents, cancellationToken).ConfigureAwait(false);
        var uri = snapshot.ToSnapshotUri(originalUri);
        var result = await ExecuteAsync(async token =>
        {
            await EnsureStartedAsync(token).ConfigureAwait(false);
            if (!info!.Capabilities.Contains(capability)) throw new PythonBridgeException("Python Helper 不支持引用查询。");
            var parameters = new Dictionary<string, object> { ["projectRoot"] = snapshot.Root, ["uri"] = uri, ["version"] = version };
            if (line.HasValue) { parameters["line"] = line.Value; parameters["column"] = column!.Value; }
            var response = await SendAsync<PythonReferencesResult>(method, parameters, token).ConfigureAwait(false);
            if (response.Uri != uri || response.Version != version)
                throw new PythonBridgeException("引用查询返回了不匹配的文档版本。");
            foreach (var location in response.References)
                if (location.Range.Line < 1 || location.Range.Column < 1 || location.Range.EndLine != location.Range.Line
                    || location.Range.EndColumn <= location.Range.Column)
                    throw new PythonBridgeException("引用查询返回了无效位置。");
            return response;
        }, cancellationToken, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        return result with
        {
            Uri = originalUri,
            References = result.References.Select(location => location with { Uri = snapshot.ToOriginalUri(location.Uri) }).ToArray(),
            Issues = result.Issues.Select(issue => issue with { Uri = snapshot.ToOriginalUri(issue.Uri) }).ToArray(),
        };
    }

    private async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken caller, TimeSpan? timeout = null)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        using var deadline = new CancellationTokenSource(timeout ?? options.RequestTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, deadline.Token, lifetime.Token);
        var acquired = false;
        try
        {
            await gate.WaitAsync(linked.Token).ConfigureAwait(false);
            acquired = true;
            linked.Token.ThrowIfCancellationRequested();
            return await action(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !caller.IsCancellationRequested && !lifetime.IsCancellationRequested)
        {
            if (acquired) await ResetProcessAsync().ConfigureAwait(false);
            throw new TimeoutException($"Python Helper 请求超时（{(timeout ?? options.RequestTimeout).TotalSeconds:0.##} 秒）。");
        }
        catch (Exception ex)
        {
            var details = GetStderr();
            if (acquired) await ResetProcessAsync().ConfigureAwait(false);
            if (ex is OperationCanceledException or PythonBridgeException) throw;
            if (ex is IOException or JsonException or Win32Exception or InvalidOperationException or DecoderFallbackException)
                throw new PythonBridgeException($"Python Helper 通信失败：{ex.Message}{details}", ex);
            throw;
        }
        finally
        {
            if (acquired) gate.Release();
        }
    }

    private async Task EnsureStartedAsync(CancellationToken token)
    {
        if (process is { HasExited: false } && info is not null) return;
        await ResetProcessAsync().ConfigureAwait(false);
        var helper = Path.Combine(Path.GetFullPath(options.HelperDirectory), "main.py");
        if (!File.Exists(helper)) throw new PythonBridgeException($"找不到 Python Helper：{helper}");
        var start = new ProcessStartInfo(options.PythonExecutable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(helper)!,
        };
        // Ignore Python environment overrides and user site packages; do not write bytecode caches.
        foreach (var argument in new[] { "-E", "-s", "-B", "-u", helper }) start.ArgumentList.Add(argument);
        process = Process.Start(start) ?? throw new PythonBridgeException("无法启动 Python Helper。");
        output = process.StandardOutput.BaseStream;
        receiveOffset = receiveCount = 0;
        lock (stderrLock) stderrTail = string.Empty;
        stderrTask = DrainStderrAsync(process.StandardError);
        info = await SendAsync<PythonHelperInfo>("helper.hello", new { protocolVersions = options.ProtocolVersions }, token).ConfigureAwait(false);
        if (!options.ProtocolVersions.Contains(info.ProtocolVersion) || info.ProtocolVersion != 1 ||
            string.IsNullOrWhiteSpace(info.PythonVersion) || !info.Capabilities.Contains("parse"))
            throw new PythonBridgeException("Python Helper 协议版本或解析能力不兼容。");
    }

    private async Task<T> SendAsync<T>(string method, object parameters, CancellationToken token)
    {
        var id = (++nextId).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var message = JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters }, JsonOptions);
        if (Encoding.UTF8.GetByteCount(message) + 1 > MaxMessageBytes)
            throw new PythonBridgeException("Python Helper 消息超过 8 MiB 上限。");
        await process!.StandardInput.WriteAsync((message + "\n").AsMemory(), token).ConfigureAwait(false);
        await process.StandardInput.FlushAsync(token).ConfigureAwait(false);
        var line = await ReadBoundedLineAsync(token).ConfigureAwait(false);
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("jsonrpc", out var rpc) || rpc.GetString() != "2.0" ||
            !root.TryGetProperty("id", out var responseId) || responseId.ValueKind != JsonValueKind.String || responseId.GetString() != id)
            throw new PythonBridgeException("Python Helper 响应格式或请求 ID 不匹配。");
        var hasError = root.TryGetProperty("error", out var error);
        var hasResult = root.TryGetProperty("result", out var result);
        if (hasError == hasResult) throw new PythonBridgeException("Python Helper 必须返回 result 或 error。");
        if (hasError)
        {
            if (!error.TryGetProperty("code", out var code) || !code.TryGetInt32(out var number) ||
                !error.TryGetProperty("message", out var text) || text.ValueKind != JsonValueKind.String)
                throw new PythonBridgeException("Python Helper 错误响应格式无效。");
            throw new PythonBridgeException($"Python Helper：{text.GetString()}", errorCode: number);
        }
        return result.Deserialize<T>(JsonOptions) ?? throw new PythonBridgeException("Python Helper 返回空结果。");
    }

    private async Task<string> ReadBoundedLineAsync(CancellationToken token)
    {
        using var message = new MemoryStream();
        while (true)
        {
            if (receiveOffset == receiveCount)
            {
                receiveCount = await output!.ReadAsync(receiveBuffer, token).ConfigureAwait(false);
                receiveOffset = 0;
                if (receiveCount == 0) throw new IOException($"Python Helper 已退出或关闭输出。{GetStderr()}");
            }
            var newline = Array.IndexOf(receiveBuffer, (byte)'\n', receiveOffset, receiveCount - receiveOffset);
            var end = newline < 0 ? receiveCount : newline;
            var count = end - receiveOffset;
            if (message.Length + count + 1 > MaxMessageBytes)
                throw new PythonBridgeException("Python Helper 响应超过 8 MiB 上限。");
            message.Write(receiveBuffer, receiveOffset, count);
            receiveOffset = end;
            if (newline >= 0)
            {
                receiveOffset++;
                return new UTF8Encoding(false, true).GetString(message.GetBuffer(), 0, (int)message.Length);
            }
        }
    }

    private static void ValidateResult(PythonParseRequest request, PythonParseResult result)
    {
        if (result.Uri != request.Uri || result.Version != request.Version ||
            result.Diagnostics.Any(d => d is null || d.Line < 1 || d.Column < 1 || string.IsNullOrWhiteSpace(d.Message)))
            throw new PythonBridgeException("Python Helper 返回了不匹配的文档或无效的诊断位置。");
        if (result.Symbols is not { } symbols)
        {
            if (result.Diagnostics.Length == 0) throw new PythonBridgeException("Python Helper 缺少解析结果。");
            return;
        }
        static bool InvalidRange(PythonSourceRange r) => r.Line < 1 || r.Column < 1 || r.EndLine < r.Line ||
            r.EndColumn < 1 || (r.EndLine == r.Line && r.EndColumn < r.Column);
        if (symbols.Classes.Concat(symbols.Functions).Any(s => s is null || InvalidRange(s.Range)) ||
            symbols.Imports.Any(s => s is null || InvalidRange(s.Range)))
            throw new PythonBridgeException("Python Helper 返回了无效的源码范围。");
    }

    private async Task DrainStderrAsync(StreamReader reader)
    {
        var buffer = new char[1024];
        try
        {
            int count;
            while ((count = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
                lock (stderrLock)
                {
                    stderrTail += new string(buffer, 0, count);
                    if (stderrTail.Length > 4096) stderrTail = stderrTail[^4096..];
                }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
    }

    private string GetStderr()
    {
        lock (stderrLock) return string.IsNullOrWhiteSpace(stderrTail) ? string.Empty : $"\n{stderrTail.Trim()}";
    }

    private async Task ResetProcessAsync()
    {
        var previous = process;
        process = null;
        info = null;
        if (previous is null) return;
        try
        {
            if (!previous.HasExited) previous.Kill(entireProcessTree: true);
            await previous.WaitForExitAsync().ConfigureAwait(false);
            if (stderrTask is not null) await stderrTask.ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
        finally
        {
            output?.Dispose();
            output = null;
            previous.Dispose();
            stderrTask = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lifetime.Cancel();
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (process is { HasExited: false } && info is not null)
            {
                using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                try { await SendAsync<JsonElement>("helper.shutdown", new { }, shutdown.Token).ConfigureAwait(false); }
                catch (Exception ex) when (ex is OperationCanceledException or IOException or PythonBridgeException or JsonException or InvalidOperationException) { }
            }
            await ResetProcessAsync().ConfigureAwait(false);
        }
        finally { gate.Release(); }
    }
}
