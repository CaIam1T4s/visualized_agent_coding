using AvaloniaEdit.Document;
using happy_agent_coding.Services;
using happy_agent_coding.ViewModels;
using VisualizedAgentCoding.Application.Editor.ObjectScripts;
using VisualizedAgentCoding.Application.Ports;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Domain.Projects;
using VisualizedAgentCoding.Infrastructure.Projects;
using VisualizedAgentCoding.Infrastructure.Python;

namespace VisualizedAgentCoding.App.Tests;

public sealed class StructuralEditorHistoryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "structure-history-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task StructureSharesHistoryWithMethodsAndTypingAndRestoresAnchors(string newline)
    {
        var (project, script, source) = Create(newline);
        await using var bridge = new PythonProcessBridge();
        var baseline = await new ObjectScriptDocumentService(bridge).InspectAsync(project, script.Id, 1, source);
        var editor = new HistoryEditor(baseline);
        var members = editor.Projection.EditableRegions.First(r => r.Id == "members").Span;
        editor.Text.Insert(members.Offset, "def walk(self):" + newline + "    pass" + newline);
        var before = editor.Projection.FullSource;
        var stateBefore = new StructuralEditorState(editor.Projection.Document, 0, 5, 0);
        var stateAfter = editor.AddInitialize(stateBefore);
        var added = editor.Projection.FullSource;
        var body = editor.Projection.EditableRegions.First(r => r.Id == "initialize").Span;
        editor.Text.Replace(body.Offset, body.Length, "self.value = 1" + newline);
        var typed = editor.Projection.FullSource;
        editor.Text.UndoStack.Undo();
        Assert.Equal(added, editor.Projection.FullSource);
        editor.Text.UndoStack.Undo();
        Assert.Equal(before, editor.Projection.FullSource);
        Assert.Equal(stateBefore, editor.Restored);
        Assert.DoesNotContain(editor.Projection.Document.Regions!, r => r.Kind == "initialize");
        Assert.False(editor.Provider.CanInsert(0));
        editor.Text.UndoStack.Undo();
        Assert.Equal(source, editor.Projection.FullSource);
        editor.Text.UndoStack.Redo();
        Assert.Equal(before, editor.Projection.FullSource);
        editor.Text.UndoStack.Redo();
        Assert.Equal(added, editor.Projection.FullSource);
        Assert.Equal(stateAfter, editor.Restored);
        var restoredBody = editor.Projection.EditableRegions.First(r => r.Id == "initialize").Span;
        Assert.True(editor.Provider.CanInsert(restoredBody.Offset));
        Assert.False(editor.Provider.CanInsert(restoredBody.Offset - 1));
        var visibleLine = editor.Text.GetLocation(restoredBody.Offset);
        var fullLine = editor.Projection.ToFullPosition(visibleLine.Line, visibleLine.Column);
        Assert.True(editor.Projection.TryToViewPosition(fullLine.Line, fullLine.Column, out var mappedLine, out var mappedColumn));
        Assert.Equal((visibleLine.Line, visibleLine.Column), (mappedLine, mappedColumn));
        Assert.Empty(editor.Provider.GetDeletableSegments(new SimpleSegment(restoredBody.Offset - 1, 2)));
        editor.Text.UndoStack.Redo();
        Assert.Equal(typed, editor.Projection.FullSource);
        Assert.Equal(editor.Text.Text, editor.Projection.ViewSource);
        editor.Text.UndoStack.Undo();
        editor.Text.UndoStack.Undo();
        editor.Text.Insert(editor.Projection.EditableRegions.First(r => r.Id == "members").Span.Offset, "# branch" + newline);
        Assert.False(editor.Text.UndoStack.CanRedo);
    }

    [Fact]
    public async Task UndoAfterSaveCanRemoveSavedWrapperAndRedoWithDiskConflictChecks()
    {
        var (project, script, source) = Create("\n");
        await using var bridge = new PythonProcessBridge();
        await using var vm = new MainViewModel(new Settings(), new InMemoryRecentProjectsStore(), new ProjectSession(), bridge);
        await vm.OpenProject(root);
        await vm.OpenScriptFromExplorerCommand.ExecuteAsync(Assert.Single(vm.ObjectScripts));
        var baseline = (await vm.InspectEditorProtectionAsync(CancellationToken.None))!;
        var editor = new HistoryEditor(baseline, vm);
        editor.AddInitialize(new(baseline, 0, 0, 0));
        var added = editor.Projection.FullSource;
        editor.Text.SetOwnerThread(null!);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal("已保存", vm.EditorState);
        Assert.Equal(added, File.ReadAllText(project.ToAbsolutePath(script.Path)));
        editor.Text.SetOwnerThread(Thread.CurrentThread);
        editor.Text.UndoStack.Undo();
        Assert.Equal(source, vm.PythonCode);
        editor.Text.SetOwnerThread(null!);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal("已保存", vm.EditorState);
        Assert.Equal(source, File.ReadAllText(project.ToAbsolutePath(script.Path)));
        editor.Text.SetOwnerThread(Thread.CurrentThread);
        editor.Text.UndoStack.Redo();
        Assert.Equal(added, vm.PythonCode);
        var external = source + "# external\n";
        File.WriteAllText(project.ToAbsolutePath(script.Path), external);
        editor.Text.SetOwnerThread(null!);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Contains("外部修改", vm.EditorState);
        Assert.Equal(external, File.ReadAllText(project.ToAbsolutePath(script.Path)));
        Assert.Equal(added, vm.PythonCode);
    }

    [Theory]
    [InlineData("\n", false)]
    [InlineData("\r\n", false)]
    [InlineData("\n", true)]
    [InlineData("\r\n", true)]
    public async Task EventEditAfterTypingWithoutTrailingNewlinePreservesLiveViewUndoAndAnchors(string nl, bool message)
    {
        var (project, script, source) = Create(nl);
        await using var bridge = new PythonProcessBridge();
        var service = new ObjectScriptDocumentService(bridge);
        var doc = await service.InspectAsync(project, script.Id, 0, source);
        var imported = await new VisualizedAgentCoding.Application.Editor.ScriptImportService(bridge).InspectAsync(
            project, script, 0, source, doc,
            [new("vac_runtime", Name: "broadcast", Source: "runtime")]);
        doc = (await service.InspectAsync(project, script.Id, 1, imported.Source)) with { Imports = imported.Block };
        doc = ObjectScriptFeatures.AddInitialize(doc);
        var live = new MultiRegionProjection(doc);
        var text = new TextDocument(live.ViewSource);
        var provider = new MultiRegionReadOnlyProvider(text, live.EditableRegions);
        var changingStructure = false;
        text.TextChanged += (_, _) => { if (!changingStructure) live.UpdateRegions(provider.ReadBodies()); };
        var start = live.EditableRegions.Single(r => r.Id == "start").Span;
        text.Replace(start.Offset, start.Length, "print('ready')");
        Assert.Equal(text.Text, live.ViewSource);
        Assert.NotEqual(text.Text, new MultiRegionProjection(live.Document).ViewSource);
        var before = StructuralEditorState.Capture(live, text.Text, start.Offset, 0, start.Offset);
        var updated = message ? ObjectScriptFeatures.AddMessage(live.Document, "go") : ObjectScriptFeatures.AddSpriteClick(live.Document);
        var target = new MultiRegionProjection(updated);
        target.UpdateRegions(before.RegionBodies!);
        var after = StructuralEditorState.Capture(target, target.ViewSource, 0, 0, 0);
        void Restore(StructuralEditorState state)
        {
            live = (MultiRegionProjection)state.CreateProjection();
            provider = new(text, live.EditableRegions);
            changingStructure = false;
            Assert.Equal(text.Text, live.ViewSource);
        }
        StructuralEditorHistory.Apply(text, before, after, () => changingStructure = true, Restore);
        Assert.Equal("print('ready')", live.ViewRegionSources()["start"]);
        Assert.Contains("broadcast", text.Text);
        text.UndoStack.Undo();
        Assert.Equal(before.ViewSource, text.Text);
        Assert.DoesNotContain(live.Document.Regions!, r => r.Id == updated.Regions!.Last().Id);
        text.UndoStack.Redo();
        Assert.Equal(after.ViewSource, text.Text);
        var eventId = updated.Regions!.Last().Id;
        var deleted = message ? ObjectScriptFeatures.DeleteMessage(live.Document, eventId)
            : ObjectScriptFeatures.DeleteSpriteClick(live.Document, eventId);
        var deletionTarget = new MultiRegionProjection(deleted);
        deletionTarget.UpdateRegions(live.ViewRegionSources().Where(pair => pair.Key != eventId)
            .ToDictionary(pair => pair.Key, pair => pair.Value));
        var deletionState = StructuralEditorState.Capture(deletionTarget, deletionTarget.ViewSource, 0, 0, 0);
        StructuralEditorHistory.Apply(text, after, deletionState, () => changingStructure = true, Restore);
        Assert.Equal(before.ViewSource, text.Text);
        text.UndoStack.Undo();
        Assert.Equal(after.ViewSource, text.Text);
        text.UndoStack.Undo();
        var restoredStart = live.EditableRegions.Single(r => r.Id == "start").Span;
        Assert.True(provider.CanInsert(restoredStart.Offset + restoredStart.Length));
        Assert.False(provider.CanInsert(restoredStart.Offset - 1));
        text.Insert(restoredStart.Offset + restoredStart.Length, nl + "print('after undo')");
        Assert.Contains("after undo", live.FullSource);
        Assert.Equal(text.Text, StructuralEditorState.Capture(live, text.Text, 0, 0, 0).CreateProjection().ViewSource);
        var conflict = StructuralEditorState.Capture(live, text.Text, 0, 0, 0);
        text.Insert(restoredStart.Offset, "# changed" + nl);
        Assert.Throws<InvalidOperationException>(() => StructuralEditorHistory.Apply(text, conflict, after, () => { }, _ => { }));
    }

    private sealed class HistoryEditor
    {
        public TextDocument Text { get; }
        public MultiRegionProjection Projection { get; private set; }
        public MultiRegionReadOnlyProvider Provider { get; private set; }
        public StructuralEditorState? Restored { get; private set; }
        private bool restoring;
        private readonly MainViewModel? vm;
        public HistoryEditor(ObjectScriptDocument initial, MainViewModel? vm = null)
        {
            this.vm = vm;
            Projection = new(initial);
            Text = new(Projection.ViewSource);
            Provider = new(Text, Projection.EditableRegions);
            vm?.SetEditorProjection(Projection);
            Text.TextChanged += (_, _) =>
            {
                if (restoring) return;
                Projection.UpdateRegions(Provider.ReadBodies());
                if (vm is not null) vm.PythonCode = Projection.FullSource;
            };
        }
        public StructuralEditorState AddInitialize(StructuralEditorState before)
        {
            var updated = ObjectScriptFeatures.AddInitialize(Projection.Document);
            var target = new MultiRegionProjection(updated);
            var body = target.EditableRegions.First(r => r.Id == "initialize").Span;
            var line = new TextDocument(target.ViewSource).GetLineByOffset(body.Offset);
            var after = new StructuralEditorState(updated, line.Offset, line.Length, line.EndOffset);
            StructuralEditorHistory.Apply(Text, before, after, () => restoring = true, state =>
            {
                Projection = new(state.Document);
                Provider = new(Text, Projection.EditableRegions);
                Restored = state;
                vm?.RestoreEditorStructure(Projection);
                restoring = false;
            });
            return after;
        }
    }

    private (ProjectWorkspace, ProjectScriptEntry, string) Create(string newline)
    {
        var projects = new ProjectService(new JsonProjectStore());
        var created = projects.CreateScript(projects.Create(root, "Demo"), ProjectScriptKind.Object, "hero");
        var source = projects.LoadScript(created.Project, created.Script).Replace("\n", newline);
        File.WriteAllText(created.Project.ToAbsolutePath(created.Script.Path), source);
        return (created.Project, created.Script, source);
    }
    private sealed class Settings : ISettingsStore
    {
        public AppSettings Load() => new();
        public void Save(AppSettings settings) { }
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
