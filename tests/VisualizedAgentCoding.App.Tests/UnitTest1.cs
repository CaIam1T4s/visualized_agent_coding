using System;
using System.IO;
using happy_agent_coding.Services;
using happy_agent_coding.ViewModels;

namespace VisualizedAgentCoding.App.Tests;

public class MainViewModelTests
{
    [Fact]
    public void StartsWithPythonDocumentAndLineNumbers()
    {
        var viewModel = new MainViewModel();

        Assert.Contains("def main():", viewModel.PythonCode);
        Assert.Equal("1\r\n2\r\n3\r\n4\r\n5\r\n6", viewModel.LineNumbers);
        Assert.Equal("main.py", viewModel.FileName);
    }

    [Fact]
    public void SavingWithoutAProjectReportsThatNoProjectIsOpen()
    {
        var viewModel = new MainViewModel();

        viewModel.SaveCommand.Execute(null);

        Assert.Equal("尚未打开项目", viewModel.EditorState);
    }

    [Fact]
    public void ProjectCreationAndOpeningRoundTripMainScript()
    {
        var root = Path.Combine(Path.GetTempPath(), $"visualized-project-{Guid.NewGuid():N}");
        try
        {
            var viewModel = new MainViewModel(new InMemorySettingsStore());
            viewModel.CreateProject(root, "Demo");

            Assert.Equal("Demo", viewModel.ProjectName);
            Assert.True(Directory.Exists(Path.Combine(root, "scripts", "user")));
            Assert.Contains("def main():", File.ReadAllText(Path.Combine(root, "scripts", "main.py")));

            viewModel.PythonCode = "print('saved')";
            viewModel.SaveCommand.Execute(null);
            var reopened = new MainViewModel(new InMemorySettingsStore());
            reopened.OpenProject(root);

            Assert.Equal("print('saved')", reopened.PythonCode);
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
            ProjectWorkspace.Create(root, "Demo");
            File.WriteAllText(Path.Combine(root, "scripts", "user", "helpers.py"), "pass");
            File.WriteAllText(Path.Combine(root, "scripts", "objects", "hero.py"), "pass");
            File.WriteAllText(Path.Combine(root, "scripts", "backdrops", "forest.py"), "pass");
            File.WriteAllText(Path.Combine(root, "scripts", "user", "notes.json"), "{}");
            Directory.CreateDirectory(Path.Combine(root, "assets", "images"));
            File.WriteAllText(Path.Combine(root, "assets", "images", "hero.png"), "image");
            var viewModel = new MainViewModel(new InMemorySettingsStore());
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
    public void SwitchingProjectsSavesThePreviousMainScript()
    {
        var first = Path.Combine(Path.GetTempPath(), $"visualized-project-{Guid.NewGuid():N}");
        var second = Path.Combine(Path.GetTempPath(), $"visualized-project-{Guid.NewGuid():N}");
        try
        {
            var viewModel = new MainViewModel(new InMemorySettingsStore());
            viewModel.CreateProject(first);
            viewModel.PythonCode = "print('first')";
            viewModel.CreateProject(second);

            Assert.Equal("print('first')", File.ReadAllText(Path.Combine(first, "scripts", "main.py")));
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
                MainContentWidth: 520,
                TopPanelRatio: 0.7));

            var json = File.ReadAllText(filePath);
            Assert.Contains("\"Appearance.SidebarDisplayMode\": \"IconAndText\"", json);
            Assert.Contains("\"Appearance.Theme\": \"Light\"", json);
            Assert.Contains("\"Layout.MainContentWidth\": 520", json);
            Assert.Contains("\"Layout.TopPanelRatio\": 0.7", json);
            Assert.Equal(new AppSettings(2, 1, 520, 0.7), store.Load());
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
    public void LegacyLayoutWidthIsConvertedToMainContentWidth()
    {
        var filePath = Path.Combine(AppContext.BaseDirectory, $"legacy-layout-{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(filePath, "{\"Layout.RightPanelWidth\": 460}");
            var store = new JsonSettingsStore(filePath);

            Assert.Equal(460, store.Load().MainContentWidth);
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
    public void SavingLayoutPersistsTheCurrentDimensions()
    {
        var store = new InMemorySettingsStore();
        var viewModel = new MainViewModel(store);

        viewModel.UpdateLayout(512, 0.7);
        viewModel.SaveLayout();

        Assert.Equal(new AppSettings(0, 0, 512, 0.7), store.Settings);
    }

    private sealed class InMemorySettingsStore : ISettingsStore
    {
        public AppSettings Settings { get; private set; } = new();

        public AppSettings Load() => Settings;

        public void Save(AppSettings settings) => Settings = settings;
    }
}
