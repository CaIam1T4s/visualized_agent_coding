using AvaloniaEdit.Document;
using happy_agent_coding.Services;
using happy_agent_coding.ViewModels;
using VisualizedAgentCoding.Application.Editor;
using VisualizedAgentCoding.Application.Editor.ObjectScripts;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Application.Python;
using VisualizedAgentCoding.Domain.Projects;
using VisualizedAgentCoding.Infrastructure.Projects;
using VisualizedAgentCoding.Infrastructure.Python;

namespace VisualizedAgentCoding.App.Tests;

public sealed class ScriptImportsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "script-imports-" + Guid.NewGuid().ToString("N"));
    private readonly JsonProjectStore store = new();
    private (ProjectWorkspace Project, ProjectScriptEntry Tools, ProjectScriptEntry Caller) Create(ProjectScriptKind kind)
    {
        var service = new ProjectService(store);
        var tools = service.CreateScript(service.Create(root, "Demo"), ProjectScriptKind.User, "tools");
        store.WriteScript(root, tools.Script.Path, "def greet():\n    return 1\n");
        var caller = service.CreateScript(tools.Project, kind, "caller");
        return (caller.Project, tools.Script, caller.Script);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task OrdinaryScriptProjectionProtectsSummaryPreservesDocstringAndSupportsStructuralUndo(string nl)
    {
        var (project, tools, caller) = Create(ProjectScriptKind.User);
        var original = "\"\"\"用户说明😀\"\"\"" + nl + "from __future__ import annotations" + nl + "value = 1" + nl;
        await using var bridge = new PythonProcessBridge();
        var service = new ScriptImportService(bridge);
        var result = await service.InspectAsync(project, caller, 1, original,
            imports: [new("scripts.user.tools", Alias: "utils", Source: "project", ScriptId: tools.Id), new("math")]);
        var before = new UserScriptImportDocument(caller.Id, result.Uri, original, null);
        var after = new UserScriptImportDocument(caller.Id, result.Uri, result.Source, result.Block);
        var current = new UserScriptImportProjection(before);
        var buffer = new TextDocument(current.ViewSource);
        MultiRegionReadOnlyProvider? provider = null;
        var restoring = false;
        buffer.TextChanged += (_, _) => { if (!restoring && provider is not null) current.UpdateRegions(provider.ReadBodies()); };
        StructuralEditorHistory.ApplyUser(buffer, before, after, () => restoring = true, document =>
        {
            current = new(document); provider = new(buffer, current.EditableRegions); restoring = false;
        });
        Assert.Contains("# 已导入：", buffer.Text);
        Assert.DoesNotContain("vac:import", buffer.Text);
        Assert.Contains("用户说明😀", buffer.Text);
        var summary = buffer.Text.IndexOf("# 已导入", StringComparison.Ordinal);
        Assert.False(provider!.CanInsert(summary + 2));
        Assert.Equal(result.Source, current.FullSource);
        var fullValueLine = 1 + result.Source[..result.Source.IndexOf("value", StringComparison.Ordinal)].Count(c => c == '\n');
        Assert.True(current.TryToViewPosition(fullValueLine, 1, out var row, out var column));
        Assert.Equal((fullValueLine, 1), current.ToFullPosition(row, column));
        buffer.UndoStack.Undo(); Assert.Equal(original, current.FullSource); Assert.Equal(original, buffer.Text);
        buffer.UndoStack.Redo(); Assert.Equal(result.Source, current.FullSource);
        var offset = buffer.Text.IndexOf("value = 1", StringComparison.Ordinal);
        buffer.Replace(offset, "value = 1".Length, "value = 2");
        Assert.EndsWith("value = 2" + nl, current.FullSource);
        var reread = await service.InspectAsync(project, caller, 2, current.FullSource);
        Assert.Null(reread.Problem);
        Assert.Equal(current.ViewSource, new UserScriptImportProjection(new(caller.Id, result.Uri, current.FullSource, reread.Block)).ViewSource);
    }

    [Theory]
    [InlineData(ProjectScriptKind.Object)]
    [InlineData(ProjectScriptKind.Backdrop)]
    public async Task V3ImportsAreModuleLevelPreserveBodiesAndSaveThroughTrustedStructure(ProjectScriptKind kind)
    {
        var (project, tools, caller) = Create(kind);
        await using var bridge = new PythonProcessBridge();
        await using var vm = new MainViewModel(new Settings(), new InMemoryRecentProjectsStore(), new ProjectSession(), bridge);
        await vm.OpenProject(root);
        await vm.OpenScriptFromExplorerCommand.ExecuteAsync((kind == ProjectScriptKind.Object ? vm.ObjectScripts : vm.BackdropScripts).Single());
        var original = await vm.InspectEditorProtectionAsync(CancellationToken.None);
        Assert.Equal(3, original!.TemplateVersion);
        var projection = new MultiRegionProjection(original);
        projection.UpdateRegions(new Dictionary<string, string> { ["members"] = "def greet(self):\n    return 1\n", ["start"] = "self.greet()\n" });
        vm.RestoreEditorStructure(projection);
        var service = new ScriptImportService(bridge);
        var result = await service.InspectAsync(project, caller, 2, projection.FullSource, projection.Document,
            [new("scripts.user.tools", Alias: "utils", Source: "project", ScriptId: tools.Id)]);
        Assert.True(result.SyntaxValid);
        Assert.True(result.Source.IndexOf("import scripts.user.tools", StringComparison.Ordinal) < result.Source.IndexOf("class Caller", StringComparison.Ordinal));
        var updated = await new ObjectScriptDocumentService(bridge).InspectAsync(project, caller.Id, 3, result.Source);
        Assert.True(updated.HasRegions);
        var target = new MultiRegionProjection(updated);
        Assert.StartsWith("# 已导入：", target.ViewSource);
        Assert.Contains("def greet(self):\n    return 1", target.ViewSource);
        Assert.DoesNotContain("import scripts", target.ViewSource);
        vm.RestoreEditorStructure(target);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal("已保存", vm.EditorState);
        Assert.Equal(result.Source, store.ReadScript(root, caller.Path));
        Assert.Equal(tools.Id, updated.Imports!.Entries.Single().ScriptId);
    }

    [Fact]
    public async Task ManagedImportsParticipateInRenameAndDeletionImpactWithStableIdentity()
    {
        var (project, tools, caller) = Create(ProjectScriptKind.User);
        await using var bridge = new PythonProcessBridge();
        var service = new ScriptImportService(bridge);
        var result = await service.InspectAsync(project, caller, 1, "utils.greet()\n",
            imports: [new("scripts.user.tools", Alias: "utils", Source: "project", ScriptId: tools.Id)]);
        store.WriteScript(root, caller.Path, result.Source);
        var impact = await new ScriptDeletionImpactService(bridge, store).AnalyzeAsync(project, tools.Id, 1, []);
        Assert.Equal(DeletionImpactState.Complete, impact.State);
        Assert.Contains(impact.References, r => r.Uri == result.Uri);
        var rename = new ScriptRenameService(bridge, store);
        var plan = await rename.PlanAsync(project, tools.Id, "utilities", 1, []);
        var renamed = rename.Apply(plan);
        var source = store.ReadScript(root, caller.Path);
        Assert.Contains("import scripts.user.utilities as utils", source);
        var inspected = await service.InspectAsync(renamed, caller, 2, source);
        Assert.Empty(inspected.Diagnostics);
        Assert.Equal(tools.Id, inspected.Block!.Entries.Single().ScriptId);
        var deleted = new ProjectService(store).DeleteScript(renamed, tools.Id);
        inspected = await service.InspectAsync(deleted, caller, 3, source);
        Assert.Contains(inspected.Diagnostics, d => d.Navigation == "imports");
        Assert.Equal(source, store.ReadScript(root, caller.Path));
    }

    [Fact]
    public async Task V2UpgradePreservesEveryRegionAndRequiresExplicitOptIn()
    {
        var (project, _, caller) = Create(ProjectScriptKind.Object);
        var source = ProjectScriptTemplates.CreateMultiRegion(caller.Kind, "Caller")
            .Replace("    # vac:region-end members", "    text = '# vac:template-version=2'\n    # vac:region-end members");
        store.WriteScript(root, caller.Path, source);
        await using var bridge = new PythonProcessBridge();
        var documents = new ObjectScriptDocumentService(bridge);
        var original = await documents.InspectAsync(project, caller.Id, 1, source);
        var upgraded = await new ObjectScriptUpgradeService(documents).PrepareAsync(project, original, 2);
        Assert.Equal(3, upgraded.TemplateVersion);
        Assert.Contains("text = '# vac:template-version=2'", upgraded.FullSource);
        Assert.Equal(source, store.ReadScript(root, caller.Path));
        Assert.Equal(new MultiRegionProjection(original).ViewSource, new MultiRegionProjection(upgraded).ViewSource);
        await Assert.ThrowsAsync<ProjectOperationException>(() => new ScriptImportService(bridge).InspectAsync(project, caller, 1, source, original, [new("math")]));
    }

    [Fact]
    public async Task RoleManualImportIsDiagnosedAndConversionPreservesImportedSymbolAndAlias()
    {
        var (project, _, caller) = Create(ProjectScriptKind.Object);
        await using var bridge = new PythonProcessBridge();
        var documents = new ObjectScriptDocumentService(bridge);
        var source = store.ReadScript(root, caller.Path).Replace("    # vac:region-end members", "    from math import sqrt as root\n    # vac:region-end members");
        var doc = await documents.InspectAsync(project, caller.Id, 1, source);
        var context = ScriptImportService.Context(project, caller, doc);
        var parse = await bridge.ParseAsync(new(doc.Uri, 1, source, context));
        var error = Assert.Single(parse.Diagnostics);
        Assert.Equal("import-rule", error.Category);
        var converted = await new ScriptImportService(bridge).InspectAsync(project, caller, 2, source, doc, convertLine: error.Line);
        Assert.Equal("sqrt", converted.Block!.Entries.Single().Name);
        Assert.Equal("root", converted.Block.Entries.Single().Alias);
        Assert.True(converted.SyntaxValid);
    }

    [Fact]
    public async Task ImportRuleErrorsRemainSavableButRunEntryRejectsThem()
    {
        var (project, _, caller) = Create(ProjectScriptKind.User);
        new ProjectService(store).CreateScript(project, ProjectScriptKind.Object, "hero");
        await using var bridge = new PythonProcessBridge();
        await using var vm = new MainViewModel(new Settings(), new InMemoryRecentProjectsStore(), new ProjectSession(), bridge);
        await vm.OpenProject(root);
        await vm.OpenScriptFromExplorerCommand.ExecuteAsync(vm.UserScripts.Single(s => s.Id == caller.Id));
        vm.PythonCode = "import scripts.objects.hero\n";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal("已保存", vm.EditorState);
        Assert.Equal(vm.PythonCode, store.ReadScript(root, caller.Path));
        await vm.RunCommand.ExecuteAsync(null);
        Assert.Contains("导入规则错误", vm.EditorState);
        vm.PythonCode = "import math\n";
        var run = vm.RunCommand.ExecuteAsync(null);
        await TestMainViewModelFactory.WaitForInteractiveAsync(vm, run);
        Assert.True(vm.IsPythonActive);
        await vm.StopPythonAsync();
        await run;
        Assert.Equal("已停止", vm.EditorState);
        vm.PythonCode = "value =\n";
        await vm.RunCommand.ExecuteAsync(null);
        Assert.Contains("存在语法错误", vm.EditorState);
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private sealed class Settings : ISettingsStore { public AppSettings Load() => new(); public void Save(AppSettings settings) { } }
}
