using System;
using System.Threading;
using System.Threading.Tasks;
using VisualizedAgentCoding.Application.Editor.ObjectScripts;
using VisualizedAgentCoding.Domain.Projects;

namespace happy_agent_coding.ViewModels;

public partial class MainViewModel
{
    private readonly SemaphoreSlim scriptSaveGate = new(1, 1);
    private long editorNavigationGeneration;
    private ObjectScriptSaveService? objectScriptSaveService;
    private string? loadedScriptId;
    private string? loadedScriptSource;
    private ObjectScriptDocument? savedObjectDocument;
    private bool hasStructuralEdit;

    public event Action? EditorHistoryResetRequested;

    private void RememberLoadedScript(string id, string source, bool resetHistory = true)
    {
        loadedScriptId = id;
        loadedScriptSource = source;
        savedObjectDocument = null;
        hasStructuralEdit = false;
        if (resetHistory) EditorHistoryResetRequested?.Invoke();
        NotifyEditorSourceChanged();
    }

    private void ResetLoadedScript()
    {
        loadedScriptId = null;
        loadedScriptSource = null;
        savedObjectDocument = null;
        hasStructuralEdit = false;
        EditorHistoryResetRequested?.Invoke();
        NotifyEditorSourceChanged();
    }

    private async Task SaveActiveProjectAsync(CancellationToken token = default, bool checkUnchangedDisk = false)
    {
        await scriptSaveGate.WaitAsync(token);
        try
        {
            SaveStage();
            if (activeProject is null || OpenScript is not { } script) return;
            var project = activeProject;
            var source = PythonCode;
            var version = pythonDocumentVersion;
            if (loadedScriptId != script.Id || loadedScriptSource is null)
                throw new ProjectOperationException("缺少打开时的源码快照，未保存。");
            // No local edit: do not overwrite external edits just to navigate or close.
            if (source == loadedScriptSource)
            {
                if (checkUnchangedDisk && projectSession.LoadScript(project, script) != loadedScriptSource)
                    throw new ProjectOperationException("磁盘脚本已被外部修改。可通过右键菜单重新加载，未覆盖磁盘内容。");
                return;
            }
            if (script.Kind == ProjectScriptKind.User)
            {
                if (projectSession.LoadScript(project, script) != loadedScriptSource)
                    throw new ProjectOperationException("磁盘脚本已被外部修改，未覆盖。当前编辑内容已保留。");
                projectSession.SaveScript(project, script, source);
                RememberLoadedScript(script.Id, source, resetHistory: false);
                return;
            }
            if (objectScriptSaveService is null)
                throw new ProjectOperationException("对象脚本保存服务不可用，当前编辑内容已保留。");
            var baseline = loadedScriptSource;
            var document = savedObjectDocument;
            var multi = editorProjection as MultiRegionProjection;
            var plan = hasStructuralEdit && editorProjection is { } trusted && trusted.Document.ScriptId == script.Id && trusted.FullSource == source
                ? new ObjectScriptSavePlan(project, script.Id, script.Path, version, baseline, source, trusted.Document with { Version = version })
                : multi is not null && multi.Document.ScriptId == script.Id && multi.FullSource == source && multi.BaselineDocument.FullSource == baseline
                    ? ObjectScriptRegionSaveService.Prepare(project, multi.BaselineDocument, version, multi.FullRegionSources())
                : await Task.Run(() => objectScriptSaveService.PrepareFullBufferAsync(project, script.Id, version,
                    baseline, source, document, token), token);
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(activeProject, project) || openScriptId != script.Id || pythonDocumentVersion != version)
                throw new ProjectOperationException("保存期间编辑内容已变化，请重试。当前缓冲区已保留。");
            objectScriptSaveService.Apply(plan, pythonDocumentVersion);
            loadedScriptSource = plan.MergedSource;
            savedObjectDocument = plan.Document;
            hasStructuralEdit = false;
            if (multi is not null && multi.FullSource == plan.MergedSource && plan.Document is { } saved)
                multi.AcceptSavedDocument(saved);
        }
        finally { scriptSaveGate.Release(); }
    }
}
