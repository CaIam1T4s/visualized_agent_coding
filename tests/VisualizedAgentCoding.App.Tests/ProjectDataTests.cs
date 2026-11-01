using happy_agent_coding.Services;
using happy_agent_coding.ViewModels;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Application.Runtime;
using VisualizedAgentCoding.Domain.Projects;
using VisualizedAgentCoding.Infrastructure.Projects;
using VisualizedAgentCoding.Infrastructure.Python;

namespace VisualizedAgentCoding.App.Tests;

public sealed class ProjectDataTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "vac-data-" + Guid.NewGuid().ToString("N"));
    private readonly JsonProjectStore store = new();
    private readonly PythonProcessBridge bridge = new();
    private ProjectWorkspace project = null!;

    public Task InitializeAsync()
    {
        project = new ProjectService(store).Create(root);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await bridge.DisposeAsync();
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    [Fact]
    public void ProjectDataRoundTripsThroughTheManifest()
    {
        var variables = new[]
        {
            new ProjectVariable("var-score", "score", ProjectDataTypes.Number, "10"),
            new ProjectVariable("var-name", "name", ProjectDataTypes.Text, "player"),
            new ProjectVariable("var-ready", "ready", ProjectDataTypes.Boolean, "true"),
        };
        var lists = new[] { new ProjectListDefinition("list-items", "items", ["coin", "gem"]) };
        var saved = new ProjectService(store).SaveProjectData(project, variables, lists);
        var reopened = new ProjectService(store).Open(root);
        Assert.Equal("10", Assert.Single(reopened.Manifest.Variables, v => v.Name == "score").Value);
        Assert.Equal(["coin", "gem"], Assert.Single(reopened.Manifest.Lists, l => l.Name == "items").Values);
        Assert.Equal(saved.Manifest.Variables.Count, reopened.Manifest.Variables.Count);
    }

    [Fact]
    public void InvalidDataIsRejected()
    {
        Assert.Throws<ProjectFormatException>(() => new ProjectService(store).SaveProjectData(project,
            [new ProjectVariable("var-x", "x", ProjectDataTypes.Number, "not-a-number")], []));
        Assert.Throws<ProjectFormatException>(() => new ProjectService(store).SaveProjectData(project,
            [new ProjectVariable("var-x", "x", ProjectDataTypes.Number, "1"),
             new ProjectVariable("var-y", "x", ProjectDataTypes.Text, "y")], []));
    }

    [Fact]
    public async Task PreparedRunContainsManifestData()
    {
        project = new ProjectService(store).CreateScript(project, ProjectScriptKind.Object, "hero").Project;
        project = new ProjectService(store).SaveProjectData(project,
            [new ProjectVariable("var-score", "score", ProjectDataTypes.Number, "4")],
            [new ProjectListDefinition("list-items", "items", ["a"])]);
        var request = await new ProjectRunService(store, bridge).PrepareAsync(
            project, PythonEnvironment.DefaultExecutable, CancellationToken.None);
        Assert.Equal("score", Assert.Single(request.ProjectData!.Variables).Name);
        Assert.Equal("4", Assert.Single(request.ProjectData.Variables).Value);
        Assert.Equal(["a"], Assert.Single(request.ProjectData.Lists).Values);
    }

    [Fact]
    public async Task ManagerRenameUpdatesLiteralReferencesAndDeleteBlocks()
    {
        var created = new ProjectService(store).CreateScript(project, ProjectScriptKind.User, "refs");
        project = created.Project;
        store.WriteScript(root, created.Script.Path,
            "from vac_runtime import get_variable\nprint(get_variable(\"score\"))\n");
        project = new ProjectService(store).SaveProjectData(project,
            [new ProjectVariable("var-score", "score", ProjectDataTypes.Number, "0")], []);
        await using var vm = new MainViewModel(new JsonSettingsStore(Path.Combine(root, "settings.json")),
            new InMemoryRecentProjectsStore(), new ProjectSession(), bridge);
        await vm.OpenProject(root);
        Assert.Single(vm.FindProjectDataReferences("score"));
        var error = vm.SaveProjectData(
            [new ProjectVariable("var-score", "points", ProjectDataTypes.Number, "0")], []);
        Assert.Null(error);
        var rewritten = store.ReadScript(root, created.Script.Path);
        Assert.Contains("get_variable(\"points\")", rewritten);
        Assert.DoesNotContain("get_variable(\"score\")", rewritten);
        error = vm.SaveProjectData([], []);
        Assert.NotNull(error);
        Assert.Contains("points", error);
    }

    [Fact]
    public async Task RenamePreservesUnsavedEditorContent()
    {
        var created = new ProjectService(store).CreateScript(project, ProjectScriptKind.User, "refs");
        project = created.Project;
        store.WriteScript(root, created.Script.Path,
            "from vac_runtime import get_variable\nprint(get_variable(\"score\"))\n");
        project = new ProjectService(store).SaveProjectData(project,
            [new ProjectVariable("var-score", "score", ProjectDataTypes.Number, "0")], []);
        await using var vm = new MainViewModel(new JsonSettingsStore(Path.Combine(root, "settings.json")),
            new InMemoryRecentProjectsStore(), new ProjectSession(), bridge);
        await vm.OpenProject(root);
        var item = Assert.Single(vm.UserScripts);
        await vm.OpenScriptFromExplorerCommand.ExecuteAsync(item);
        vm.PythonCode = "unsaved = True\nprint(get_variable(\"score\"))\n";
        var error = vm.SaveProjectData(
            [new ProjectVariable("var-score", "points", ProjectDataTypes.Number, "0")], []);
        Assert.Null(error);
        Assert.Contains("unsaved = True", vm.PythonCode);
        Assert.Contains("get_variable(\"points\")", vm.PythonCode);
        Assert.DoesNotContain("get_variable(\"score\")", vm.PythonCode);
        Assert.Contains("get_variable(\"score\")", store.ReadScript(root, created.Script.Path));
    }

    [Fact]
    public void ManagerDraftConvertsValuesAndLists()
    {
        var model = new VariableManagerModel(
            [new ProjectVariable("var-score", "score", ProjectDataTypes.Number, "0")],
            [new ProjectListDefinition("list-items", "items", ["a"])]);
        model.Variables[0].Name = "points";
        model.Variables[0].Value = "7";
        model.Lists[0].ValuesText = "a\nb";
        var variables = model.ToVariables();
        var lists = model.ToLists();
        Assert.Equal("points", variables[0].Name);
        Assert.Equal("7", variables[0].Value);
        Assert.Equal(["a", "b"], lists[0].Values);
    }
}
