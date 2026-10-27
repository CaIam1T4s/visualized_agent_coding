using System.Threading.Channels;
using VisualizedAgentCoding.Application.Ports;
using VisualizedAgentCoding.Application.Python;

namespace VisualizedAgentCoding.App.Tests;

public sealed class PythonDiagnosticsCoordinatorTests
{
    private static PythonParseRequest Document(string uri, long version) => new(uri, version, "source remains intact");
    private static PythonParseResult Result(PythonParseRequest request, bool invalid = false) => new(request.Uri, request.Version,
        invalid ? [new("error", "syntax error", 1, 3)] : [], new([], [], []));

    [Fact]
    public async Task RapidEditsDebounceToOnlyTheLatestDocument()
    {
        var bridge = new ControlledBridge();
        var updates = new List<PythonAnalysisUpdate>();
        await using var coordinator = new PythonDiagnosticsCoordinator(bridge, updates.Add, TimeSpan.FromMilliseconds(50));
        var first = coordinator.UpdateAsync(Document("file:///a.py", 1));
        var second = coordinator.UpdateAsync(Document("file:///a.py", 2));
        var third = coordinator.UpdateAsync(Document("file:///a.py", 3));
        var request = await bridge.Next();
        Assert.Equal(3, request.Document.Version);
        request.Completion.SetResult(Result(request.Document));
        await Task.WhenAll(first, second, third);
        Assert.Equal(1, bridge.RequestCount);
        Assert.Equal("语法正常", updates.Last().Status);
    }

    [Fact]
    public async Task LateResultsFromPreviousFileCannotOverwriteCurrentDiagnostics()
    {
        var bridge = new ControlledBridge();
        var updates = new List<PythonAnalysisUpdate>();
        await using var coordinator = new PythonDiagnosticsCoordinator(bridge, updates.Add, TimeSpan.Zero);
        var oldTask = coordinator.UpdateAsync(Document("file:///a.py", 1));
        var old = await bridge.Next();
        var currentTask = coordinator.UpdateAsync(Document("file:///b.py", 2));
        var current = await bridge.Next();
        current.Completion.SetResult(Result(current.Document, true));
        await currentTask;
        old.Completion.SetResult(Result(old.Document)); // Fake deliberately ignores cancellation.
        await oldTask;
        Assert.Equal("file:///b.py", updates.Last().Document!.Uri);
        Assert.Single(updates.Last().Result!.Diagnostics);
    }

    [Fact]
    public async Task ClosingDocumentClearsDiagnosticsAndRejectsQueuedUiUpdates()
    {
        var bridge = new ControlledBridge();
        var updates = new List<PythonAnalysisUpdate>();
        var queue = new Queue<Action>();
        await using var coordinator = new PythonDiagnosticsCoordinator(bridge, updates.Add, TimeSpan.Zero, queue.Enqueue);
        var task = coordinator.UpdateAsync(Document("file:///a.py", 1));
        var request = await bridge.Next();
        request.Completion.SetResult(Result(request.Document, true));
        await task;
        await coordinator.UpdateAsync(null);
        while (queue.TryDequeue(out var action)) action();
        var cleared = Assert.Single(updates);
        Assert.Null(cleared.Document);
        Assert.Null(cleared.Result);
        Assert.Empty(cleared.Status);
    }

    [Fact]
    public async Task MissingHelperShowsErrorAndLaterInputCanRecover()
    {
        var bridge = new ControlledBridge();
        var updates = new List<PythonAnalysisUpdate>();
        await using var coordinator = new PythonDiagnosticsCoordinator(bridge, updates.Add, TimeSpan.Zero);
        var source = Document("file:///a.py", 1);
        var first = coordinator.UpdateAsync(source);
        (await bridge.Next()).Completion.SetException(new PythonBridgeException("missing python"));
        await first;
        Assert.Contains("missing python", updates.Last().Status);
        Assert.Equal("source remains intact", source.Source);
        var second = coordinator.UpdateAsync(source with { Version = 2 });
        var request = await bridge.Next();
        request.Completion.SetResult(Result(request.Document));
        await second;
        Assert.Equal("语法正常", updates.Last().Status);
    }

    [Fact]
    public async Task MismatchedVersionIsAnErrorAndDoesNotPublishDiagnostics()
    {
        var bridge = new ControlledBridge();
        var updates = new List<PythonAnalysisUpdate>();
        await using var coordinator = new PythonDiagnosticsCoordinator(bridge, updates.Add, TimeSpan.Zero);
        var task = coordinator.UpdateAsync(Document("file:///a.py", 1));
        var request = await bridge.Next();
        request.Completion.SetResult(Result(request.Document with { Version = 0 }));
        await task;
        Assert.Null(updates.Last().Result);
        Assert.Contains("版本不匹配", updates.Last().Status);
    }

    [Fact]
    public async Task DisposalSuppressesLateResultsAndDisposesBridge()
    {
        var bridge = new ControlledBridge();
        var updates = new List<PythonAnalysisUpdate>();
        var coordinator = new PythonDiagnosticsCoordinator(bridge, updates.Add, TimeSpan.Zero);
        var task = coordinator.UpdateAsync(Document("file:///a.py", 1));
        var request = await bridge.Next();
        await coordinator.DisposeAsync();
        var count = updates.Count;
        request.Completion.SetResult(Result(request.Document, true));
        await task;
        Assert.Equal(count, updates.Count);
        Assert.True(bridge.Disposed);
    }

    private sealed record Pending(PythonParseRequest Document, TaskCompletionSource<PythonParseResult> Completion);
    private sealed class ControlledBridge : IPythonBridge
    {
        private readonly Channel<Pending> requests = Channel.CreateUnbounded<Pending>();
        public int RequestCount { get; private set; }
        public bool Disposed { get; private set; }
        public Task<PythonHelperInfo> InitializeAsync(CancellationToken token = default) => Task.FromResult(new PythonHelperInfo(1, "test", ["parse"]));
        public Task<PythonParseResult> ParseAsync(PythonParseRequest document, CancellationToken token = default)
        {
            RequestCount++;
            var completion = new TaskCompletionSource<PythonParseResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            requests.Writer.TryWrite(new(document, completion));
            return completion.Task;
        }
        public async Task<Pending> Next() => await requests.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        public Task<PythonReferencesResult> FindReferencesAsync(PythonReferencesRequest request, CancellationToken token = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
