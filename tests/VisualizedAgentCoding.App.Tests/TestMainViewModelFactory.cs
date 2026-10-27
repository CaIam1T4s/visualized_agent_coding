using happy_agent_coding.Services;
using happy_agent_coding.ViewModels;
using VisualizedAgentCoding.Application.Projects;

namespace VisualizedAgentCoding.App.Tests;

internal static class TestMainViewModelFactory
{
    public static MainViewModel Create(ISettingsStore settingsStore) =>
        new(settingsStore, new InMemoryRecentProjectsStore(), new ProjectSession());
}
