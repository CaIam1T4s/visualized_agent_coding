using VisualizedAgentCoding.Application.Editor.ObjectScripts;
using VisualizedAgentCoding.Application.Ports;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Application.Python;
using VisualizedAgentCoding.Domain.Projects;
using VisualizedAgentCoding.Infrastructure.Projects;
using VisualizedAgentCoding.Infrastructure.Python;

namespace VisualizedAgentCoding.App.Tests;

public sealed class ObjectScriptDocumentTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "object-doc-" + Guid.NewGuid().ToString("N"));
    private readonly ProjectService projects = new(new JsonProjectStore());

    [Theory]
    [InlineData(ProjectScriptKind.Object)]
    [InlineData(ProjectScriptKind.Backdrop)]
    public async Task NewTemplatesHaveEmptyUserRegionAndKeepCompleteValidSource(ProjectScriptKind kind)
    {
        var created = projects.CreateScript(projects.Create(root, "Demo"), kind, "hero");
        var source = projects.LoadScript(created.Project, created.Script);
        await using var bridge = new PythonProcessBridge();
        var document = await new ObjectScriptDocumentService(bridge).InspectAsync(created.Project, created.Script.Id, 7, source);
        Assert.Equal(ObjectScriptStatus.Recognized, document.Status);
        Assert.True(document.HasUserRegion);
        Assert.Equal("", document.UserSource);
        Assert.Equal(source, document.FullSource);
        Assert.Equal(created.Script.Id, document.ScriptId);
        Assert.Equal(7, document.Version);
        Assert.Equal(1, document.TemplateVersion);
        Assert.Empty((await bridge.ParseAsync(new(document.Uri, 7, source))).Diagnostics);
        Assert.Equal(source, projects.LoadScript(created.Project, created.Script));
    }

    [Fact]
    public async Task UnsavedUnicodeCrLfBufferIsInspectedWithoutSavingOrNormalization()
    {
        var created = projects.CreateScript(projects.Create(root, "Demo"), ProjectScriptKind.Object, "hero");
        var original = projects.LoadScript(created.Project, created.Script);
        var user = "    text = \"你好😀\"\n    def act(self):\n        return self.text\n";
        var source = original.Replace("    # vac:user-code-end", user + "    # vac:user-code-end")
            .Replace("    pass", "    pass  # 系统😀").Replace("\n", "\r\n");
        await using var bridge = new PythonProcessBridge();
        var document = await new ObjectScriptDocumentService(bridge).InspectAsync(created.Project, created.Script.Id, 11, source);
        Assert.Equal(ObjectScriptStatus.Recognized, document.Status);
        Assert.Equal(user.Replace("\n", "\r\n"), document.UserSource);
        var span = document.UserRegion!;
        Assert.Equal(source, source[..span.Offset] + document.UserSource + source[(span.Offset + span.Length)..]);
        Assert.Equal(source, document.FullSource);
        Assert.Equal(original, projects.LoadScript(created.Project, created.Script));
    }

    [Fact]
    public async Task UserSyntaxErrorsAreIndependentFromStructureAndNoCodeExecutes()
    {
        var created = projects.CreateScript(projects.Create(root, "Demo"), ProjectScriptKind.Object, "hero");
        var template = projects.LoadScript(created.Project, created.Script);
        var source = template.Replace("    # vac:user-code-end", "    value =\n    # vac:user-code-end");
        await using var bridge = new PythonProcessBridge();
        var service = new ObjectScriptDocumentService(bridge);
        var document = await service.InspectAsync(created.Project, created.Script.Id, 2, source);
        Assert.Equal(ObjectScriptStatus.Recognized, document.Status);
        Assert.NotEmpty((await bridge.ParseAsync(new(document.Uri, 2, source))).Diagnostics);
        var raising = template.Replace("    # vac:user-code-end", "    raise RuntimeError('must not run')\n    # vac:user-code-end");
        Assert.Equal(ObjectScriptStatus.Recognized, (await service.InspectAsync(created.Project, created.Script.Id, 3, raising)).Status);
    }

    [Theory]
    [InlineData("legacy", ObjectScriptStatus.LegacyUnmarked)]
    [InlineData("broken", ObjectScriptStatus.InvalidStructure)]
    [InlineData("version", ObjectScriptStatus.UnsupportedVersion)]
    public async Task UnrecognizedDocumentsPreserveSourceAndHaveNoEditableRegion(string mode, ObjectScriptStatus expected)
    {
        var created = projects.CreateScript(projects.Create(root, "Demo"), ProjectScriptKind.Object, "hero");
        var template = projects.LoadScript(created.Project, created.Script);
        var source = mode switch
        {
            "legacy" => "from runtime.element_base import ElementBase\nclass Hero(ElementBase):\n    pass\n",
            "broken" => template.Replace("class Hero(", "class Wrong("),
            _ => template.Replace("version=1", "version=2"),
        };
        await using var bridge = new PythonProcessBridge();
        var document = await new ObjectScriptDocumentService(bridge).InspectAsync(created.Project, created.Script.Id, 1, source);
        Assert.Equal(expected, document.Status);
        Assert.Equal(source, document.FullSource);
        Assert.False(document.HasUserRegion);
        Assert.Null(document.UserSource);
        Assert.Null(document.UserRegion);
        Assert.NotEmpty(document.Problems);
    }

    [Fact]
    public async Task UserScriptsAreNotInspectedAsObjectTemplates()
    {
        var created = projects.CreateScript(projects.Create(root, "Demo"), ProjectScriptKind.User, "helper");
        var bridge = new StubBridge();
        await Assert.ThrowsAsync<ProjectOperationException>(() => new ObjectScriptDocumentService(bridge)
            .InspectAsync(created.Project, created.Script.Id, 1, ""));
        Assert.False(bridge.Called);
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("outOfBounds")]
    [InlineData("surrogate")]
    [InlineData("unknown")]
    [InlineData("unconfirmed")]
    public async Task InvalidHelperResponsesCannotCreateEditableDocuments(string mode)
    {
        var created = projects.CreateScript(projects.Create(root, "Demo"), ProjectScriptKind.Object, "hero");
        var bridge = new StubBridge { Mode = mode };
        await Assert.ThrowsAsync<PythonBridgeException>(() => new ObjectScriptDocumentService(bridge)
            .InspectAsync(created.Project, created.Script.Id, 1, "😀"));
    }

    [Fact]
    public async Task CancelledInspectionDoesNotStartHelper()
    {
        var created = projects.CreateScript(projects.Create(root, "Demo"), ProjectScriptKind.Object, "hero");
        var bridge = new StubBridge();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ObjectScriptDocumentService(bridge)
            .InspectAsync(created.Project, created.Script.Id, 1, "", cancellation.Token));
        Assert.False(bridge.Called);
    }

    private sealed class StubBridge : IPythonBridge
    {
        public string Mode = "unknown";
        public bool Called;
        public Task<ObjectScriptInspectionResult> InspectObjectScriptAsync(ObjectScriptInspectionRequest request, CancellationToken cancellationToken = default)
        {
            Called = true;
            var span = Mode switch { "surrogate" => new SourceSpan(1, 0), "outOfBounds" => new SourceSpan(0, 99), _ => new SourceSpan(0, 0) };
            return Task.FromResult(new ObjectScriptInspectionResult(request.Uri, Mode == "stale" ? request.Version + 1 : request.Version,
                Mode == "unknown" ? "unknown" : Mode == "unconfirmed" ? "invalidStructure" : "recognized", 1, span, []));
        }
        public Task<PythonHelperInfo> InitializeAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PythonParseResult> ParseAsync(PythonParseRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PythonReferencesResult> FindReferencesAsync(PythonReferencesRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public void Dispose() => Directory.Delete(root, true);
}
