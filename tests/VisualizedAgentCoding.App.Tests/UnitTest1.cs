using System;
using System.IO;
using happy_agent_coding.Services;
using happy_agent_coding.ViewModels;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Infrastructure.Projects;

namespace VisualizedAgentCoding.App.Tests;

public class MainViewModelTests
{
    [Fact]
    public void StartsWithoutAProjectOrEditorDocument()
    {
        var viewModel = new MainViewModel();

        Assert.Equal(string.Empty, viewModel.PythonCode);
        Assert.Equal("1", viewModel.LineNumbers);
        Assert.Equal(string.Empty, viewModel.FileName);
        Assert.False(viewModel.HasOpenProject);
        Assert.False(viewModel.IsEditorVisible);
        Assert.True(viewModel.ShowOpenProjectPrompt);
        Assert.Equal("尚未打开项目", viewModel.EditorEmptyPromptTitle);
        Assert.Equal("打开或新建一个项目后，即可在此编辑脚本。", viewModel.EditorEmptyPromptMessage);
        Assert.False(viewModel.ShowStageCanvas);
    }

    [Fact]
    public void JsonRecentProjectsStoreRoundTripsRecentProjectEntries()
    {
        var root = Path.Combine(Path.GetTempPath(), $"recent-projects-{Guid.NewGuid():N}");
        var filePath = Path.Combine(root, "recent-projects.json");
        try
        {
            var entry = new RecentProjectEntry("Demo", Path.Combine(root, "project"), DateTimeOffset.Now);
            var store = new JsonRecentProjectsStore(filePath);
            store.Save([entry]);

            var loaded = store.Load();

            Assert.Single(loaded);
            Assert.Equal(entry, loaded[0]);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void OpeningAProjectPersistsRecentEntryForTheNextViewModel()
    {
        var root = Path.Combine(Path.GetTempPath(), $"recent-projects-{Guid.NewGuid():N}");
        var projectRoot = Path.Combine(root, "project");
        var filePath = Path.Combine(root, "recent-projects.json");
        try
        {
            var store = new JsonRecentProjectsStore(filePath);
            var viewModel = new MainViewModel(new InMemorySettingsStore(), store, new ProjectSession());
            viewModel.CreateProject(projectRoot, "Demo");

            var reopened = new MainViewModel(new InMemorySettingsStore(), store, new ProjectSession());

            var recent = Assert.Single(reopened.RecentProjectsToday);
            Assert.Equal("Demo", recent.Name);
            Assert.Equal(projectRoot, recent.Path);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void StartupRemovesMissingAndInvalidProjectsFromRecentList()
    {
        var root = Path.Combine(Path.GetTempPath(), $"recent-project-cleanup-{Guid.NewGuid():N}");
        var validRoot = Path.Combine(root, "valid");
        var missingRoot = Path.Combine(root, "missing");
        try
        {
            var projectSession = new ProjectSession();
            var validProject = projectSession.Create(validRoot, "Valid");
            var recentStore = new InMemoryRecentProjectsStore(
            [
                new RecentProjectEntry("Missing", missingRoot, DateTimeOffset.Now),
                new RecentProjectEntry("Invalid", root, DateTimeOffset.Now.AddMinutes(-1)),
                new RecentProjectEntry(validProject.Name, validRoot, DateTimeOffset.Now.AddMinutes(-2)),
            ]);

            var viewModel = new MainViewModel(
                new InMemorySettingsStore(), recentStore, projectSession);

            var recent = Assert.Single(viewModel.RecentProjectsToday);
            Assert.Equal("Valid", recent.Name);
            Assert.Equal(validRoot, recent.Path);
            var persisted = Assert.Single(recentStore.Projects);
            Assert.Equal(validRoot, persisted.Path);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SavingWithoutAProjectReportsThatNoProjectIsOpen()
    {
        var viewModel = new MainViewModel();

        viewModel.SaveCommand.Execute(null);

        Assert.Equal("尚未打开项目", viewModel.EditorState);
    }

    [Fact]
    public void OpeningAProjectLeavesTheEditorEmptyAndDoesNotExposeTheEntryScript()
    {
        var root = Path.Combine(Path.GetTempPath(), $"visualized-project-{Guid.NewGuid():N}");
        try
        {
            var viewModel = TestMainViewModelFactory.Create(new InMemorySettingsStore());
            viewModel.CreateProject(root, "Demo");

            Assert.Equal("Demo", viewModel.ProjectName);
            Assert.True(Directory.Exists(Path.Combine(root, "scripts", "user")));
            Assert.Contains("def main():", File.ReadAllText(Path.Combine(root, "scripts", "main.py")));
            Assert.Equal(string.Empty, viewModel.PythonCode);
            Assert.Equal(string.Empty, viewModel.FileName);
            Assert.Null(viewModel.OpenScript);
            Assert.False(viewModel.IsEditorVisible);
            Assert.True(viewModel.ShowStageCanvas);
            Assert.False(viewModel.ShowOpenProjectPrompt);
            Assert.Equal("尚未打开脚本", viewModel.EditorEmptyPromptTitle);
            Assert.Contains("资源管理器", viewModel.EditorEmptyPromptMessage);
            Assert.Empty(viewModel.UserScripts);
            Assert.Empty(viewModel.ObjectScripts);
            Assert.Empty(viewModel.BackdropScripts);

            viewModel.PythonCode = "print('saved')";
            viewModel.SaveCommand.Execute(null);

            Assert.Equal("尚未打开脚本", viewModel.EditorState);
            Assert.Contains("def main():", File.ReadAllText(Path.Combine(root, "scripts", "main.py")));

            var reopened = TestMainViewModelFactory.Create(new InMemorySettingsStore());
            reopened.OpenProject(root);

            Assert.Equal(string.Empty, reopened.PythonCode);
            Assert.Null(reopened.OpenScript);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResourceExplorerGroupsOnlyScriptsFromTheThreeScriptDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), $"visualized-project-{Guid.NewGuid():N}");
        try
        {
            new happy_agent_coding.Services.ProjectSession().Create(root, "Demo");
            File.WriteAllText(Path.Combine(root, "scripts", "user", "helpers.py"), "pass");
            File.WriteAllText(Path.Combine(root, "scripts", "objects", "hero.py"), "pass");
            File.WriteAllText(Path.Combine(root, "scripts", "backdrops", "forest.py"), "pass");
            File.WriteAllText(Path.Combine(root, "scripts", "user", "notes.json"), "{}");
            Directory.CreateDirectory(Path.Combine(root, "assets", "images"));
            File.WriteAllText(Path.Combine(root, "assets", "images", "hero.png"), "image");
            var viewModel = TestMainViewModelFactory.Create(new InMemorySettingsStore());
            viewModel.OpenProject(root);

            Assert.Equal("helpers", Assert.Single(viewModel.UserScripts).DisplayName);
            Assert.Equal("hero", Assert.Single(viewModel.ObjectScripts).DisplayName);
            Assert.Equal("forest", Assert.Single(viewModel.BackdropScripts).DisplayName);

            viewModel.ResourceSearchQuery = "hero";

            Assert.Empty(viewModel.UserScripts);
            Assert.Equal("scripts/objects/hero.py", Assert.Single(viewModel.ObjectScripts).RelativePath);
            Assert.Empty(viewModel.BackdropScripts);
            Assert.Equal("1 项脚本", viewModel.ProjectResourceStatus);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SwitchingProjectsClearsTheEditorWithoutWritingTheEntryScript()
    {
        var first = Path.Combine(Path.GetTempPath(), $"visualized-project-{Guid.NewGuid():N}");
        var second = Path.Combine(Path.GetTempPath(), $"visualized-project-{Guid.NewGuid():N}");
        try
        {
            var viewModel = TestMainViewModelFactory.Create(new InMemorySettingsStore());
            viewModel.CreateProject(first);
            var originalMainScript = File.ReadAllText(Path.Combine(first, "scripts", "main.py"));
            viewModel.PythonCode = "print('first')";
            viewModel.CreateProject(second);

            Assert.Equal(originalMainScript, File.ReadAllText(Path.Combine(first, "scripts", "main.py")));
            Assert.Equal(string.Empty, viewModel.PythonCode);
            Assert.Equal(Path.GetFullPath(second), viewModel.ProjectRootPath);

            viewModel.CloseProject();
            Assert.Null(viewModel.ProjectName);
            Assert.Equal(string.Empty, viewModel.PythonCode);
        }
        finally
        {
            if (Directory.Exists(first))
                Directory.Delete(first, recursive: true);
            if (Directory.Exists(second))
                Directory.Delete(second, recursive: true);
        }
    }

    [Fact]
    public void JsonSettingsStoreRoundTripsFlatSettingsKeys()
    {
        var filePath = Path.Combine(AppContext.BaseDirectory, $"settings-{Guid.NewGuid():N}.json");

        try
        {
            var store = new JsonSettingsStore(filePath);
            store.Save(new AppSettings(
                SidebarDisplayModeIndex: 2,
                ThemeIndex: 1,
                TopPanelRatio: 0.7));

            var json = File.ReadAllText(filePath);
            Assert.Contains("\"Appearance.SidebarDisplayMode\": \"IconAndText\"", json);
            Assert.Contains("\"Appearance.Theme\": \"Light\"", json);
            Assert.DoesNotContain("Layout.MainContentWidth", json);
            Assert.Contains("\"Layout.TopPanelRatio\": 0.7", json);
            Assert.Equal(new AppSettings(2, 1, 0.7), store.Load());
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    [Fact]
    public void LegacyIntegerSettingsAreStillReadable()
    {
        var filePath = Path.Combine(AppContext.BaseDirectory, $"legacy-settings-{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(filePath, "{\"Appearance.SidebarDisplayMode\": 1,\"Appearance.Theme\": 2}");
            var store = new JsonSettingsStore(filePath);

            Assert.Equal(new AppSettings(1, 2), store.Load());
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    [Fact]
    public void LegacyEditorWidthIsIgnoredWhileOtherLayoutSettingsAreLoaded()
    {
        var filePath = Path.Combine(AppContext.BaseDirectory, $"legacy-layout-{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(filePath,
                "{\"Layout.MainContentWidth\": 720,\"Layout.TopPanelRatio\": 0.7,\"Layout.AuxiliaryWidth\": 560}");
            var store = new JsonSettingsStore(filePath);

            Assert.Equal(new AppSettings(0, 0, 0.7, 560), store.Load());
        }
        finally
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
        }
    }

    [Fact]
    public void ChangingSettingsPersistsTheCurrentValues()
    {
        var store = new InMemorySettingsStore();
        var viewModel = new MainViewModel(store);

        viewModel.SidebarDisplayModeIndex = 2;
        viewModel.SelectedThemeIndex = 1;

        Assert.Equal(new AppSettings(2, 1), store.Settings);
    }

    [Fact]
    public void SavingLayoutPersistsTheSupportedLayoutSettings()
    {
        var store = new InMemorySettingsStore();
        var viewModel = new MainViewModel(store);

        viewModel.UpdateLayout(0.7, 560);
        viewModel.SaveLayout();

        Assert.Equal(new AppSettings(0, 0, 0.7, 560), store.Settings);
    }

    private sealed class InMemorySettingsStore : ISettingsStore
    {
        public AppSettings Settings { get; private set; } = new();

        public AppSettings Load() => Settings;

        public void Save(AppSettings settings) => Settings = settings;
    }

}
