using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using VisualizedAgentCoding.Application.Ports;
using VisualizedAgentCoding.Application.Python;
using VisualizedAgentCoding.Domain.Projects;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Application.Editor.ObjectScripts;

namespace happy_agent_coding.ViewModels;

public sealed record PythonReferenceItem(PythonReferenceLocation Location, string RelativePath)
{
    public string DisplayText => $"{RelativePath}  第 {Location.Range.Line} 行，第 {Location.Range.Column} 列  {(Location.IsDefinition ? "定义" : "引用")}";
}

public partial class MainViewModel
{
    private ProjectReferenceService? referenceService;
    private CancellationTokenSource? referenceCancellation;
    private bool navigatingReference;
    private readonly List<Task> referenceTasks = new();
    public ObservableCollection<PythonReferenceItem> PythonReferences { get; } = [];

    [ObservableProperty]
    private bool hasReferenceQuery;
    [ObservableProperty]
    private bool isFindingReferences;
    [ObservableProperty]
    private string referenceStatus = string.Empty;
    [ObservableProperty]
    private string referenceIssues = string.Empty;

    private void InitializeReferences(IPythonBridge? bridge, IProjectStore store)
    {
        if (bridge is not null)
        {
            referenceService = new ProjectReferenceService(bridge, store);
            deletionImpactService = new ScriptDeletionImpactService(bridge, store);
            scriptRenameService = new ScriptRenameService(bridge, store);
            objectScriptDocumentService = new ObjectScriptDocumentService(bridge);
            objectScriptSaveService = new ObjectScriptSaveService(objectScriptDocumentService, store);
        }
    }

    public Task FindReferencesAtAsync(int line, int column)
    {
        referenceTasks.RemoveAll(task => task.IsCompleted);
        var task = FindReferencesCoreAsync(line, column);
        referenceTasks.Add(task);
        return task;
    }

    private async Task FindReferencesCoreAsync(int line, int column)
    {
        if (activeProject is null || OpenScript is not { } script) return;
        ResetReferenceQuery();
        HasReferenceQuery = true;
        if (referenceService is null)
        {
            ReferenceStatus = "引用查询不可用。";
            return;
        }
        var cancellation = referenceCancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        var project = activeProject;
        var version = pythonDocumentVersion;
        var uri = new Uri(project.ToAbsolutePath(script.Path)).AbsoluteUri;
        var overlay = new PythonSourceDocument(uri, version, PythonCode);
        IsFindingReferences = true;
        ReferenceStatus = "正在按需查找引用…";
        try
        {
            // No Save call: the overlay, including an empty buffer, replaces disk content.
            var result = await Task.Run(() => referenceService.FindAsync(project, uri, version, line, column, new[] { overlay }, token), token);
            if (token.IsCancellationRequested) return;
            if (!ReferenceEquals(activeProject, project) || pythonDocumentVersion != version)
            {
                ReferenceStatus = "项目内容已变化，请重新查找引用。";
                return;
            }
            foreach (var location in result.References)
                PythonReferences.Add(new(location, Path.GetRelativePath(project.RootPath, new Uri(location.Uri).LocalPath)));
            ReferenceStatus = $"找到 {result.References.Length} 处位置（含定义）";
            ReferenceIssues = string.Join(Environment.NewLine, result.Issues.Select(issue =>
                $"{Path.GetRelativePath(project.RootPath, new Uri(issue.Uri).LocalPath)}：{issue.Message}"));
            if (result.IsIncomplete) ReferenceStatus += "，结果可能不完整";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested) ReferenceStatus = $"引用查询失败：{ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(referenceCancellation, cancellation)) IsFindingReferences = false;
        }
    }

    public async Task<bool> OpenReference(PythonReferenceItem item)
    {
        if (activeProject is null) return false;
        var path = Path.GetFullPath(new Uri(item.Location.Uri).LocalPath);
        var script = activeProject.Manifest.Scripts.FirstOrDefault(entry =>
            string.Equals(Path.GetFullPath(activeProject.ToAbsolutePath(entry.Path)), path, StringComparison.OrdinalIgnoreCase));
        if (script is null && string.Equals(activeProject.MainScriptPath, path, StringComparison.OrdinalIgnoreCase))
            script = ResolveProjectScript(activeProject.Manifest.MainScriptId);
        if (script is null)
        {
            ReportResourceMessage("该引用位于未索引或运行时脚本中，暂不支持在编辑器中打开。");
            return false;
        }
        navigatingReference = true;
        try
        {
            await OpenScriptFromExplorer(new(script.Id, Path.GetFileNameWithoutExtension(script.Path), script.Path, script.Kind));
            return openScriptId == script.Id;
        }
        finally { navigatingReference = false; }
    }

    private ProjectScriptEntry? ResolveProjectScript(string? id) => activeProject?.FindScript(id)
        ?? (activeProject is not null && id == activeProject.Manifest.MainScriptId
            ? new ProjectScriptEntry(id, activeProject.Manifest.MainScript, ProjectScriptKind.User) : null);

    private void ResetReferenceQuery()
    {
        referenceCancellation?.Cancel();
        referenceCancellation?.Dispose();
        referenceCancellation = null;
        IsFindingReferences = false;
        HasReferenceQuery = false;
        ReferenceStatus = string.Empty;
        ReferenceIssues = string.Empty;
        PythonReferences.Clear();
    }
}
