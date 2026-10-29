using System;
using System.IO;
using happy_agent_coding.Services;
using happy_agent_coding.ViewModels;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Infrastructure.Projects;

namespace VisualizedAgentCoding.App.Tests;

public class SettingsStoreTests
{
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
    public async Task ChangingSettingsPersistsTheCurrentValues()
    {
        var store = new InMemorySettingsStore();
        await using var viewModel = TestMainViewModelFactory.Create(store);

        viewModel.SidebarDisplayModeIndex = 2;
        viewModel.SelectedThemeIndex = 1;

        Assert.Equal(new AppSettings(2, 1), store.Settings);
    }

    [Fact]
    public async Task SavingLayoutPersistsTheSupportedLayoutSettings()
    {
        var store = new InMemorySettingsStore();
        await using var viewModel = TestMainViewModelFactory.Create(store);

        viewModel.UpdateLayout(0.7, 560);
        viewModel.SaveLayout();

        Assert.Equal(new AppSettings(0, 0, 0.7, 560), store.Settings);
    }
}
