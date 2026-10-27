namespace VisualizedAgentCoding.Application.Editor;

public sealed record AutoSavePolicy(bool SaveOnExit = true);

/// <summary>Replace this provider with a settings adapter without changing exit or save code.</summary>
public interface IAutoSavePolicyProvider
{
    AutoSavePolicy Current { get; }
}

public sealed class DefaultAutoSavePolicyProvider : IAutoSavePolicyProvider
{
    public AutoSavePolicy Current => new();
}

public sealed record ExitAutoSaveResult(bool CanClose, bool Saved, string? Error = null);

/// <summary>No dependency on windows, Python analysis, project storage or application lifetime.</summary>
public sealed class ExitAutoSaveService(IAutoSavePolicyProvider policies, Func<CancellationToken, Task> save)
{
    public async Task<ExitAutoSaveResult> PrepareAsync(CancellationToken token = default)
    {
        try
        {
            if (!policies.Current.SaveOnExit) return new(true, false);
            token.ThrowIfCancellationRequested();
            await save(token);
            return new(true, true);
        }
        catch (OperationCanceledException) { return new(false, false, "自动保存已取消。"); }
        catch (Exception ex) { return new(false, false, ex.Message); }
    }
}
