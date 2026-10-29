using happy_agent_coding.Services;
using happy_agent_coding.ViewModels;
using VisualizedAgentCoding.Application.Editor.ObjectScripts;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Application.Python;
using VisualizedAgentCoding.Domain.Projects;
using VisualizedAgentCoding.Infrastructure.Projects;
using VisualizedAgentCoding.Infrastructure.Python;

namespace VisualizedAgentCoding.App.Tests;

public sealed class ObjectScriptDiagnosticsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "diagnostic-projection-" + Guid.NewGuid().ToString("N"));

    private async Task<MultiRegionProjection> Create(PythonProcessBridge bridge, string members, string? start = null,
        string newline = "\n")
    {
        var service = new ProjectService(new JsonProjectStore());
        var created = service.CreateScript(service.Create(root, "Demo"), ProjectScriptKind.Object, "hero");
        var source = ProjectScriptTemplates.CreateMultiRegion(ProjectScriptKind.Object, "Hero")
            .Replace("    # vac:region-end members", members + "    # vac:region-end members");
        if (start is not null) source = source.Replace("        " + ProjectScriptTemplates.UserPlaceholderStatement + "\n", start);
        source = source.Replace("\n", newline);
        var document = await new ObjectScriptDocumentService(bridge).InspectAsync(created.Project, created.Script.Id, 1, source);
        Assert.True(document.HasRegions, string.Join("; ", document.Problems));
        return new(document);
    }

    private static async Task<PythonDiagnostic[]> Analyze(PythonProcessBridge bridge, MultiRegionProjection projection) =>
        ObjectScriptDiagnostics.Present((await bridge.ParseAsync(new(projection.Document.Uri, 1, projection.FullSource))).Diagnostics, projection);

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task MixedIndentationKeepsUserErrorAndDropsHiddenRecoveryCascade(string newline)
    {
        await using var bridge = new PythonProcessBridge();
        var projection = await Create(bridge, "\tvalue =\n", newline: newline);
        var errors = await Analyze(bridge, projection);
        var error = Assert.Single(errors);
        Assert.Equal(2, error.Line);
        Assert.Equal("native", error.Parser);
        Assert.DoesNotContain("系统代码", error.DisplayText);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task MissingUserMethodBodyPointsAtVisibleDefinitionAndTranslatesMessageLine(string newline)
    {
        await using var bridge = new PythonProcessBridge();
        var projection = await Create(bridge, "    def walk(self):\n", newline: newline);
        var error = Assert.Single(await Analyze(bridge, projection));
        Assert.Equal(2, error.Line);
        Assert.Equal(1, error.Column);
        Assert.EndsWith("on line 2", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("        # user comment\n")]
    public async Task EmptyOrCommentOnlyStartupBodyPointsAtEditableRegion(string body)
    {
        await using var bridge = new PythonProcessBridge();
        var projection = await Create(bridge, "", body);
        var error = Assert.Single(await Analyze(bridge, projection));
        var region = projection.EditableRegions.Single(r => r.Id == "start");
        Assert.Equal(1 + projection.ViewSource[..region.Span.Offset].Count(c => c == '\n'), error.Line);
        Assert.Contains("至少一条语句", error.Message);
    }

    [Fact]
    public async Task IndependentErrorsRemainVisibleAcrossMultipleRegionsAndDisappearAfterRepair()
    {
        await using var bridge = new PythonProcessBridge();
        var projection = await Create(bridge, "    value =\n", "        other =\n");
        var errors = await Analyze(bridge, projection);
        Assert.Equal(2, errors.Length);
        Assert.All(errors, error => Assert.True(error.Line > 0));
        Assert.Contains(errors, error => error.Parser == "recovery");
        projection.UpdateRegions(new Dictionary<string, string> { ["members"] = "value = 1\n", ["start"] = "other = 2\n" });
        Assert.Empty(await Analyze(bridge, projection));
    }

    [Fact]
    public async Task IndependentMissingBlockAtHiddenEofIsNotDiscardedAsACascade()
    {
        await using var bridge = new PythonProcessBridge();
        var projection = await Create(bridge, "    value =\n", "        if True:\n");
        var errors = await Analyze(bridge, projection);
        Assert.Equal(2, errors.Length);
        Assert.Contains(errors, e => e.Parser == "recovery" && e.Message == "expected an indented block" && e.Line > 0);
    }

    [Fact]
    public async Task UnknownUnmappedDiagnosticsArePreservedAndCountsUsePresentedItems()
    {
        await using var bridge = new PythonProcessBridge();
        var projection = await Create(bridge, "    value =\n");
        var position = projection.ToFullPosition(2, 1);
        PythonDiagnostic[] raw = [new("error", "invalid syntax", position.Line, position.Column, "native"),
            new("error", "invalid syntax", position.Line, position.Column, "recovery"),
            new("error", "unexpected indent", 1, 1, "recovery"), new("error", "unknown", 1, 1)];
        var vm = TestMainViewModelFactory.Create(new Settings());
        foreach (var error in raw) vm.PythonDiagnostics.Add(error);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        vm.SetEditorProjection(projection);
        Assert.Equal(2, vm.PythonErrorCount);
        Assert.Equal(2, vm.EditorDiagnostics.Count());
        Assert.Contains(nameof(MainViewModel.PythonErrorListTitle), changed);
        Assert.Contains("暂无法定位", vm.EditorDiagnostics.Last().Message);
        Assert.DoesNotContain("系统代码", vm.EditorDiagnostics.Last().DisplayText);
        Assert.Equal(3, ObjectScriptDiagnostics.Present(raw, null).Length);
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private sealed class Settings : ISettingsStore
    {
        public AppSettings Load() => new();
        public void Save(AppSettings settings) { }
    }
}
