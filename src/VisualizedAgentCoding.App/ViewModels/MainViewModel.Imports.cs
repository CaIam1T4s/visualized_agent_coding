using System;
using System.Threading;
using System.Threading.Tasks;
using VisualizedAgentCoding.Application.Editor;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Domain.Projects;

namespace happy_agent_coding.ViewModels;

public partial class MainViewModel
{
    private ScriptImportService? scriptImportService;
    private UserScriptImportProjection? editorUserProjection;
    public ISourceProjection? CurrentEditorProjection => (ISourceProjection?)editorUserProjection ?? editorProjection;
    public string? CurrentScriptUri => activeProject is not null && OpenScript is { } script
        ? new Uri(activeProject.ToAbsolutePath(script.Path)).AbsoluteUri : null;
    public ImportScriptTarget[] ImportTargets => activeProject is null || OpenScript is not { } script ? []
        : ScriptImportService.Context(activeProject, script).Scripts;

    public void SetUserImportProjection(UserScriptImportProjection? value)
    {
        editorUserProjection = value;
        if (value is not null) editorProjection = null;
        NotifyEditorDiagnosticsChanged();
    }

    public async Task<ScriptImportsResult> InspectScriptImportsAsync(ScriptImport[]? imports = null, int? convertLine = null,
        CancellationToken token = default)
    {
        var project = activeProject ?? throw new ProjectOperationException("未打开项目。");
        var script = OpenScript ?? throw new ProjectOperationException("未打开脚本。");
        var source = PythonCode;
        var version = pythonDocumentVersion;
        if (scriptImportService is null) throw new ProjectOperationException("导入分析服务不可用。");
        var result = await scriptImportService.InspectAsync(project, script, version, source,
            editorProjection?.Document, imports, convertLine, token);
        if (project != activeProject || OpenScript?.Id != script.Id || source != PythonCode || version != pythonDocumentVersion)
            throw new ProjectOperationException("编辑内容已改变，请重新打开导入管理。");
        return result;
    }

    public async Task<VisualizedAgentCoding.Application.Editor.ObjectScripts.ObjectScriptDocument> InspectImportObjectAsync(string source)
    {
        if (activeProject is null || OpenScript is null || objectScriptDocumentService is null)
            throw new ProjectOperationException("模板分析不可用。");
        var result = await objectScriptDocumentService.InspectAsync(activeProject, OpenScript.Id, pythonDocumentVersion, source);
        if (!result.HasRegions || result.TemplateVersion != 3) throw new ProjectOperationException("导入后的模板结构无法确认，未应用。");
        return result;
    }
}
