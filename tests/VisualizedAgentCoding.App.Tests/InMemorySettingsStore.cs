using happy_agent_coding.Services;

namespace VisualizedAgentCoding.App.Tests;

internal sealed class InMemorySettingsStore : ISettingsStore
{
    public AppSettings Settings { get; private set; } = new();
    public AppSettings Load() => Settings;
    public void Save(AppSettings settings) => Settings = settings;
}
