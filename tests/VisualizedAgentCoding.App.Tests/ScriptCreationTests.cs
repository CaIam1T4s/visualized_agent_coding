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
    public void CreatingAUserScriptWritesTheFileAndIndexesItWithAStableId()
    {
        var root = NewTempDirectory();
        try
        {
            var viewModel = OpenProject(root);
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
    public void CreatingAnObjectOrBackdropScriptAlsoAllocatesADistinctObjectId()
    {
        var root = NewTempDirectory();
        try
        {
            var viewModel = OpenProject(root);

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
    public void CreatedScriptsSurviveReopeningTheProject()
    {
        var root = NewTempDirectory();
        try
        {
            var creator = OpenProject(root);
            CreateScript(creator, ProjectScriptKind.User, "helpers");

            var reopened = ReopenProject(root);

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
    public void RefusedNamesKeepTheEditorOpenAndSurfaceTheReason()
    {
        var root = NewTempDirectory();
        try
        {
            var viewModel = OpenProject(root);
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
    public void CreatingIsUnavailableUntilAProjectIsOpen()
    {
        var viewModel = new MainViewModel(new TestSettingsStore());

        Assert.All(viewModel.ScriptCategories, category => Assert.False(category.CanCreate));

        var categoryUnderTest = CategoryFor(viewModel, ProjectScriptKind.User);
        categoryUnderTest.StartCreateCommand.Execute(null);

        Assert.False(categoryUnderTest.IsCreating);
    }

    [Fact]
    public void CategoriesFollowTheProjectIndexNotTheDisk()
    {
        var root = NewTempDirectory();
        try
        {
            var viewModel = OpenProject(root);
            Assert.All(viewModel.ScriptCategories, category => Assert.True(category.CanCreate));
            Assert.All(viewModel.ScriptCategories, category => Assert.True(category.IsEmpty));

            CreateScript(viewModel, ProjectScriptKind.User, "helpers");
            CreateScript(viewModel, ProjectScriptKind.Backdrop, "forest");

            viewModel.ResourceSearchQuery = "forest";

            Assert.Empty(CategoryFor(viewModel, ProjectScriptKind.User).Scripts);
            Assert.Single(CategoryFor(viewModel, ProjectScriptKind.Backdrop).Scripts);
            Assert.Equal("1 项脚本", viewModel.ProjectResourceStatus);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ObjectManagerModeIsAPlaceholderThatDoesNotDisturbScripts()
    {
        var root = NewTempDirectory();
        try
        {
            var viewModel = OpenProject(root);
            CreateScript(viewModel, ProjectScriptKind.User, "helpers");
            Assert.True(viewModel.IsResourceFilterMode);
            Assert.False(viewModel.IsObjectManagerMode);

            viewModel.ResourceExplorerModeIndex = 1;

            Assert.False(viewModel.IsResourceFilterMode);
            Assert.True(viewModel.IsObjectManagerMode);
            Assert.Single(CategoryFor(viewModel, ProjectScriptKind.User).Scripts);

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
    public void ClosingTheProjectDisablesCreationButKeepsTheCategories()
    {
        var root = NewTempDirectory();
        try
        {
            var viewModel = OpenProject(root);
            CreateScript(viewModel, ProjectScriptKind.User, "helpers");

            viewModel.CloseProject();

            Assert.False(viewModel.HasOpenProject);
            Assert.All(viewModel.ScriptCategories, category => Assert.False(category.CanCreate));
            Assert.All(viewModel.ScriptCategories, category => Assert.True(category.IsEmpty));
            Assert.Equal("打开项目后显示脚本", viewModel.ProjectResourceStatus);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void OpeningAProjectCollapsesEveryCategory()
    {
        var root = NewTempDirectory();
        try
        {
            var viewModel = OpenProject(root);
            CreateScript(viewModel, ProjectScriptKind.User, "helpers");
            CategoryFor(viewModel, ProjectScriptKind.User).IsExpanded = true;
            CategoryFor(viewModel, ProjectScriptKind.Object).IsExpanded = true;

            var reopened = ReopenProject(root);

            Assert.All(reopened.ScriptCategories, category => Assert.False(category.IsExpanded));
            Assert.Equal("1 项脚本", reopened.ProjectResourceStatus);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ClosingTheProjectCollapsesCategoriesAndClearsTheFilter()
    {
        var root = NewTempDirectory();
        try
        {
            var viewModel = OpenProject(root);
            CreateScript(viewModel, ProjectScriptKind.User, "helpers");
            CreateScript(viewModel, ProjectScriptKind.Object, "hero");
            var userCategory = CategoryFor(viewModel, ProjectScriptKind.User);
            userCategory.StartCreateCommand.Execute(null);
            Assert.True(userCategory.IsExpanded);

            viewModel.ResourceSearchQuery = "hero";
            Assert.True(CategoryFor(viewModel, ProjectScriptKind.Object).IsExpanded);

            viewModel.CloseProject();

            Assert.Empty(viewModel.ResourceSearchQuery);
            Assert.All(viewModel.ScriptCategories, category => Assert.False(category.IsExpanded));
            Assert.All(viewModel.ScriptCategories, category => Assert.False(category.IsCreating));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ClearingTheSearchRestoresTheExpansionTheUserChose()
    {
        var root = NewTempDirectory();
        try
        {
            var viewModel = OpenProject(root);
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
    public void SearchingACategoryThatIsCollapsedOpensItSoHitsStayVisible()
    {
        var root = NewTempDirectory();
        try
        {
            var viewModel = OpenProject(root);
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

    private static MainViewModel OpenProject(string root)
    {
        CreateService().Create(root, "Demo");
        var viewModel = new MainViewModel(new TestSettingsStore());
        viewModel.OpenProject(root);
        return viewModel;
    }

    private static MainViewModel ReopenProject(string root)
    {
        var viewModel = new MainViewModel(new TestSettingsStore());
        viewModel.OpenProject(root);
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
