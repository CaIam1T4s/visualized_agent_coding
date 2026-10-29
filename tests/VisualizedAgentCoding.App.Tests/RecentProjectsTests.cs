using System;
using System.IO;
using happy_agent_coding.Services;
using happy_agent_coding.ViewModels;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Infrastructure.Projects;

namespace VisualizedAgentCoding.App.Tests;

public class RecentProjectsTests
{
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
    public async Task OpeningAProjectPersistsRecentEntryForTheNextViewModel()
    {
        var root = Path.Combine(Path.GetTempPath(), $"recent-projects-{Guid.NewGuid():N}");
        var projectRoot = Path.Combine(root, "project");
        var filePath = Path.Combine(root, "recent-projects.json");
        try
        {
            var store = new JsonRecentProjectsStore(filePath);
            await using var viewModel = new MainViewModel(new InMemorySettingsStore(), store, new ProjectSession());
            await viewModel.CreateProject(projectRoot, "Demo");

            await using var reopened = new MainViewModel(new InMemorySettingsStore(), store, new ProjectSession());

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
    public async Task StartupRemovesMissingAndInvalidProjectsFromRecentList()
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

            await using var viewModel = new MainViewModel(
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
}
