using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Application.Python;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualizedAgentCoding.Domain.Projects;

namespace happy_agent_coding.ViewModels;

public partial class MainViewModel
{
    public ScriptRenameState? ScriptRename { get; private set; }
    private ScriptRenameService? scriptRenameService;
    private ScriptRenamePlan? pendingRenamePlan;
    private CancellationTokenSource? renameCancellation;
    private readonly List<Task> renameTasks = new();
    public ObservableCollection<ScriptRenamePreviewItem> RenameChanges { get; } = [];
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsScriptActionDialogOpen))] private bool isRenamePreviewOpen;
    [ObservableProperty] private bool canApplyRename;
    [ObservableProperty] private string renamePreviewStatus = string.Empty;
    [ObservableProperty] private string? renamePreviewError;

    [RelayCommand]
    private void StartRenameScript(ProjectScriptItem? item)
    {
        if (activeProject?.FindScript(item?.Id) is not { } script || IsScriptActionDialogOpen)
            return;
        ScriptRename = new ScriptRenameState(script.Id, Path.GetFileNameWithoutExtension(script.Path),
            PrepareRenameScriptAsync, CancelRenameScript);
        CategoryFor(script.Kind).CompleteCreation();
        CategoryFor(script.Kind).IsExpanded = true;
        RefreshProjectResources();
    }

    [RelayCommand]
    private void CancelRenameScript()
    {
        renameCancellation?.Cancel();
        pendingRenamePlan = null;
        CanApplyRename = false;
        IsRenamePreviewOpen = false;
        if (ScriptRename is null) return;
        ScriptRename = null;
        RefreshProjectResources();
    }

    private Task PrepareRenameScriptAsync()
    {
        renameTasks.RemoveAll(task => task.IsCompleted);
        var task = PrepareRenameCoreAsync();
        renameTasks.Add(task);
        return task;
    }

    private async Task PrepareRenameCoreAsync()
    {
        if (ScriptRename is not { } rename || activeProject is null) return;
        renameCancellation?.Cancel();
        renameCancellation?.Dispose();
        renameCancellation = new CancellationTokenSource();
        var token = renameCancellation.Token;
        var project = activeProject;
        var version = pythonDocumentVersion;
        CanApplyRename = false;
        pendingRenamePlan = null;
        RenameChanges.Clear();
        rename.Error = null;
        RenamePreviewError = null;
        RenamePreviewStatus = "正在分析重命名及引用修改…";
        IsRenamePreviewOpen = true;
        try
        {
            if (scriptRenameService is null) throw new ProjectOperationException("重命名分析不可用，未修改文件。");
            if (OpenScript is { } current && loadedScriptId == current.Id
                && projectSession.LoadScript(project, current) != loadedScriptSource)
                throw new ProjectOperationException("当前脚本已被外部修改，请处理冲突后再生成重命名预览。");
            var overlays = OpenScript is { } open
                ? new[] { new PythonSourceDocument(new Uri(project.ToAbsolutePath(open.Path)).AbsoluteUri, version, PythonCode) }
                : Array.Empty<PythonSourceDocument>();
            var plan = await Task.Run(() => scriptRenameService.PlanAsync(project, rename.ScriptId, rename.Name, version, overlays, token), token);
            if (token.IsCancellationRequested) return;
            if (!ReferenceEquals(activeProject, project) || version != pythonDocumentVersion || !ReferenceEquals(ScriptRename, rename))
                throw new ProjectOperationException("编辑内容已变化，请重新生成预览。");
            pendingRenamePlan = plan;
            foreach (var change in plan.Changes) RenameChanges.Add(new(change));
            RenamePreviewStatus = $"{project.FindScript(plan.ScriptId)!.Path} → {plan.NewPath}\n将同步修改 {plan.Changes.Length} 个文件的引用。未保存的编辑内容将一并保存。";
            CanApplyRename = true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (token.IsCancellationRequested) return;
            rename.Error = ex.Message;
            RenamePreviewError = ex.Message;
            RenamePreviewStatus = "未生成可应用的修改计划，文件未改变。";
        }
    }

    [RelayCommand]
    private void ApplyRename()
    {
        if (!CanApplyRename || pendingRenamePlan is not { } plan || scriptRenameService is null) return;
        try
        {
            if (!ReferenceEquals(activeProject, plan.Project) || pythonDocumentVersion != plan.Version)
                throw new ProjectOperationException("编辑内容已变化，请重新生成预览。");
            var openPath = OpenScript?.Path;
            activeProject = scriptRenameService.Apply(plan);
            if (openPath is not null)
            {
                var saved = plan.Documents.FirstOrDefault(document => string.Equals(document.Path.Replace('\\', '/'),
                    openPath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
                if (saved is not null)
                {
                    RememberLoadedScript(openScriptId!, saved.After);
                    PythonCode = saved.After;
                }
            }
            CancelRenameScript();
            RefreshProjectResources();
            NotifyScriptEditorState();
            ReportResourceMessage($"已重命名为 {plan.NewName}.py，并更新 {plan.Changes.Length} 个文件的引用。");
        }
        catch (Exception ex)
        {
            RenamePreviewError = ex.Message;
            CanApplyRename = false;
        }
    }

}
