using happy_agent_coding.Services;
using happy_agent_coding.ViewModels;
using VisualizedAgentCoding.Application.Projects;

namespace VisualizedAgentCoding.App.Tests;

internal static class TestMainViewModelFactory
{
    public static MainViewModel Create(ISettingsStore settingsStore) =>
        new(settingsStore, new InMemoryRecentProjectsStore(), new ProjectSession());

    // These model tests have no UI dispatcher driving the terminal refresh timer.
    public static async Task WaitForInteractiveAsync(MainViewModel vm, Task run)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var flush = typeof(MainViewModel).GetMethod("FlushTerminalEvents",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        while (!vm.CanDispatchStageClicks)
        {
            if (run.IsCompleted) { await run; throw new InvalidOperationException(vm.RuntimeStatus); }
            flush.Invoke(vm, null);
            await Task.Delay(20, timeout.Token);
        }
    }
}
