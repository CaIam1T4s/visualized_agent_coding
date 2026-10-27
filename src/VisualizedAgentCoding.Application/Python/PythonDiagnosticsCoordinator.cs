using VisualizedAgentCoding.Application.Ports;

namespace VisualizedAgentCoding.Application.Python;

public sealed record PythonAnalysisUpdate(PythonParseRequest? Document, PythonParseResult? Result, string Status);

/// <summary>Debounce and latest-document protection, independent of any UI framework.</summary>
public sealed class PythonDiagnosticsCoordinator : IAsyncDisposable
{
    private readonly IPythonBridge bridge;
    private readonly Action<PythonAnalysisUpdate> publish;
    private readonly Action<Action> dispatch;
    private readonly TimeSpan debounce;
    private CancellationTokenSource? pending;
    private long generation;
    private int disposed;

    public PythonDiagnosticsCoordinator(IPythonBridge bridge, Action<PythonAnalysisUpdate> publish,
        TimeSpan? debounce = null, Action<Action>? dispatch = null)
    {
        this.bridge = bridge;
        this.publish = publish;
        this.debounce = debounce ?? TimeSpan.FromMilliseconds(350);
        this.dispatch = dispatch ?? (action => action());
    }

    // Update and Dispose are called by the owning editor thread. Async completions may run elsewhere.
    public Task UpdateAsync(PythonParseRequest? document)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        var revision = Interlocked.Increment(ref generation);
        pending?.Cancel();
        pending?.Dispose();
        pending = new CancellationTokenSource();
        var token = pending.Token;
        Publish(revision, new(document, null, document is null ? string.Empty : "语法检查中…"));
        return document is null ? Task.CompletedTask : AnalyzeAsync(document, revision, token);
    }

    private async Task AnalyzeAsync(PythonParseRequest document, long revision, CancellationToken token)
    {
        try
        {
            await Task.Delay(debounce, token).ConfigureAwait(false);
            var result = await bridge.ParseAsync(document, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (result.Uri != document.Uri || result.Version != document.Version)
                throw new PythonBridgeException("诊断结果的文档版本不匹配。");
            Publish(revision, new(document, result,
                result.Diagnostics.Length == 0 ? "语法正常" : $"{result.Diagnostics.Length} 个语法错误"));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            Publish(revision, new(document, null, $"语法检查不可用：{ex.Message}"));
        }
    }

    private void Publish(long revision, PythonAnalysisUpdate update) => dispatch(() =>
    {
        // Check again on the UI thread, not just before posting to its queue.
        if (Volatile.Read(ref disposed) == 0 && Interlocked.Read(ref generation) == revision)
            publish(update);
    });

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        Interlocked.Increment(ref generation);
        pending?.Cancel();
        pending?.Dispose();
        await bridge.DisposeAsync().ConfigureAwait(false);
    }
}
