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
    public void SavingUpdatesEditorState()
    {
        var viewModel = new MainViewModel();

        viewModel.SaveCommand.Execute(null);

        Assert.Equal("已保存", viewModel.EditorState);
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
