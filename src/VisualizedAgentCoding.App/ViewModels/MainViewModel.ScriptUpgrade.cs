using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VisualizedAgentCoding.Application.Editor.ObjectScripts;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Domain.Projects;

namespace happy_agent_coding.ViewModels;

public partial class MainViewModel
{
    private int? ReadScriptTemplateVersion(ProjectScriptEntry script)
    {
        if (script.Kind == ProjectScriptKind.User || activeProject is null) return null;
        try
        {
            return ObjectScriptTemplateVersion.ReadHeader(script.Id == loadedScriptId && script.Id == openScriptId
                ? PythonCode : projectSession.LoadScript(activeProject, script));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ProjectOperationException) { return null; }
    }

    private void UpdateOpenTemplateDecoration()
    {
        if (OpenScript is not { Kind: not ProjectScriptKind.User } script) return;
        var category = CategoryFor(script.Kind);
        var item = category.Scripts.FirstOrDefault(s => s.Id == script.Id);
        if (item is null) return;
        var version = ObjectScriptTemplateVersion.ReadHeader(PythonCode);
        if (item.TemplateVersion != version)
            category.Scripts[category.Scripts.IndexOf(item)] = item with { TemplateVersion = version };
    }

    public async Task<ObjectScriptDocument> PrepareScriptUpgradeAsync(CancellationToken token)
    {
        var project = activeProject ?? throw new ProjectOperationException("未打开项目。");
        var id = OpenScript?.Id;
        var source = PythonCode;
        var version = pythonDocumentVersion;
        var original = await InspectEditorProtectionAsync(token)
            ?? throw new ProjectOperationException("无法确认用户区域，请先修复脚本结构。");
        if (objectScriptDocumentService is null) throw new ProjectOperationException("模板分析服务不可用。");
        var upgraded = await new ObjectScriptUpgradeService(objectScriptDocumentService).PrepareAsync(project, original, version, token);
        if (!ReferenceEquals(activeProject, project) || OpenScript?.Id != id || pythonDocumentVersion != version || PythonCode != source)
            throw new ProjectOperationException("脚本内容已改变，请重新预览升级。");
        return upgraded;
    }
}
