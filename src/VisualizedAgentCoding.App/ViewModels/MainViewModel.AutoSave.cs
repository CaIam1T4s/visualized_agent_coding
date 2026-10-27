using System.Threading;
using System.Threading.Tasks;
using VisualizedAgentCoding.Application.Editor;

namespace happy_agent_coding.ViewModels;

public partial class MainViewModel
{
    private ExitAutoSaveService exitAutoSave = null!;

    private void InitializeExitAutoSave(IAutoSavePolicyProvider? policies)
    {
        exitAutoSave = new ExitAutoSaveService(policies ?? new DefaultAutoSavePolicyProvider(), token =>
        {
            token.ThrowIfCancellationRequested();
            SaveActiveProject();
            return Task.CompletedTask;
        });
    }

    public async Task<bool> PrepareForExitAsync(CancellationToken token = default)
    {
        var result = await exitAutoSave.PrepareAsync(token);
        if (!result.CanClose)
        {
            EditorState = $"自动保存失败，窗口已保留：{result.Error}";
            ReportResourceMessage(EditorState);
        }
        return result.CanClose;
    }
}
