using System.Diagnostics;
using VisualizedAgentCoding.Application.Python;
using VisualizedAgentCoding.Infrastructure.Python;

namespace VisualizedAgentCoding.App.Tests;

public sealed class PythonProcessBridgeTests : IDisposable
{
    private readonly string temporary = Path.Combine(Path.GetTempPath(), $"python-bridge-{Guid.NewGuid():N}");
    private static string Python => System.Environment.GetEnvironmentVariable("VISUALIZED_AGENT_PYTHON") ?? "python";

    public PythonProcessBridgeTests()
    {
        Directory.CreateDirectory(temporary);
        File.WriteAllText(Path.Combine(temporary, "main.py"), FakeHelper);
    }

    private PythonProcessBridge Fake(TimeSpan? timeout = null) => new(new PythonBridgeOptions
    {
        PythonExecutable = Python, HelperDirectory = temporary,
        RequestTimeout = timeout ?? TimeSpan.FromSeconds(15),
    });
    private static PythonProcessBridge Real(int[]? versions = null) => new(new PythonBridgeOptions
    {
        PythonExecutable = Python, ProtocolVersions = versions ?? [1],
    });
    private static PythonParseRequest Request(string source, long version = 1) => new("file:///test.py", version, source);

    [Fact]
    public async Task RealHelperNegotiatesAndReturnsSymbolsAndUtf16Ranges()
    {
        await using var bridge = Real();
        var info = await bridge.InitializeAsync();
        Assert.Equal(1, info.ProtocolVersion);
        Assert.Contains("parse", info.Capabilities);
        var result = await bridge.ParseAsync(Request("\"😀\"; import os\nclass 甲: pass\nasync def f():\n    pass\n", 42));
        Assert.Equal(42, result.Version);
        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Symbols);
        Assert.Equal("甲", Assert.Single(result.Symbols.Classes).Name);
        Assert.Equal(14, result.Symbols.Classes[0].Range.EndColumn);
        Assert.Equal("f", Assert.Single(result.Symbols.Functions).Name);
        Assert.Equal(7, Assert.Single(result.Symbols.Imports).Range.Column);
        Assert.Equal("os", Assert.Single(result.Symbols.Imports[0].Names));
    }

    [Fact]
    public async Task RealHelperReturnsSyntaxErrorsAndCanParseAgain()
    {
        await using var bridge = Real();
        var invalid = await bridge.ParseAsync(Request("def f(:\n    pass"));
        Assert.Null(invalid.Symbols);
        Assert.Equal(1, Assert.Single(invalid.Diagnostics).Line);
        Assert.True(invalid.Diagnostics[0].Column > 0);
        Assert.Empty((await bridge.ParseAsync(Request("x = 1", 2))).Diagnostics);
    }

    [Fact]
    public async Task ParsingDoesNotExecuteSource()
    {
        await using var bridge = Real();
        var marker = Path.Combine(temporary, "executed").Replace('\\', '/');
        await bridge.ParseAsync(Request($"open('{marker}', 'w').write('executed')"));
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public async Task ConcurrentRequestsAreMatchedAndReuseOneProcess()
    {
        await using var bridge = Fake();
        var first = await bridge.InitializeAsync();
        var results = await Task.WhenAll(Enumerable.Range(1, 12).Select(i => bridge.ParseAsync(Request("ok", i))));
        Assert.Equal(Enumerable.Range(1, 12).Select(i => (long)i), results.Select(r => r.Version));
        Assert.Equal(first, await bridge.InitializeAsync());
    }

    [Fact]
    public async Task IncompatibleProtocolIsReported()
    {
        await using var bridge = Real([99]);
        var error = await Assert.ThrowsAsync<PythonBridgeException>(() => bridge.InitializeAsync());
        Assert.Equal(-32002, error.ErrorCode);
    }

    [Fact]
    public async Task MissingPythonAndMissingHelperAreReported()
    {
        await using var absentPython = new PythonProcessBridge(new PythonBridgeOptions
        {
            PythonExecutable = Path.Combine(temporary, "missing-python.exe"), HelperDirectory = temporary,
        });
        await Assert.ThrowsAsync<PythonBridgeException>(() => absentPython.InitializeAsync());
        await using var absentHelper = new PythonProcessBridge(new PythonBridgeOptions { HelperDirectory = Path.Combine(temporary, "missing") });
        await Assert.ThrowsAsync<PythonBridgeException>(() => absentHelper.InitializeAsync());
    }

    [Theory]
    [InlineData("crash")]
    [InlineData("bad-id")]
    [InlineData("bad-json")]
    [InlineData("bad-version")]
    [InlineData("missing-fields")]
    [InlineData("invalid-range")]
    [InlineData("huge-response")]
    [InlineData("error")]
    public async Task BrokenResponseFailsRequestAndNextCallRestarts(string mode)
    {
        await using var bridge = Fake();
        var before = await bridge.InitializeAsync();
        await Assert.ThrowsAsync<PythonBridgeException>(() => bridge.ParseAsync(Request(mode)));
        Assert.Empty((await bridge.ParseAsync(Request("ok", 2))).Diagnostics);
        var after = await bridge.InitializeAsync();
        Assert.NotEqual(before.PythonVersion, after.PythonVersion);
    }

    [Fact]
    public async Task StderrFloodIsDrainedWithoutBlockingProtocol()
    {
        await using var bridge = Fake();
        Assert.Empty((await bridge.ParseAsync(Request("stderr-flood"))).Diagnostics);
    }

    [Fact]
    public async Task TimeoutKillsProcessAndAllowsRestart()
    {
        await using var bridge = Fake(TimeSpan.FromSeconds(2));
        var info = await bridge.InitializeAsync();
        await Assert.ThrowsAsync<TimeoutException>(() => bridge.ParseAsync(Request("hang")));
        Assert.False(IsRunning(int.Parse(info.PythonVersion)));
        Assert.Empty((await bridge.ParseAsync(Request("ok"))).Diagnostics);
    }

    [Fact]
    public async Task ActiveCancellationKillsProcessButQueuedCancellationDoesNot()
    {
        await using var bridge = Fake();
        var info = await bridge.InitializeAsync();
        using var activeCancellation = new CancellationTokenSource();
        var active = bridge.ParseAsync(Request("hang"), activeCancellation.Token);
        await WaitForMarker();
        using var queuedCancellation = new CancellationTokenSource();
        var queued = bridge.ParseAsync(Request("ok", 2), queuedCancellation.Token);
        queuedCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        Assert.True(IsRunning(int.Parse(info.PythonVersion)));
        activeCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active);
        Assert.False(IsRunning(int.Parse(info.PythonVersion)));
        Assert.Empty((await bridge.ParseAsync(Request("ok", 3))).Diagnostics);
    }

    [Fact]
    public async Task DisposingAnActiveBridgeReapsItsProcessAndRejectsNewWork()
    {
        var bridge = Fake();
        var info = await bridge.InitializeAsync();
        var active = bridge.ParseAsync(Request("hang"));
        await WaitForMarker();
        await bridge.DisposeAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active);
        Assert.False(IsRunning(int.Parse(info.PythonVersion)));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => bridge.InitializeAsync());
        await bridge.DisposeAsync();
    }

    [Fact]
    public async Task OversizedRequestIsRejectedBeforeItCanHangTheHelper()
    {
        await using var bridge = Fake();
        await Assert.ThrowsAsync<PythonBridgeException>(() => bridge.ParseAsync(Request(new string('x', 8 * 1024 * 1024))));
        Assert.Empty((await bridge.ParseAsync(Request("ok"))).Diagnostics);
    }

    [Fact]
    public async Task IdleProcessExitIsDetectedAndNextRequestRestarts()
    {
        await using var bridge = Fake();
        var before = await bridge.InitializeAsync();
        using (var process = Process.GetProcessById(int.Parse(before.PythonVersion)))
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
        Assert.Empty((await bridge.ParseAsync(Request("ok"))).Diagnostics);
        Assert.NotEqual(before.PythonVersion, (await bridge.InitializeAsync()).PythonVersion);
    }

    [Fact]
    public async Task IdleDisposalRequestsGracefulShutdown()
    {
        var bridge = Fake();
        var info = await bridge.InitializeAsync();
        await bridge.DisposeAsync();
        Assert.True(File.Exists(Path.Combine(temporary, "shutdown")));
        Assert.False(IsRunning(int.Parse(info.PythonVersion)));
    }

    [Fact]
    public async Task RealHelperReturnsMultipleSyntaxErrorsWithOriginalCoordinates()
    {
        await using var bridge = Real();
        var result = await bridge.ParseAsync(Request("def first(:\n    pass\ndef second(:\n    pass\n"));
        Assert.Null(result.Symbols);
        Assert.Equal(new[] { 1, 3 }, result.Diagnostics.Select(error => error.Line));
        Assert.All(result.Diagnostics, error => Assert.True(error.Column > 0));
    }

    [Fact]
    public async Task CancellingReferenceQueryReapsProcessAndDeletesItsSourceSnapshot()
    {
        await using var bridge = Fake();
        await bridge.InitializeAsync();
        var uri = new Uri(Path.Combine(temporary, "unsaved.py")).AbsoluteUri;
        var request = new PythonReferencesRequest(temporary, uri, 1, 1, 1,
            new[] { new PythonSourceDocument(uri, 1, "hang") });
        using var cancellation = new CancellationTokenSource();
        var query = bridge.FindReferencesAsync(request, cancellation.Token);
        await WaitForMarker();
        var snapshot = File.ReadAllText(Path.Combine(temporary, "snapshot-root"));
        Assert.True(Directory.Exists(snapshot));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => query);
        Assert.False(Directory.Exists(snapshot));
        Assert.False(File.Exists(Path.Combine(temporary, "unsaved.py")));
    }

    private async Task WaitForMarker()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        while (!File.Exists(Path.Combine(temporary, "started"))) await Task.Delay(10, timeout.Token);
    }

    private static bool IsRunning(int id)
    {
        try { using var process = Process.GetProcessById(id); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }

    public void Dispose() => Directory.Delete(temporary, true);

    private const string FakeHelper = """
import json, os, sys, time
from pathlib import Path
from urllib.parse import urlparse
from urllib.request import url2pathname

def emit(id, result):
    print(json.dumps({'jsonrpc': '2.0', 'id': id, 'result': result}), flush=True)

for line in sys.stdin:
    req = json.loads(line)
    id = req['id']
    if req['method'] == 'helper.hello':
        emit(id, {'protocolVersion': 1, 'pythonVersion': str(os.getpid()), 'capabilities': ['parse', 'findReferences']})
        continue
    if req['method'] == 'helper.shutdown':
        open('shutdown', 'w').close()
        emit(id, {})
        break
    p = req['params']
    if req['method'] == 'python.findReferences':
        Path('snapshot-root').write_text(p['projectRoot'], encoding='utf-8')
        source = Path(url2pathname(urlparse(p['uri']).path)).read_text(encoding='utf-8')
        if source == 'hang':
            Path('started').touch()
            time.sleep(20)
        emit(id, {'uri': p['uri'], 'version': p['version'], 'references': [], 'issues': [], 'isIncomplete': False})
        continue
    mode = p['source']
    if mode == 'crash':
        sys.stderr.write('injected crash')
        sys.stderr.flush()
        os._exit(7)
    if mode == 'hang':
        open('started', 'w').close()
        time.sleep(20)
    if mode == 'bad-json':
        print('this is not JSON', flush=True)
        continue
    if mode == 'huge-response':
        sys.stdout.write('x' * (8 * 1024 * 1024 + 1))
        sys.stdout.flush()
        continue
    if mode == 'stderr-flood':
        sys.stderr.write('x' * (128 * 1024))
        sys.stderr.flush()
    if mode == 'error':
        print(json.dumps({'jsonrpc': '2.0', 'id': id, 'error': {'code': -32602, 'message': 'injected'}}), flush=True)
        continue
    symbols = {'classes': [], 'functions': [], 'imports': []}
    if mode == 'invalid-range':
        symbols['classes'] = [{'name': 'bad', 'range': {'line': 0, 'column': 1, 'endLine': 1, 'endColumn': 1}}]
    result = {'uri': p['uri'], 'version': p['version'], 'diagnostics': [], 'symbols': symbols}
    if mode == 'bad-version': result['version'] -= 1
    if mode == 'missing-fields': result = {'uri': p['uri']}
    emit('wrong-id' if mode == 'bad-id' else id, result)
""";
}
