using AvaloniaEdit.Document;
using happy_agent_coding.Services;
using happy_agent_coding.ViewModels;
using VisualizedAgentCoding.Application.Editor.ObjectScripts;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Application.Python;
using VisualizedAgentCoding.Domain.Projects;
using VisualizedAgentCoding.Infrastructure.Projects;
using VisualizedAgentCoding.Infrastructure.Python;

namespace VisualizedAgentCoding.App.Tests;

public class MultiRegionProjectionTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "multi-view-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task NewTemplatesHaveReadOnlyTitlesAndHideOneAndTwoIndentLevels(string newline)
    {
        var (project, script, source) = Create(newline);
        await using var bridge = new PythonProcessBridge();
        var document = await new ObjectScriptDocumentService(bridge).InspectAsync(project, script.Id, 1, source);
        var projection = new MultiRegionProjection(document);
        Assert.Equal(source, projection.FullSource);
        Assert.Equal("# 类定义" + newline + newline + "# 程序启动时运行" + newline + ProjectScriptTemplates.UserPlaceholderStatement + newline + newline, projection.ViewSource);
        Assert.DoesNotContain("ElementBase", projection.ViewSource);
        projection.UpdateRegions(new Dictionary<string, string>
        {
            ["members"] = "def walk(self):" + newline + "    self.move(10)" + newline,
            ["start"] = "self.walk()",
        });
        Assert.Contains("    def walk(self):" + newline + "        self.move(10)", projection.FullSource);
        Assert.Contains("        self.walk()" + newline, projection.FullSource);
        Assert.Equal((0, 0), projection.ToFullPosition(1, 1));
        var viewDocument = new TextDocument(projection.ViewSource);
        var offset = projection.ViewSource.IndexOf("self.walk", StringComparison.Ordinal);
        var position = viewDocument.GetLocation(offset);
        var full = projection.ToFullPosition(position.Line, position.Column);
        var fullDocument = new TextDocument(projection.FullSource);
        Assert.Equal(projection.FullSource.IndexOf("self.walk", StringComparison.Ordinal), fullDocument.GetOffset(full.Line, full.Column));
        Assert.True(projection.TryToViewPosition(full.Line, full.Column, out var line, out var column));
        Assert.Equal((position.Line, position.Column), (line, column));
        Assert.False(projection.TryToViewPosition(1, 1, out _, out _));
        var reread = await new ObjectScriptDocumentService(bridge).InspectAsync(project, script.Id, 2, projection.FullSource);
        Assert.True(reread.HasRegions);
        Assert.Empty((await bridge.ParseAsync(new(document.Uri, 2, projection.FullSource))).Diagnostics);
    }

    [Fact]
    public async Task HeaderLikeCommentsAreUserTextAndAnchorsSurviveEditsUndoAndRedo()
    {
        var (project, script, source) = Create("\n");
        await using var bridge = new PythonProcessBridge();
        var document = await new ObjectScriptDocumentService(bridge).InspectAsync(project, script.Id, 1, source);
        var projection = new MultiRegionProjection(document);
        var buffer = new TextDocument(projection.ViewSource);
        var provider = new MultiRegionReadOnlyProvider(buffer, projection.EditableRegions);
        Assert.False(provider.CanInsert(0));
        Assert.Empty(provider.GetDeletableSegments(new SimpleSegment(0, buffer.TextLength)));
        var insertion = projection.EditableRegions[0].Span.Offset;
        buffer.Insert(insertion, "# 程序启动时运行\nvalue = 1\n");
        projection.UpdateRegions(provider.ReadBodies());
        Assert.Equal(buffer.Text, projection.ViewSource);
        Assert.Contains("    # 程序启动时运行\n    value = 1\n", projection.FullSource);
        Assert.Equal(2, projection.Document.Regions!.Length);
        buffer.UndoStack.Undo();
        projection.UpdateRegions(provider.ReadBodies());
        Assert.Equal(source, projection.FullSource);
        buffer.UndoStack.Redo();
        projection.UpdateRegions(provider.ReadBodies());
        Assert.Equal(buffer.Text, projection.ViewSource);
        var start = projection.EditableRegions[1].Span;
        Assert.True(provider.CanInsert(start.Offset));
        Assert.Empty(provider.GetDeletableSegments(new SimpleSegment(insertion, start.Offset - insertion + 1)));
        buffer.Remove(start.Offset, start.Length);
        projection.UpdateRegions(provider.ReadBodies());
        Assert.Equal("", projection.FullRegionSources()["start"]);
        Assert.DoesNotContain("pass", projection.FullRegionSources()["start"]);
    }

    [Theory]
    [InlineData("    text = \"\"\"hello\n  literal indent\n\"\"\"\n")]
    [InlineData("\ttext = r\"\"\"你好😀\n\tactual tab\n\"\"\"\n")]
    [InlineData("    text = f\"\"\"hello {\n        {\"a\": \"\"\"nested\n literal\n\"\"\"}\n    }\n  tail\n\"\"\"\n")]
    [InlineData("    text = \"line\\\n  continued string\"\n")]
    public void LiteralContentsTabsAndUnchangedLinesArePreserved(string source)
    {
        var codec = new PythonWrapperIndentation(source, 4);
        Assert.Equal(source, string.Concat(codec.Encode(codec.ViewSource).Select(l => l.Raw)));
        var edited = codec.ViewSource + "value = 2\n";
        var full = string.Concat(codec.Encode(edited).Select(l => l.Raw));
        Assert.Equal(source + "    value = 2\n", full);
    }

    [Fact]
    public async Task EnterAndBackspaceAtTitleBoundaryDoNotModifyTitleAndRemainUndoable()
    {
        var (project, script, source) = Create("\r\n");
        await using var bridge = new PythonProcessBridge();
        var document = await new ObjectScriptDocumentService(bridge).InspectAsync(project, script.Id, 1, source);
        var projection = new MultiRegionProjection(document);
        var buffer = new TextDocument(projection.ViewSource);
        var provider = new MultiRegionReadOnlyProvider(buffer, projection.EditableRegions);
        var headerEnd = buffer.GetLineByNumber(1).EndOffset;
        Assert.True(provider.TryEnterFromTitle(headerEnd, out var caret));
        Assert.Equal(projection.EditableRegions[0].Span.Offset, caret);
        Assert.True(provider.TryBackspaceFirstBlankLine(caret, out var returned));
        Assert.Equal(headerEnd, returned);
        Assert.Equal(projection.ViewSource, buffer.Text);
        buffer.UndoStack.Undo();
        Assert.Equal("\r\n", provider.ReadBodies()["members"]);
        buffer.UndoStack.Redo();
        Assert.Equal("", provider.ReadBodies()["members"]);
        Assert.False(provider.CanInsert(headerEnd));
    }

    [Fact]
    public void KeywordAdjacentStringsAndFormattedFillDoNotLeakLiteralState()
    {
        var source = "    if\"{\":\n        value = 1\n    text = f\"{value:'>10} {value:#x}\"\n";
        var codec = new PythonWrapperIndentation(source, 4);
        Assert.Equal("if\"{\":\n    value = 1\ntext = f\"{value:'>10} {value:#x}\"\n", codec.ViewSource);
        Assert.Equal(source + "    other = 2\n", string.Concat(codec.Encode(codec.ViewSource + "other = 2\n").Select(l => l.Raw)));
    }

    [Fact]
    public async Task ReferencesUseFullSnapshotAndReturnToVisibleMethodAndCall()
    {
        var (project, script, source) = Create("\n");
        await using var bridge = new PythonProcessBridge();
        var document = await new ObjectScriptDocumentService(bridge).InspectAsync(project, script.Id, 1, source);
        var projection = new MultiRegionProjection(document);
        projection.UpdateRegions(new Dictionary<string, string>
        {
            ["members"] = "def walk(self):\n    pass\n",
            ["start"] = "self.walk()\n",
        });
        var buffer = new TextDocument(projection.ViewSource);
        var visible = buffer.GetLocation(projection.ViewSource.LastIndexOf("walk", StringComparison.Ordinal));
        var full = projection.ToFullPosition(visible.Line, visible.Column);
        var result = await bridge.FindReferencesAsync(new(project.RootPath, document.Uri, 2, full.Line, full.Column,
            [new(document.Uri, 2, projection.FullSource)]));
        Assert.False(result.IsIncomplete);
        var references = result.References.Where(r => r.Uri == document.Uri).ToArray();
        Assert.True(references.Length >= 2);
        Assert.All(references, r => Assert.True(projection.TryToViewPosition(r.Range.Line, r.Range.Column, out _, out _)));
        Assert.True(projection.TryToViewPosition(full.Line, full.Column, out var line, out var column));
        Assert.Equal((visible.Line, visible.Column), (line, column));
    }

    [Fact]
    public async Task ProjectedSyntaxErrorsSaveWithoutReparsingAndExternalChangesStillRejectOverwrite()
    {
        var (project, script, source) = Create("\n");
        await using var bridge = new PythonProcessBridge();
        await using var vm = new MainViewModel(new Settings(), new InMemoryRecentProjectsStore(), new ProjectSession(), bridge);
        await vm.OpenProject(root);
        await vm.OpenScriptFromExplorerCommand.ExecuteAsync(Assert.Single(vm.ObjectScripts));
        var document = await vm.InspectEditorProtectionAsync(CancellationToken.None);
        var projection = new MultiRegionProjection(document!);
        vm.SetEditorProjection(projection);
        projection.UpdateRegions(new Dictionary<string, string> { ["start"] = "self.text = \"\"\"unfinished" });
        vm.PythonCode = projection.FullSource;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal("已保存", vm.EditorState);
        Assert.Equal(projection.FullSource, File.ReadAllText(project.ToAbsolutePath(script.Path)));
        projection.UpdateRegions(new Dictionary<string, string> { ["start"] = "self.value = 1\n" });
        vm.PythonCode = projection.FullSource;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal("已保存", vm.EditorState);
        var external = File.ReadAllText(project.ToAbsolutePath(script.Path)) + "# external\n";
        File.WriteAllText(project.ToAbsolutePath(script.Path), external);
        projection.UpdateRegions(new Dictionary<string, string> { ["start"] = "self.value = 2\n" });
        vm.PythonCode = projection.FullSource;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Contains("外部修改", vm.EditorState);
        Assert.Equal(external, File.ReadAllText(project.ToAbsolutePath(script.Path)));
        Assert.Contains("self.value = 2", vm.PythonCode);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task InitializeAdditionPreservesBodiesAndSavesWithConflictProtection(string newline)
    {
        var (project, script, _) = Create(newline);
        await using var bridge = new PythonProcessBridge();
        await using var vm = new MainViewModel(new Settings(), new InMemoryRecentProjectsStore(), new ProjectSession(), bridge);
        await vm.OpenProject(root);
        await vm.OpenScriptFromExplorerCommand.ExecuteAsync(Assert.Single(vm.ObjectScripts));
        var original = (await vm.InspectEditorProtectionAsync(CancellationToken.None))!;
        var projection = new MultiRegionProjection(original);
        projection.UpdateRegions(new Dictionary<string, string> { ["members"] = "def walk(self):" + newline + "    pass" + newline });
        vm.SetEditorProjection(projection);
        vm.PythonCode = projection.FullSource;
        vm.AddInitializeRegion();
        var added = (await vm.InspectEditorProtectionAsync(CancellationToken.None))!;
        Assert.Equal(3, added.Regions!.Length);
        var ordered = new MultiRegionProjection(added);
        Assert.True(ordered.ViewSource.IndexOf("# 类定义", StringComparison.Ordinal)
            < ordered.ViewSource.IndexOf("# 初始化", StringComparison.Ordinal));
        Assert.True(ordered.ViewSource.IndexOf("# 初始化", StringComparison.Ordinal)
            < ordered.ViewSource.IndexOf("# 程序启动时运行", StringComparison.Ordinal));
        Assert.Equal(added.FullSource, ordered.FullSource);
        var visibleDocument = new TextDocument(ordered.ViewSource);
        var initializePosition = visibleDocument.GetLocation(ordered.EditableRegions.First(r => r.Id == "initialize").Span.Offset);
        var fullPosition = ordered.ToFullPosition(initializePosition.Line, initializePosition.Column);
        Assert.True(ordered.TryToViewPosition(fullPosition.Line, fullPosition.Column, out var visibleLine, out var visibleColumn));
        Assert.Equal((initializePosition.Line, initializePosition.Column), (visibleLine, visibleColumn));
        ordered.UpdateRegions(new Dictionary<string, string> { ["initialize"] = "self.value = 1" + newline });
        Assert.Contains("        self.value = 1" + newline, ordered.FullSource);
        Assert.Contains("    def walk(self):" + newline + "        pass", ordered.FullSource);
        Assert.Contains("    def walk(self):" + newline + "        pass", added.FullSource);
        vm.AddInitializeRegion();
        Assert.Equal(added.FullSource, vm.PythonCode);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal("已保存", vm.EditorState);
        var reread = await new ObjectScriptDocumentService(bridge).InspectAsync(project, script.Id, 20,
            File.ReadAllText(project.ToAbsolutePath(script.Path)));
        Assert.True(reread.HasRegions, string.Join(";", reread.Problems));
        Assert.Equal(3, reread.Regions!.Length);
        Assert.Empty((await bridge.ParseAsync(new(reread.Uri, 20, reread.FullSource))).Diagnostics);
    }

    [Fact]
    public async Task ReferenceWarningsMapToVisibleRowsAndKeepOriginalNavigationRanges()
    {
        var (_, _, _) = Create("\n");
        await using var bridge = new PythonProcessBridge();
        await using var vm = new MainViewModel(new Settings(), new InMemoryRecentProjectsStore(), new ProjectSession(), bridge);
        await vm.OpenProject(root);
        await vm.OpenScriptFromExplorerCommand.ExecuteAsync(Assert.Single(vm.ObjectScripts));
        var projection = new MultiRegionProjection((await vm.InspectEditorProtectionAsync(CancellationToken.None))!);
        projection.UpdateRegions(new Dictionary<string, string> { ["members"] = "def walk(self):\n    pass\n", ["start"] = "self.walk()\nx =\n" });
        vm.SetEditorProjection(projection);
        vm.PythonCode = projection.FullSource;
        var view = new TextDocument(projection.ViewSource);
        var call = view.GetLocation(projection.ViewSource.LastIndexOf("walk", StringComparison.Ordinal));
        var full = projection.ToFullPosition(call.Line, call.Column);
        var errorRow = view.GetLocation(projection.ViewSource.IndexOf("x =", StringComparison.Ordinal)).Line;
        await vm.FindReferencesAtAsync(full.Line, full.Column);
        Assert.Contains($"第 {errorRow} 行", vm.ReferenceIssues);
        Assert.All(vm.PythonReferences, r => Assert.NotNull(r.ViewRange));
        Assert.Contains(vm.PythonReferences, r => r.ViewRange!.Line != r.Location.Range.Line);
    }

    private (ProjectWorkspace Project, ProjectScriptEntry Script, string Source) Create(string newline)
    {
        var projects = new ProjectService(new JsonProjectStore());
        var created = projects.CreateScript(projects.Create(root, "Demo"), ProjectScriptKind.Object, "hero");
        var source = projects.LoadScript(created.Project, created.Script).Replace("\n", newline);
        File.WriteAllText(created.Project.ToAbsolutePath(created.Script.Path), source);
        Assert.StartsWith("# vac:template-version=3", source);
        return (created.Project, created.Script, source);
    }
    private sealed class Settings : ISettingsStore
    {
        public AppSettings Load() => new();
        public void Save(AppSettings settings) { }
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
