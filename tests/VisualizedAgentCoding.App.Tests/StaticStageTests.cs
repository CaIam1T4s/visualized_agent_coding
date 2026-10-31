using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Domain.Projects;
using VisualizedAgentCoding.Infrastructure.Projects;
using happy_agent_coding.Services;
using SkiaSharp;

namespace VisualizedAgentCoding.App.Tests;

public sealed class StaticStageTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "vac-stage-" + Guid.NewGuid().ToString("N"));
    private readonly JsonProjectStore store = new();
    private ProjectStageService Service() => new(new JsonStageStore(), new ProjectImageStore());

    [Fact]
    public void NewProjectNeedsNoRuntimeFolderAndExistingCompatibilityFilesArePreserved()
    {
        var project = new ProjectService(store).Create(directory);
        Assert.False(Directory.Exists(Path.Combine(directory, "runtime")));
        Assert.Empty(Service().Load(project).State.Objects);
        Directory.CreateDirectory(Path.Combine(directory, "runtime"));
        var legacy = Path.Combine(directory, "runtime", "element_base.py");
        File.WriteAllText(legacy, "# existing base");
        new ProjectService(store).Open(directory);
        Assert.Equal("# existing base", File.ReadAllText(legacy));
    }

    [Fact]
    public async Task InitialLayoutPersistsWithoutAnOpenEditorAndDoesNotLeakAcrossProjects()
    {
        var service = new ProjectService(store);
        var project = service.Create(directory);
        project = service.CreateScript(project, ProjectScriptKind.Object, "cat").Project;
        project = service.CreateScript(project, ProjectScriptKind.Backdrop, "forest").Project;
        project = service.CreateScript(project, ProjectScriptKind.Backdrop, "night").Project;
        await using var vm = TestMainViewModelFactory.Create(new InMemorySettingsStore());
        await vm.OpenProject(directory);
        Assert.Null(vm.OpenScript);
        var sprite = Assert.Single(vm.StageSprites);
        sprite.X = 70; sprite.Y = 30; sprite.Size = 150; sprite.Direction = -45; sprite.Layer = 8; sprite.IsVisible = false;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal("已保存", vm.EditorState);
        await vm.SelectStageObjectCommand.ExecuteAsync(vm.StageBackdrops[1]);
        var current = vm.CurrentBackdrop!.ObjectId;
        await vm.CloseProject();
        await vm.OpenProject(directory);
        var restored = Assert.Single(vm.StageSprites);
        Assert.Equal((70d, 30d, 150d, -45d, 8, false), (restored.X, restored.Y, restored.Size, restored.Direction, restored.Layer, restored.IsVisible));
        Assert.Equal(current, vm.CurrentBackdrop!.ObjectId);
        vm.ClearStageSelection();
        Assert.Equal(current, vm.CurrentBackdrop!.ObjectId);
        Assert.Equal(274, restored.CanvasLeft);
        Assert.Equal(114, restored.CanvasTop);
        var otherRoot = Path.Combine(directory, "other");
        var other = service.Create(otherRoot);
        service.CreateScript(other, ProjectScriptKind.Object, "cat");
        await vm.OpenProject(otherRoot);
        Assert.Equal(0, Assert.Single(vm.StageSprites).X);
        await vm.OpenProject(directory);
        Assert.Equal(70, Assert.Single(vm.StageSprites).X);
    }

    [Fact]
    public void SceneConflictAndMalformedFilesNeverOverwriteExternalContent()
    {
        var project = new ProjectService(store).Create(directory);
        var scene = Service();
        var initial = scene.Load(project);
        var saved = scene.Save(project, new(), initial.Revision);
        var path = Path.Combine(directory, "scenes", "stage.json");
        File.WriteAllText(path, "external change");
        Assert.Throws<ProjectOperationException>(() => scene.Save(project, new(), saved.Revision));
        Assert.Throws<ProjectFormatException>(() => scene.Load(project));
        Assert.Equal("external change", File.ReadAllText(path));
    }

    [Theory]
    [InlineData("../external.png")]
    [InlineData("assets/images/../external.png")]
    [InlineData("C:/external.png")]
    public void SceneRejectsExternalImageReferences(string image)
    {
        var scene = new StageState { Objects = [new("object", ImagePath: image)] };
        Assert.Throws<ProjectFormatException>(() => StageRules.Validate(scene));
    }

    [Fact]
    public void ImportedImagesRemainInsideTheProjectAfterSourceRemovalAndProjectMove()
    {
        var root = Path.Combine(directory, "project");
        var service = new ProjectService(store);
        var created = service.CreateScript(service.Create(root), ProjectScriptKind.Object, "cat");
        var source = Path.Combine(directory, "picture.png");
        var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a2ioAAAAASUVORK5CYII=");
        File.WriteAllBytes(source, bytes);
        var stage = Service();
        var path = stage.ImportImage(created.Project, created.Script.ObjectId!, source);
        Assert.StartsWith("assets/images/", path);
        stage.Save(created.Project, new() { Objects = [ImageObject(created.Script.ObjectId!, path)] }, null);
        File.Delete(source);
        var moved = Path.Combine(directory, "moved");
        Directory.Move(root, moved);
        var reopened = service.Open(moved);
        Assert.Equal(path, Assert.Single(stage.Load(reopened).State.Objects).ImagePath);
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(moved, path.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public void ImageHeaderIsCheckedBeforeDecodeAndImportUsesTheValidatedSnapshot()
    {
        var service = new ProjectService(store);
        var created = service.CreateScript(service.Create(directory), ProjectScriptKind.Object, "cat");
        using var image = new SKBitmap(24, 12);
        image.Erase(SKColors.Red);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        var source = Path.Combine(directory, "source.png");
        File.WriteAllBytes(source, encoded.ToArray());
        var snapshot = StageImageLoader.Read(source);
        File.WriteAllText(source, "changed after validation");
        var path = Service().ImportImage(created.Project, created.Script.ObjectId!, source, snapshot);
        Assert.Equal(snapshot, File.ReadAllBytes(Path.Combine(directory, path.Replace('/', Path.DirectorySeparatorChar))));
        Assert.Throws<ProjectOperationException>(() => StageImageLoader.Read(source));
        using var wide = new SKBitmap(9000, 1);
        using var tooWide = wide.Encode(SKEncodedImageFormat.Png, 100);
        Assert.Throws<ProjectOperationException>(() => StageImageLoader.Validate(tooWide.ToArray()));
    }

    [Fact]
    public async Task ReloadAfterConflictDiscardsOnlyStageEditsAndInvalidReloadKeepsTheCurrentLayout()
    {
        var service = new ProjectService(store);
        var created = service.CreateScript(service.Create(directory), ProjectScriptKind.Object, "cat");
        await using var vm = TestMainViewModelFactory.Create(new InMemorySettingsStore());
        await vm.OpenProject(directory);
        await vm.SelectStageObjectCommand.ExecuteAsync(Assert.Single(vm.StageSprites));
        var code = vm.PythonCode;
        vm.StageSprites[0].X = 20;
        await vm.SaveCommand.ExecuteAsync(null);
        var snapshot = Service().Load(created.Project);
        Service().Save(created.Project, snapshot.State with { Objects = [new(created.Script.ObjectId!, X: 50)] }, snapshot.Revision);
        vm.StageSprites[0].X = 99;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Contains("外部修改", vm.EditorState);
        Assert.Equal(99, vm.StageSprites[0].X);
        vm.ReloadStage();
        Assert.Equal(50, vm.StageSprites[0].X);
        Assert.Equal(code, vm.PythonCode);
        vm.StageSprites[0].X = 80;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal("已保存", vm.EditorState);
        File.WriteAllText(Path.Combine(directory, "scenes", "stage.json"), "invalid json");
        vm.ReloadStage();
        Assert.Equal(80, vm.StageSprites[0].X);
        Assert.Contains("未重新加载", vm.ProjectResourceMessage);
    }

    [Theory]
    [InlineData(1000, 500, 480, 240)]
    [InlineData(500, 1000, 180, 360)]
    [InlineData(500, 450, 400, 360)]
    [InlineData(120, 80, 120, 80)]
    public void ImageAtOneHundredPercentFitsBothStageAxesWithoutUpscaling(double width, double height, double expectedWidth, double expectedHeight)
    {
        var scale = StageRules.FitImageScale(width, height);
        Assert.Equal(expectedWidth, width * scale, 6);
        Assert.Equal(expectedHeight, height * scale, 6);
    }

    [Fact]
    public void CleanupWaitsForSaveAndPreservesSharedReferencedAndManuallyNamedImages()
    {
        var service = new ProjectService(store);
        var created = service.CreateScript(service.Create(directory), ProjectScriptKind.Object, "cat");
        var stage = Service();
        var source = Path.Combine(directory, "source.png");
        File.WriteAllBytes(source, [1, 2, 3]);
        string Import() => stage.ImportImage(created.Project, created.Script.ObjectId!, source);
        string Absolute(string path) => Path.Combine(directory, path.Replace('/', Path.DirectorySeparatorChar));
        var old = Import();
        var saved = stage.Save(created.Project, new() { Objects = [ImageObject(created.Script.ObjectId!, old)] }, null);
        var next = Import();
        var orphan = Import();
        var explicitReference = Import();
        var unsavedReference = Import();
        var manual = "assets/images/my-picture.png";
        File.Copy(source, Absolute(manual));
        File.WriteAllText(Path.Combine(directory, "scripts", "user", "references.py"), $"path = '{explicitReference}'");
        Assert.Null(stage.CleanUnusedImages(created.Project, [next], saved.Revision, $"path = '{unsavedReference}'"));
        Assert.True(File.Exists(Absolute(old))); // Disk still refers to the old image.
        Assert.False(File.Exists(Absolute(orphan)));
        Assert.True(File.Exists(Absolute(explicitReference)));
        Assert.True(File.Exists(Absolute(unsavedReference)));
        Assert.True(File.Exists(Absolute(manual)));
        saved = stage.Save(created.Project, new() { Objects = [ImageObject(created.Script.ObjectId!, next, true)] }, saved.Revision);
        Assert.Null(stage.CleanUnusedImages(created.Project, [next, old], saved.Revision));
        Assert.True(File.Exists(Absolute(old))); // Another live object can retain it.
        Assert.Null(stage.CleanUnusedImages(created.Project, [next], saved.Revision));
        Assert.False(File.Exists(Absolute(old)));
        Assert.True(File.Exists(Absolute(next)));
        Assert.True(Assert.Single(stage.Load(created.Project).State.Objects).NormalizeImage);
        var newOrphan = Import();
        File.AppendAllText(Path.Combine(directory, "scenes", "stage.json"), " ");
        Assert.NotNull(stage.CleanUnusedImages(created.Project, [next], saved.Revision));
        Assert.True(File.Exists(Absolute(newOrphan)));
    }

    private static StageObjectState ImageObject(string objectId, string path, bool normalize = false) =>
        new(objectId, ImagePath: path, NormalizeImage: normalize,
            Costumes: [new("costume-" + objectId, "默认", path, normalize)], InitialCostumeId: "costume-" + objectId);

    [Fact]
    public void SceneRejectsDuplicateCostumeNamesAndImageMirrorMismatch()
    {
        var costumes = new List<StageCostumeState>
        {
            new("c1", "同名", "assets/images/a.png", true),
            new("c2", "同名", "assets/images/b.png", true),
        };
        var duplicate = new StageState { Objects = [new("object", ImagePath: "assets/images/a.png", NormalizeImage: true,
            Costumes: costumes, InitialCostumeId: "c1")] };
        Assert.Throws<ProjectFormatException>(() => StageRules.Validate(duplicate));
        var mismatched = new StageState { Objects = [new("object", ImagePath: "assets/images/b.png", NormalizeImage: true,
            Costumes: [new("c1", "默认", "assets/images/a.png", true)], InitialCostumeId: "c1")] };
        Assert.Throws<ProjectFormatException>(() => StageRules.Validate(mismatched));
    }

    [Fact]
    public async Task OpeningProjectDeletesOnlyUnreferencedManagedImages()
    {
        var service = new ProjectService(store);
        var created = service.CreateScript(service.Create(directory), ProjectScriptKind.Object, "cat");
        var images = Path.Combine(directory, "assets", "images");
        Directory.CreateDirectory(images);
        var referenced = "assets/images/" + Guid.NewGuid().ToString("N") + ".png";
        var orphan = "assets/images/" + Guid.NewGuid().ToString("N") + ".png";
        var manual = "assets/images/manual.png";
        File.WriteAllBytes(Path.Combine(directory, referenced.Replace('/', Path.DirectorySeparatorChar)), [1]);
        File.WriteAllBytes(Path.Combine(directory, orphan.Replace('/', Path.DirectorySeparatorChar)), [2]);
        File.WriteAllBytes(Path.Combine(directory, manual.Replace('/', Path.DirectorySeparatorChar)), [3]);
        Service().Save(created.Project, new() { Objects = [ImageObject(created.Script.ObjectId!, referenced)] }, null);
        await using var vm = TestMainViewModelFactory.Create(new InMemorySettingsStore());
        await vm.OpenProject(directory);
        Assert.True(File.Exists(Path.Combine(directory, referenced.Replace('/', Path.DirectorySeparatorChar))));
        Assert.False(File.Exists(Path.Combine(directory, orphan.Replace('/', Path.DirectorySeparatorChar))));
        Assert.True(File.Exists(Path.Combine(directory, manual.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public void LegacySceneMigratesToCostumesWithoutRewritingOnOpen()
    {
        var service = new ProjectService(store);
        var created = service.CreateScript(service.Create(directory), ProjectScriptKind.Object, "cat");
        var path = "assets/images/legacy.png";
        Directory.CreateDirectory(Path.Combine(directory, "assets", "images"));
        File.WriteAllBytes(Path.Combine(directory, path.Replace('/', Path.DirectorySeparatorChar)), [1, 2, 3]);
        var scene = Path.Combine(directory, "scenes", "stage.json");
        Directory.CreateDirectory(Path.GetDirectoryName(scene)!);
        var legacy = "{\"version\":1,\"objects\":[{\"objectId\":\"" + created.Script.ObjectId
            + "\",\"imagePath\":\"" + path + "\",\"normalizeImage\":true}]}";
        File.WriteAllText(scene, legacy);
        var stage = Service();
        var loaded = stage.Load(created.Project);
        var item = Assert.Single(loaded.State.Objects);
        Assert.Equal(2, loaded.State.Version);
        var costume = Assert.Single(item.Costumes!);
        Assert.Equal(path, costume.ImagePath);
        Assert.True(costume.NormalizeImage);
        Assert.Equal(costume.Id, item.InitialCostumeId);
        Assert.Equal(legacy, File.ReadAllText(scene));
        stage.Save(created.Project, loaded.State, loaded.Revision);
        Assert.Contains("\"costumes\"", File.ReadAllText(scene));
        Assert.Equal(2, stage.Load(created.Project).State.Version);
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
