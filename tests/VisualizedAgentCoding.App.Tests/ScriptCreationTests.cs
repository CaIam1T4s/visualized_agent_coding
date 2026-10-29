using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using happy_agent_coding.Services;
using happy_agent_coding.ViewModels;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Domain.Projects;
using VisualizedAgentCoding.Infrastructure.Projects;

namespace VisualizedAgentCoding.App.Tests;

/// <summary>Creating scripts from the resource explorer: files, manifest index and category lists.</summary>
public class ScriptCreationTests
{
    [Fact]
    public async Task CreatingAUserScriptWritesTheFileAndIndexesItWithAStableId()
    {
        var root = NewTempDirectory();
        try
        {
            await using var viewModel = await OpenProjectAsync(root);
            var category = CategoryFor(viewModel, ProjectScriptKind.User);

            category.StartCreateCommand.Execute(null);
            category.NewScriptName = "helpers";
            category.ConfirmCreateCommand.Execute(null);

            Assert.True(File.Exists(Path.Combine(root, "scripts", "user", "helpers.py")));
            var script = Assert.Single(category.Scripts);
            Assert.Equal("helpers", script.DisplayName);
            Assert.Equal("scripts/user/helpers.py", script.RelativePath);
            Assert.StartsWith("script-", script.Id);

            var manifest = ReadManifest(root);
            var entry = Assert.Single(manifest.Scripts);
            Assert.Equal(script.Id, entry.Id);
            Assert.Equal(ProjectScriptKind.User, entry.Kind);
            Assert.Null(entry.ObjectId);
            Assert.False(category.IsCreating);
            Assert.Equal("1 项脚本", viewModel.ProjectResourceStatus);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task CreatingAnObjectOrBackdropScriptAlsoAllocatesADistinctObjectId()
    {
        var root = NewTempDirectory();
        try
        {
            await using var viewModel = await OpenProjectAsync(root);

            CreateScript(viewModel, ProjectScriptKind.Object, "hero");
            CreateScript(viewModel, ProjectScriptKind.Backdrop, "forest");

            var manifest = ReadManifest(root);
            Assert.Equal(2, manifest.Scripts.Count);
            var objectIds = manifest.Scripts.Select(script => script.ObjectId).ToArray();
            Assert.All(objectIds, id => Assert.NotNull(id));
            Assert.Equal(2, objectIds.Distinct(StringComparer.Ordinal).Count());
            Assert.Contains("ElementBase", File.ReadAllText(Path.Combine(root, "scripts", "objects", "hero.py")));
            Assert.Contains("BackdropBase", File.ReadAllText(Path.Combine(root, "scripts", "backdrops", "forest.py")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task CreatedScriptsSurviveReopeningTheProject()
    {
        var root = NewTempDirectory();
        try
        {
            await using var creator = await OpenProjectAsync(root);
            CreateScript(creator, ProjectScriptKind.User, "helpers");

            await using var reopened = await ReopenProjectAsync(root);

            var script = Assert.Single(CategoryFor(reopened, ProjectScriptKind.User).Scripts);
            Assert.Equal("helpers", script.DisplayName);
            Assert.Equal(Assert.Single(ReadManifest(root).Scripts).Id, script.Id);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task OpeningAScriptLoadsItsContentsAndMarksTheRow()
    {
        var root = NewTempDirectory();
        try
        {
            await using var viewModel = await OpenProjectAsync(root);
            CreateScript(viewModel, ProjectScriptKind.User, "helpers");
            var item = Assert.Single(CategoryFor(viewModel, ProjectScriptKind.User).Scripts);

            await viewModel.OpenScriptFromExplorerCommand.ExecuteAsync(item);

            Assert.Equal(File.ReadAllText(Path.Combine(root, "scripts", "user", "helpers.py")), viewModel.PythonCode);
            Assert.Equal("helpers.py", viewModel.FileName);
            Assert.Equal(item.Id, viewModel.OpenScript?.Id);
            Assert.True(viewModel.IsEditorVisible);
            Assert.True(Assert.Single(CategoryFor(viewModel, ProjectScriptKind.User).Scripts).IsOpen);
            Assert.DoesNotContain("main.py", viewModel.UserScripts.Select(script => script.RelativePath));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SwitchingScriptsSavesThePreviousBuffer()
    {
        var root = NewTempDirectory();
        try
        {
            await using var viewModel = await OpenProjectAsync(root);
            CreateScript(viewModel, ProjectScriptKind.User, "helpers");
            CreateScript(viewModel, ProjectScriptKind.Object, "hero");
            var helpers = Assert.Single(CategoryFor(viewModel, ProjectScriptKind.User).Scripts);
            var hero = Assert.Single(CategoryFor(viewModel, ProjectScriptKind.Object).Scripts);

            await viewModel.OpenScriptFromExplorerCommand.ExecuteAsync(helpers);
            viewModel.PythonCode = "print('helpers')\n";
            await viewModel.OpenScriptFromExplorerCommand.ExecuteAsync(hero);

            Assert.Equal("print('helpers')\n", File.ReadAllText(Path.Combine(root, "scripts", "user", "helpers.py")));
            Assert.Contains("ElementBase", viewModel.PythonCode);
            Assert.Equal("hero.py", viewModel.FileName);
            Assert.False(Assert.Single(CategoryFor(viewModel, ProjectScriptKind.User).Scripts).IsOpen);
            Assert.True(Assert.Single(CategoryFor(viewModel, ProjectScriptKind.Object).Scripts).IsOpen);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SavingWritesTheOpenScriptAndLeavesTheEntryScriptAlone()
    {
        var root = NewTempDirectory();
        try
        {
            await using var viewModel = await OpenProjectAsync(root);
            CreateScript(viewModel, ProjectScriptKind.User, "helpers");
            var originalMain = File.ReadAllText(Path.Combine(root, "scripts", "main.py"));
            await viewModel.OpenScriptFromExplorerCommand.ExecuteAsync(
                Assert.Single(CategoryFor(viewModel, ProjectScriptKind.User).Scripts));
            viewModel.PythonCode = "print('saved helpers')\n";

            await viewModel.SaveCommand.ExecuteAsync(null);

            Assert.Equal("已保存", viewModel.EditorState);
            Assert.Equal("print('saved helpers')\n", File.ReadAllText(Path.Combine(root, "scripts", "user", "helpers.py")));
            Assert.Equal(originalMain, File.ReadAllText(Path.Combine(root, "scripts", "main.py")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ClosingTheProjectClearsEditorStageAndExplorerState()
    {
        var root = NewTempDirectory();
        try
        {
            await using var viewModel = await OpenProjectAsync(root);
            CreateScript(viewModel, ProjectScriptKind.User, "helpers");
            CreateScript(viewModel, ProjectScriptKind.Object, "hero");
            CreateScript(viewModel, ProjectScriptKind.Backdrop, "forest");
            await viewModel.SelectStageObjectCommand.ExecuteAsync(Assert.Single(viewModel.StageSprites));
            viewModel.StageSprites[0].X = 40;
            var userCategory = CategoryFor(viewModel, ProjectScriptKind.User);
            userCategory.StartCreateCommand.Execute(null);
            viewModel.ResourceSearchQuery = "hero";
            Assert.NotNull(viewModel.OpenScript);
            Assert.NotNull(viewModel.SelectedSprite);
            Assert.True(userCategory.IsCreating);
            Assert.True(CategoryFor(viewModel, ProjectScriptKind.Object).IsExpanded);

            await viewModel.CloseProject();

            Assert.Equal(string.Empty, viewModel.PythonCode);
            Assert.Equal(string.Empty, viewModel.FileName);
            Assert.Null(viewModel.OpenScript);
            Assert.False(viewModel.IsEditorVisible);
            Assert.False(viewModel.ShowStageCanvas);
            Assert.True(viewModel.ShowOpenProjectPrompt);
            Assert.Equal("尚未打开项目", viewModel.EditorEmptyPromptTitle);
            Assert.Empty(viewModel.StageSprites);
            Assert.Empty(viewModel.StageBackdrops);
            Assert.Null(viewModel.SelectedSprite);
            Assert.Null(viewModel.SelectedBackdrop);
            Assert.Null(viewModel.CurrentBackdrop);
            Assert.False(viewModel.HasOpenProject);
            Assert.Equal(3, viewModel.ScriptCategories.Count);
            Assert.All(viewModel.ScriptCategories, category =>
            {
                Assert.False(category.CanCreate);
                Assert.True(category.IsEmpty);
                Assert.False(category.IsExpanded);
                Assert.False(category.IsCreating);
            });
            Assert.Empty(viewModel.ResourceSearchQuery);
            Assert.Equal("打开项目后显示脚本", viewModel.ProjectResourceStatus);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task RefusedNamesKeepTheEditorOpenAndSurfaceTheReason()
    {
        var root = NewTempDirectory();
        try
        {
            await using var viewModel = await OpenProjectAsync(root);
            var category = CategoryFor(viewModel, ProjectScriptKind.User);
            CreateScript(viewModel, ProjectScriptKind.User, "helpers");

            category.StartCreateCommand.Execute(null);
            category.NewScriptName = "helpers";
            category.ConfirmCreateCommand.Execute(null);

            Assert.True(category.IsCreating);
            Assert.True(category.HasError);
            Assert.Single(category.Scripts);

            category.NewScriptName = "bad name!";
            category.ConfirmCreateCommand.Execute(null);

            Assert.True(category.HasError);
            Assert.Single(category.Scripts);
            Assert.Single(ReadManifest(root).Scripts);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task CreatingIsUnavailableUntilAProjectIsOpen()
    {
        await using var viewModel = TestMainViewModelFactory.Create(new TestSettingsStore());

        Assert.All(viewModel.ScriptCategories, category => Assert.False(category.CanCreate));

        var categoryUnderTest = CategoryFor(viewModel, ProjectScriptKind.User);
        categoryUnderTest.StartCreateCommand.Execute(null);

        Assert.False(categoryUnderTest.IsCreating);
    }

    [Fact]
    public async Task SwitchingExplorerModeLeavesIndexedScriptsIntact()
    {
        var root = NewTempDirectory();
        try
        {
            await using var viewModel = await OpenProjectAsync(root);
            CreateScript(viewModel, ProjectScriptKind.User, "helpers");
            Assert.True(viewModel.IsResourceFilterMode);
            Assert.False(viewModel.IsObjectManagerMode);

            viewModel.ResourceExplorerModeIndex = 1;

            Assert.False(viewModel.IsResourceFilterMode);
            Assert.True(viewModel.IsObjectManagerMode);
            Assert.Single(CategoryFor(viewModel, ProjectScriptKind.User).Scripts);
            Assert.Empty(viewModel.StageSprites);
            Assert.Empty(viewModel.StageBackdrops);

            viewModel.ResourceExplorerModeIndex = 0;

            Assert.True(viewModel.IsResourceFilterMode);
            Assert.Single(CategoryFor(viewModel, ProjectScriptKind.User).Scripts);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ObjectAndBackdropScriptsAppearInTheObjectManager()
    {
        var root = NewTempDirectory();
        try
        {
            await using var viewModel = await OpenProjectAsync(root);
            CreateScript(viewModel, ProjectScriptKind.User, "helpers");
            CreateScript(viewModel, ProjectScriptKind.Object, "hero");
            CreateScript(viewModel, ProjectScriptKind.Backdrop, "forest");

            var sprite = Assert.Single(viewModel.StageSprites);
            var backdrop = Assert.Single(viewModel.StageBackdrops);
            Assert.Equal("hero", sprite.DisplayName);
            Assert.Equal("forest", backdrop.DisplayName);
            Assert.NotEqual(sprite.ObjectId, backdrop.ObjectId);
            Assert.Equal(100, sprite.Size);
            Assert.Equal(90, sprite.Direction);
            Assert.True(sprite.IsVisible);
            Assert.Null(viewModel.SelectedSprite);
            Assert.Null(viewModel.SelectedBackdrop);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SelectingAStageObjectOpensItsScriptAndShowsProperties()
    {
        var root = NewTempDirectory();
        try
        {
            await using var viewModel = await OpenProjectAsync(root);
            CreateScript(viewModel, ProjectScriptKind.Object, "hero");
            CreateScript(viewModel, ProjectScriptKind.Backdrop, "forest");
            var sprite = Assert.Single(viewModel.StageSprites);

            await viewModel.SelectStageObjectCommand.ExecuteAsync(sprite);

            Assert.Equal("hero.py", viewModel.FileName);
            Assert.True(viewModel.IsEditorVisible);
            Assert.True(Assert.Single(viewModel.StageSprites).IsSelected);
            Assert.Same(viewModel.StageSprites[0], viewModel.SelectedSprite);
            Assert.Null(viewModel.SelectedBackdrop);
            Assert.Equal(0, viewModel.ObjectManagerTabIndex);
            Assert.Contains("ElementBase", viewModel.PythonCode);

            await viewModel.SelectStageObjectCommand.ExecuteAsync(Assert.Single(viewModel.StageBackdrops));

            Assert.Equal("forest.py", viewModel.FileName);
            Assert.False(Assert.Single(viewModel.StageSprites).IsSelected);
            Assert.True(Assert.Single(viewModel.StageBackdrops).IsSelected);
            Assert.Null(viewModel.SelectedSprite);
            Assert.Same(viewModel.StageBackdrops[0], viewModel.SelectedBackdrop);
            Assert.Equal(1, viewModel.ObjectManagerTabIndex);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task OpeningAUserScriptClearsTheStageObjectSelection()
    {
        var root = NewTempDirectory();
        try
        {
            await using var viewModel = await OpenProjectAsync(root);
            CreateScript(viewModel, ProjectScriptKind.User, "helpers");
            CreateScript(viewModel, ProjectScriptKind.Object, "hero");
            await viewModel.SelectStageObjectCommand.ExecuteAsync(Assert.Single(viewModel.StageSprites));

            await viewModel.OpenScriptFromExplorerCommand.ExecuteAsync(
                Assert.Single(CategoryFor(viewModel, ProjectScriptKind.User).Scripts));

            Assert.False(Assert.Single(viewModel.StageSprites).IsSelected);
            Assert.Null(viewModel.SelectedSprite);
            Assert.Equal("helpers.py", viewModel.FileName);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task AddingASpriteFromTheObjectManagerCreatesAndSelectsIt()
    {
        var root = NewTempDirectory();
        try
        {
            await using var viewModel = await OpenProjectAsync(root);

            await viewModel.AddSpriteCommand.ExecuteAsync(null);
            await viewModel.AddSpriteCommand.ExecuteAsync(null);
            await viewModel.AddBackdropCommand.ExecuteAsync(null);

            Assert.Equal(2, viewModel.StageSprites.Count);
            Assert.Single(viewModel.StageBackdrops);
            Assert.Equal("sprite", viewModel.StageSprites[0].DisplayName);
            Assert.Equal("sprite2", viewModel.StageSprites[1].DisplayName);
            Assert.Equal("backdrop", Assert.Single(viewModel.StageBackdrops).DisplayName);
            Assert.Equal("backdrop.py", viewModel.FileName);
            Assert.True(Assert.Single(viewModel.StageBackdrops).IsSelected);
            Assert.Equal(1, viewModel.ObjectManagerTabIndex);
            Assert.True(File.Exists(Path.Combine(root, "scripts", "objects", "sprite.py")));
            Assert.True(File.Exists(Path.Combine(root, "scripts", "backdrops", "backdrop.py")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task StageSpritesExposeCanvasGeometryForThePreview()
    {
        var root = NewTempDirectory();
        try
        {
            await using var viewModel = await OpenProjectAsync(root);
            CreateScript(viewModel, ProjectScriptKind.Object, "hero");
            var sprite = Assert.Single(viewModel.StageSprites);

            Assert.Equal(48, sprite.SpriteWidth);
            Assert.Equal(216, sprite.CanvasLeft);
            Assert.Equal(156, sprite.CanvasTop);
            Assert.Equal(0, sprite.Rotation);
            Assert.Equal(1, sprite.StageOpacity);

            sprite.X = 40;
            sprite.Y = -20;
            sprite.Size = 200;
            sprite.Direction = 180;
            sprite.IsVisible = false;

            Assert.Equal(96, sprite.SpriteWidth);
            Assert.Equal(232, sprite.CanvasLeft);
            Assert.Equal(152, sprite.CanvasTop);
            Assert.Equal(90, sprite.Rotation);
            Assert.Equal(0.4, sprite.StageOpacity);

            sprite.SetPositionFromCanvasTopLeft(232, 152);
            Assert.Equal(40, sprite.X);
            Assert.Equal(-20, sprite.Y);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task CurrentBackdropFollowsSelectionThenFallsBackToTheFirstItem()
    {
        var root = NewTempDirectory();
        try
        {
            await using var viewModel = await OpenProjectAsync(root);
            CreateScript(viewModel, ProjectScriptKind.Backdrop, "forest");
            CreateScript(viewModel, ProjectScriptKind.Backdrop, "night");

            Assert.Same(viewModel.StageBackdrops[0], viewModel.CurrentBackdrop);

            await viewModel.SelectStageObjectCommand.ExecuteAsync(viewModel.StageBackdrops[1]);
            Assert.Same(viewModel.StageBackdrops[1], viewModel.CurrentBackdrop);
            Assert.Equal("night.py", viewModel.FileName);

            viewModel.ClearStageSelection();
            Assert.Null(viewModel.SelectedBackdrop);
            Assert.Same(viewModel.StageBackdrops[0], viewModel.CurrentBackdrop);
            Assert.Equal("night.py", viewModel.FileName);
            Assert.True(viewModel.IsEditorVisible);

            await viewModel.SelectStageObjectCommand.ExecuteAsync(viewModel.CurrentBackdrop);
            Assert.Same(viewModel.StageBackdrops[0], viewModel.SelectedBackdrop);
            Assert.Equal("forest.py", viewModel.FileName);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ClearingStageSelectionDoesNotCloseTheOpenScript()
    {
        var root = NewTempDirectory();
        try
        {
            await using var viewModel = await OpenProjectAsync(root);
            CreateScript(viewModel, ProjectScriptKind.Object, "hero");
            await viewModel.SelectStageObjectCommand.ExecuteAsync(Assert.Single(viewModel.StageSprites));

            viewModel.ClearStageSelection();

            Assert.False(Assert.Single(viewModel.StageSprites).IsSelected);
            Assert.Null(viewModel.SelectedSprite);
            Assert.Equal("hero.py", viewModel.FileName);
            Assert.True(viewModel.IsEditorVisible);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task OpeningAProjectCollapsesEveryCategory()
    {
        var root = NewTempDirectory();
        try
        {
            await using var viewModel = await OpenProjectAsync(root);
            CreateScript(viewModel, ProjectScriptKind.User, "helpers");
            CategoryFor(viewModel, ProjectScriptKind.User).IsExpanded = true;
            CategoryFor(viewModel, ProjectScriptKind.Object).IsExpanded = true;

            await using var reopened = await ReopenProjectAsync(root);

            Assert.All(reopened.ScriptCategories, category => Assert.False(category.IsExpanded));
            Assert.Equal("1 项脚本", reopened.ProjectResourceStatus);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ClearingTheSearchRestoresTheExpansionTheUserChose()
    {
        var root = NewTempDirectory();
        try
        {
            await using var viewModel = await OpenProjectAsync(root);
            CreateScript(viewModel, ProjectScriptKind.User, "helpers");
            CreateScript(viewModel, ProjectScriptKind.Object, "hero");
            var userCategory = CategoryFor(viewModel, ProjectScriptKind.User);
            var objectCategory = CategoryFor(viewModel, ProjectScriptKind.Object);
            var backdropCategory = CategoryFor(viewModel, ProjectScriptKind.Backdrop);

            userCategory.IsExpanded = true;
            objectCategory.IsExpanded = false;
            backdropCategory.IsExpanded = false;

            viewModel.ResourceSearchQuery = "her";

            Assert.False(userCategory.IsExpanded);
            Assert.True(objectCategory.IsExpanded);
            Assert.False(backdropCategory.IsExpanded);

            viewModel.ResourceSearchQuery = string.Empty;

            Assert.True(userCategory.IsExpanded);
            Assert.False(objectCategory.IsExpanded);
            Assert.False(backdropCategory.IsExpanded);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SearchingACategoryThatIsCollapsedOpensItSoHitsStayVisible()
    {
        var root = NewTempDirectory();
        try
        {
            await using var viewModel = await OpenProjectAsync(root);
            CreateScript(viewModel, ProjectScriptKind.Object, "hero");
            var objectCategory = CategoryFor(viewModel, ProjectScriptKind.Object);
            objectCategory.IsExpanded = false;

            viewModel.ResourceSearchQuery = "her";

            Assert.True(objectCategory.IsExpanded);
            Assert.Single(objectCategory.Scripts);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static async Task<MainViewModel> OpenProjectAsync(string root)
    {
        CreateService().Create(root, "Demo");
        var viewModel = TestMainViewModelFactory.Create(new TestSettingsStore());
        await viewModel.OpenProject(root);
        return viewModel;
    }

    private static async Task<MainViewModel> ReopenProjectAsync(string root)
    {
        var viewModel = TestMainViewModelFactory.Create(new TestSettingsStore());
        await viewModel.OpenProject(root);
        return viewModel;
    }

    private static ScriptCategory CategoryFor(MainViewModel viewModel, ProjectScriptKind kind) =>
        Assert.Single(viewModel.ScriptCategories, category => category.Kind == kind);

    private static void CreateScript(MainViewModel viewModel, ProjectScriptKind kind, string name)
    {
        var category = CategoryFor(viewModel, kind);
        category.StartCreateCommand.Execute(null);
        category.NewScriptName = name;
        category.ConfirmCreateCommand.Execute(null);
        Assert.False(category.HasError);
    }

    private static ProjectManifest ReadManifest(string root) =>
        JsonSerializer.Deserialize<ProjectManifest>(
            File.ReadAllText(Path.Combine(root, "project.json")),
            ManifestJsonOptions)!;

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static ProjectService CreateService() => new(new JsonProjectStore());

    private static string NewTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"visualized-project-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private sealed class TestSettingsStore : ISettingsStore
    {
        private AppSettings settings = new();

        public AppSettings Load() => settings;

        public void Save(AppSettings value) => settings = value;
    }
}
