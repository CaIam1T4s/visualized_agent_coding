using System.Collections.Concurrent;
using happy_agent_coding.ViewModels;
using happy_agent_coding.Services;
using SkiaSharp;
using VisualizedAgentCoding.Application.Editor.ObjectScripts;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Application.Runtime;
using VisualizedAgentCoding.Domain.Projects;
using VisualizedAgentCoding.Infrastructure.Projects;
using VisualizedAgentCoding.Infrastructure.Python;

namespace VisualizedAgentCoding.App.Tests;

public sealed class CostumeTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "vac-costume-" + Guid.NewGuid().ToString("N"));
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

    private async Task<ObjectScriptDocument> Inspect(string scriptId, string source) =>
        await new ObjectScriptDocumentService(bridge).InspectAsync(project, scriptId, 0, source);

    private static ObjectScriptDocument Body(ObjectScriptDocument document, string regionId, string text)
    {
        var projection = new MultiRegionProjection(document);
        projection.UpdateRegions(new Dictionary<string, string> { [regionId] = text });
        return projection.Document;
    }

    private string CreateImage(string name, int width, int height, SKColor color)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color);
        using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        var path = Path.Combine(root, name);
        File.WriteAllBytes(path, encoded.ToArray());
        return path;
    }

    [Fact]
    public void SnapshotKeepsTheSelectedInitialCostumeAndMirrorFields()
    {
        var item = new StageObjectItem("script", "object", ProjectScriptKind.Object, "cat", "scripts/objects/cat.py");
        var first = new StageCostumeItem("c1", "默认", "assets/images/a.png", true);
        var second = new StageCostumeItem("c2", "开心", "assets/images/b.png", false);
        item.AddCostume(first, select: true);
        item.AddCostume(second, select: false);
        Assert.Same(first, item.SelectedCostume);
        Assert.Equal("c1", item.Snapshot().InitialCostumeId);
        item.SelectInitialCostume(second);
        var state = item.Snapshot();
        Assert.Equal("c2", state.InitialCostumeId);
        Assert.Equal("assets/images/b.png", state.ImagePath);
        Assert.False(state.NormalizeImage);
        Assert.Equal(2, state.Costumes!.Count);
        item.Dispose();
    }

    [Fact]
    public async Task RuntimeSwitchLoadsAnotherCostumeAndReturnRestoresTheInitialOne()
    {
        var created = new ProjectService(store).CreateScript(project, ProjectScriptKind.Object, "cat");
        project = created.Project;
        var document = await Inspect(created.Script.Id, store.ReadScript(root, created.Script.Path));
        document = ObjectScriptFeatures.AddInitialize(document);
        document = Body(document, "initialize", "self.switch_costume('开心')\n");
        document = Body(document, "start", "print('active', self.costume)\n");
        store.WriteScript(root, created.Script.Path, document.FullSource);

        var stage = new ProjectStageService(new JsonStageStore(), new ProjectImageStore());
        var first = stage.ImportImage(project, created.Script.ObjectId!, CreateImage("first.png", 20, 20, SKColors.Red));
        var second = stage.ImportImage(project, created.Script.ObjectId!, CreateImage("second.png", 60, 20, SKColors.Blue));
        stage.Save(project, new StageState
        {
            Objects =
            [
                new(created.Script.ObjectId!, Size: 100, ImagePath: first, NormalizeImage: true,
                    Costumes:
                    [
                        new("costume-first", "默认", first, true),
                        new("costume-second", "开心", second, true),
                    ], InitialCostumeId: "costume-first")
            ]
        }, null);

        Assert.True(File.Exists(Path.Combine(root, first.Replace('/', Path.DirectorySeparatorChar))));
        await using var vm = new MainViewModel(new JsonSettingsStore(Path.Combine(root, "settings.json")),
            new InMemoryRecentProjectsStore(), new ProjectSession(), bridge);
        await vm.OpenProject(root);
        Assert.True(File.Exists(Path.Combine(root, first.Replace('/', Path.DirectorySeparatorChar))), "first image deleted on open");
        var item = Assert.Single(vm.StageSprites);
        Assert.Equal("默认", item.CostumeName);
        Assert.Equal(first, item.ImagePath);

        var run = vm.RunCommand.ExecuteAsync(null);
        await TestMainViewModelFactory.WaitForInteractiveAsync(vm, run);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await TestMainViewModelFactory.PumpUntilAsync(vm,
            () => item.CostumeName == "开心" && item.RuntimeImagePath == second, run, timeout.Token);
        Assert.Equal("开心", item.CostumeName);

        await vm.StopPythonAsync();
        await run;
        Assert.Equal("开心", item.CostumeName);
        vm.ReturnToInitialStageCommand.Execute(null);
        Assert.Equal("默认", item.CostumeName);
        Assert.Equal(first, item.ImagePath);
    }
}
