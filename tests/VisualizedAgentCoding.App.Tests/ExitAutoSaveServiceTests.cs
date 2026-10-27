using VisualizedAgentCoding.Application.Editor;

namespace VisualizedAgentCoding.App.Tests;

public sealed class ExitAutoSaveServiceTests
{
    [Fact]
    public async Task DefaultPolicySavesBeforeAllowingExit()
    {
        var saved = false;
        var service = new ExitAutoSaveService(new DefaultAutoSavePolicyProvider(), _ => { saved = true; return Task.CompletedTask; });
        var result = await service.PrepareAsync();
        Assert.True(saved);
        Assert.True(result.CanClose);
        Assert.True(result.Saved);
    }

    [Fact]
    public async Task PolicyIsReadAtExitSoLaterSettingsCanDisableAndEnableSaving()
    {
        var policies = new Policies { Current = new(false) };
        var count = 0;
        var service = new ExitAutoSaveService(policies, _ => { count++; return Task.CompletedTask; });
        Assert.True((await service.PrepareAsync()).CanClose);
        Assert.Equal(0, count);
        policies.Current = new(true);
        Assert.True((await service.PrepareAsync()).Saved);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task FailedSaveBlocksExitAndCanBeRetried()
    {
        var fail = true;
        var service = new ExitAutoSaveService(new DefaultAutoSavePolicyProvider(), _ =>
            fail ? Task.FromException(new IOException("disk full")) : Task.CompletedTask);
        var failed = await service.PrepareAsync();
        Assert.False(failed.CanClose);
        Assert.Equal("disk full", failed.Error);
        fail = false;
        Assert.True((await service.PrepareAsync()).CanClose);
    }

    [Fact]
    public async Task CancellationDoesNotPermitExitOrInvokeSave()
    {
        var service = new ExitAutoSaveService(new DefaultAutoSavePolicyProvider(), _ => throw new Exception("must not save"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.False((await service.PrepareAsync(cancellation.Token)).CanClose);
    }

    private sealed class Policies : IAutoSavePolicyProvider
    {
        public AutoSavePolicy Current { get; set; } = new();
    }
}
