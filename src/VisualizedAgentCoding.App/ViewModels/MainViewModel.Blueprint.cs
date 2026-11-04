using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VisualizedAgentCoding.Application.Editor.ObjectScripts;
using VisualizedAgentCoding.Application.Python;
using VisualizedAgentCoding.Blueprint.Core;
using VisualizedAgentCoding.Domain.Projects;

namespace happy_agent_coding.ViewModels;

public partial class MainViewModel
{
    public BlueprintEditorModel BlueprintEditor { get; } = new();
    private CancellationTokenSource? blockRefreshCancellation;
    private readonly Services.BlueprintLayoutStore blueprintLayoutStore = new();
    private string? selectedBlueprintRegionId;
    private string? currentBlueprintBody;
    private bool suppressBlueprintNavigation;

    private void InitializeBlueprintNavigation()
    {
        BlueprintEditor.ClassPage.PropertyChanged += (_, args) =>
        {
            if (suppressBlueprintNavigation || args.PropertyName != nameof(BlueprintClassPageModel.SelectedCanvas))
                return;
            if (BlueprintEditor.ClassPage.SelectedCanvas is not { } canvas
                || canvas.RegionId == selectedBlueprintRegionId) return;
            selectedBlueprintRegionId = canvas.RegionId;
            ScheduleBlockEditorRefresh();
        };
    }

    partial void OnEditorModeIndexChanged(int value)
    {
        if (value == 1) ScheduleBlockEditorRefresh();
    }

    private void ScheduleBlockEditorRefresh()
    {
        if (!IsBlockEditorMode) return;
        blockRefreshCancellation?.Cancel();
        blockRefreshCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        blockRefreshCancellation = cancellation;
        _ = RefreshBlockEditorAsync(cancellation.Token);
    }

    private async Task RefreshBlockEditorAsync(CancellationToken token)
    {
        try
        {
            if (activeProject is null || OpenScript is not { } script)
            {
                BlueprintEditor.Clear("打开角色脚本后显示启动区域节点。");
                return;
            }
            if (script.Kind != ProjectScriptKind.Object)
            {
                BlueprintEditor.Clear("当前脚本没有可预览的对象区域。");
                return;
            }
            var projection = editorProjection as MultiRegionProjection;
            if (projection is null && objectScriptDocumentService is not null)
            {
                var document = await objectScriptDocumentService.InspectAsync(
                    activeProject, script.Id, pythonDocumentVersion, PythonCode, token);
                if (document.HasRegions && document.TemplateVersion is 2 or 3)
                    projection = new MultiRegionProjection(document);
            }
            if (projection is null)
            {
                BlueprintEditor.Clear("正在等待脚本结构确认…");
                return;
            }
            var regions = projection.Document.Regions ?? [];
            if (regions.Length == 0)
            {
                BlueprintEditor.Clear("当前脚本没有可显示的区域。");
                return;
            }
            var objectName = Path.GetFileNameWithoutExtension(script.Path);
            var className = string.IsNullOrWhiteSpace(objectName) ? "Object" : objectName;
            suppressBlueprintNavigation = true;
            BlueprintEditor.ClassPage.SetRegions(objectName, className, regions,
                projection.Document.BackdropNames, selectedBlueprintRegionId);
            suppressBlueprintNavigation = false;
            var region = regions.FirstOrDefault(item => item.Id == selectedBlueprintRegionId)
                ?? regions.FirstOrDefault(item => item.Kind == "start")
                ?? regions[0];
            selectedBlueprintRegionId = region.Id;
            if (executionBridge is null)
            {
                BlueprintEditor.SetError("Python Helper 不可用，无法读取节点。");
                return;
            }
            var body = projection.ViewRegionSources().GetValueOrDefault(region.Id);
            if (body is null)
            {
                BlueprintEditor.SetError("无法读取当前区域源码。");
                return;
            }
            currentBlueprintBody = body;
            BlueprintEditor.SetLoading();
            var tree = await executionBridge.BuildNodeTreeAsync(new PythonNodeRequest(body), token);
            if (token.IsCancellationRequested) return;
            if (tree.Error is { Length: > 0 } error)
            {
                BlueprintEditor.SetStaleError("源码有语法错误，节点暂未更新：" + error);
                return;
            }
            var title = BlueprintClassPageModel.Title(region, projection.Document.BackdropNames);
            var lines = new List<BlockLine> { new(title, 0, IsHat: true) };
            AppendBlockLines(lines, tree.Nodes, 1);
            var nextDocument = NodeProjection.ToDocument(tree, script.Id, region.Id, title);
            AddStartNode(nextDocument, title);
            BlueprintDocumentReconciler.Merge(BlueprintEditor.Document, nextDocument);
            if (activeProject is not null)
            {
                var layout = await blueprintLayoutStore.LoadAsync(activeProject.RootPath, token);
                BlueprintLayoutMapper.Apply(layout, nextDocument);
            }
            BlueprintEditor.SetDocument(nextDocument);
            var projectVariables = ProjectVariables
                .Select(variable => new ProjectVariableRef(variable.Name, variable.Value))
                .ToList();
            try
            {
                var memberTree = await executionBridge.BuildNodeTreeAsync(
                    new PythonNodeRequest(PythonCode, "document"), token);
                if (memberTree.Error is null)
                {
                    var memberDocument = NodeProjection.ToDocument(memberTree, script.Id, "document", "类定义");
                    BlueprintEditor.SetVariables(BlueprintMemberIndex.CollectVariables(memberDocument, className, projectVariables));
                }
            }
            catch (Exception)
            {
                // Member indexing is optional; the node canvas stays usable.
            }
            BlueprintEditor.ImportTemplates.Clear();
            foreach (var template in BlueprintMemberIndex.ImportTemplates(projection.Document.Imports?.Entries ?? []))
                BlueprintEditor.ImportTemplates.Add(template);
            BlueprintEditor.SetTree(title, lines,
                lines.Count == 1 ? "启动区域暂时没有节点。" : "只读预览 · 自由坐标 · 拖拽只改位置");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested) BlueprintEditor.SetError("读取节点失败：" + ex.Message);
        }
    }

    private static void AddStartNode(VisualizedAgentCoding.Domain.Blueprint.BlueprintDocument document, string title)
    {
        if (document.Roots.Count == 1 && document.Roots[0].Type == "start") return;
        var start = new VisualizedAgentCoding.Domain.Blueprint.BlueprintNode
        {
            Id = document.RegionId + ":start",
            Type = "start",
            Kind = VisualizedAgentCoding.Domain.Blueprint.NodeKind.Start,
            DisplayName = title,
        };
        start.Ports.Add(new("branch-0", VisualizedAgentCoding.Domain.Blueprint.PortKind.Exec,
            VisualizedAgentCoding.Domain.Blueprint.NodeRelation.Exec, IsInput: false, Label: "启动"));
        foreach (var root in document.Roots.ToList())
        {
            root.Parent = start;
            start.Children.Add(root);
        }
        document.Roots.Clear();
        document.Roots.Add(start);
    }

    public void ReconnectBlueprintNodes(VisualizedAgentCoding.Domain.Blueprint.BlueprintNode source,
        VisualizedAgentCoding.Domain.Blueprint.BlueprintNode target)
    {
        if (currentBlueprintBody is null) return;
        var validation = BlueprintReconnectValidator.Validate(source, target);
        if (!validation.IsValid)
        {
            BlueprintEditor.SetStaleError(string.Join("；", validation.Problems));
            return;
        }
        if (BlueprintEditor.Document is not { } current) return;
        foreach (var node in WalkBlueprintNodes(current.Roots))
            node.HasPosition = true;

        if (source.Kind == VisualizedAgentCoding.Domain.Blueprint.NodeKind.Start)
        {
            var first = source.Children.FirstOrDefault(child =>
                child.Kind != VisualizedAgentCoding.Domain.Blueprint.NodeKind.Comment);
            if (first is null || first.Id == target.Id) return;
            var moving = WithBoundComments(current, target);
            var movedToStart = BlueprintSourceReorder.MoveBefore(currentBlueprintBody, moving, first.Source);
            if (movedToStart != currentBlueprintBody) ApplyBlueprintBody(movedToStart);
            return;
        }

        var updated = BlueprintSourceReorder.MoveAfter(currentBlueprintBody, WithBoundComments(current, target), source.Source);
        if (updated == currentBlueprintBody) return;
        ApplyBlueprintBody(updated);
    }

    public void BindBlueprintComment(VisualizedAgentCoding.Domain.Blueprint.BlueprintNode comment,
        VisualizedAgentCoding.Domain.Blueprint.BlueprintNode target)
    {
        if (comment.Kind != VisualizedAgentCoding.Domain.Blueprint.NodeKind.Comment
            || currentBlueprintBody is null) return;
        comment.AnnotationTarget = target;
        var updated = comment.Source.Length > 0 && target.Source.Length > 0
            ? BlueprintSourceReorder.MoveBefore(currentBlueprintBody, comment.Source, target.Source)
            : currentBlueprintBody;
        if (updated != currentBlueprintBody)
        {
            ApplyBlueprintBody(updated);
            return;
        }
        BlueprintEditor.RefreshDocument();
    }

    public void ArrangeBlueprintNodes()
    {
        if (BlueprintEditor.Document is not { } document) return;
        new BlueprintLayoutEngine().AutoArrange(document);
        BlueprintEditor.RefreshDocument();
        _ = SaveBlueprintLayoutAsync();
    }

    public async Task SaveBlueprintLayoutAsync()
    {
        if (activeProject is null || BlueprintEditor.Document is not { } document) return;
        try
        {
            var existing = await blueprintLayoutStore.LoadAsync(activeProject.RootPath);
            var layout = BlueprintLayoutMapper.Capture(document, existing);
            await blueprintLayoutStore.SaveAsync(activeProject.RootPath, layout);
        }
        catch (Exception)
        {
            // Layout is disposable view state; never block editing.
        }
    }

    public void UpdateBlueprintValue(VisualizedAgentCoding.Domain.Blueprint.BlueprintNode node,
        VisualizedAgentCoding.Domain.Blueprint.NodePort port, string value)
    {
        if (currentBlueprintBody is null || port.DefaultText == value) return;
        var old = port.DefaultText ?? string.Empty;
        if (old.Length == 0) return;
        var start = Math.Clamp(node.Source.Start, 0, currentBlueprintBody.Length);
        var length = Math.Clamp(node.Source.Length, 0, currentBlueprintBody.Length - start);
        var statement = currentBlueprintBody.Substring(start, length);
        var index = statement.IndexOf(old, StringComparison.Ordinal);
        if (index < 0) return;
        var updated = currentBlueprintBody.Remove(start + index, old.Length).Insert(start + index, value);
        ApplyBlueprintBody(updated);
    }

    public void DeleteSelectedBlueprintNode()
    {
        if (BlueprintEditor.Inspector.Node is not { } node || currentBlueprintBody is null) return;
        var updated = RemoveNode(currentBlueprintBody, node.Source);
        ApplyBlueprintBody(updated);
    }

    public void InsertBlueprintStatement() => InsertBlueprintStatement(BlueprintEditor.Inspector.NewStatement);

    public void InsertBlueprintStatement(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || currentBlueprintBody is null) return;
        var node = BlueprintEditor.Inspector.Node;
        var updated = node is null
            ? AppendStatement(currentBlueprintBody, text)
            : InsertAfter(currentBlueprintBody, node.Source, text);
        ApplyBlueprintBody(updated);
    }

    private void ApplyBlueprintBody(string body)
    {
        if (editorProjection is not MultiRegionProjection projection || selectedBlueprintRegionId is null)
            return;
        projection.UpdateRegions(new Dictionary<string, string> { [selectedBlueprintRegionId] = body });
        PythonCode = projection.FullSource;
    }

    private static VisualizedAgentCoding.Domain.Blueprint.SourceSpan WithBoundComments(
        VisualizedAgentCoding.Domain.Blueprint.BlueprintDocument document,
        VisualizedAgentCoding.Domain.Blueprint.BlueprintNode target)
    {
        var comments = WalkBlueprintNodes(document.Roots)
            .Where(node => node.Kind == VisualizedAgentCoding.Domain.Blueprint.NodeKind.Comment
                && node.AnnotationTarget?.Id == target.Id
                && node.Source.Length > 0)
            .ToList();
        if (comments.Count == 0 || target.Source.Length <= 0) return target.Source;
        var start = comments.Min(node => node.Source.Start);
        var end = target.Source.End;
        return new VisualizedAgentCoding.Domain.Blueprint.SourceSpan(
            start, end - start, target.Source.StartLine, target.Source.EndLine);
    }

    private static IEnumerable<VisualizedAgentCoding.Domain.Blueprint.BlueprintNode> WalkBlueprintNodes(
        IEnumerable<VisualizedAgentCoding.Domain.Blueprint.BlueprintNode> roots)
    {
        foreach (var root in roots)
        {
            yield return root;
            foreach (var child in WalkBlueprintNodes(root.AllChildren)) yield return child;
        }
    }

    private static string RemoveNode(string body, VisualizedAgentCoding.Domain.Blueprint.SourceSpan span)
    {
        if (span.Length <= 0) return body;
        var start = Math.Clamp(span.Start, 0, body.Length);
        var end = Math.Clamp(span.Start + span.Length, start, body.Length);
        while (start > 0 && body[start - 1] is not ('\n' or '\r')) start--;
        while (end < body.Length && body[end] is not ('\n' or '\r')) end++;
        if (end < body.Length && body[end] == '\r') end++;
        if (end < body.Length && body[end] == '\n') end++;
        return body.Remove(start, end - start);
    }

    private static string AppendStatement(string body, string text)
    {
        var newline = body.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return body.Length == 0 || body.EndsWith('\n')
            ? body + text + newline
            : body + newline + text + newline;
    }

    private static string InsertAfter(string body, VisualizedAgentCoding.Domain.Blueprint.SourceSpan span, string text)
    {
        var newline = body.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var end = Math.Clamp(span.Start + span.Length, 0, body.Length);
        while (end < body.Length && body[end] is not ('\n' or '\r')) end++;
        if (end < body.Length && body[end] == '\r') end++;
        if (end < body.Length && body[end] == '\n') end++;
        var lineStart = body.LastIndexOf('\n', Math.Max(0, span.Start - 1)) + 1;
        var indentLength = 0;
        while (lineStart + indentLength < body.Length
               && body[lineStart + indentLength] is ' ' or '\t') indentLength++;
        var indent = body.Substring(lineStart, indentLength);
        return end >= body.Length
            ? AppendStatement(body, indent + text)
            : body.Insert(end, indent + text + newline);
    }

    private static void AppendBlockLines(ICollection<BlockLine> lines, IReadOnlyList<PythonNode>? nodes, int depth)
    {
        foreach (var node in nodes ?? [])
        {
            if (node.Type == "comment")
            {
                lines.Add(new BlockLine(node.Text ?? "#", depth, IsRaw: true));
                continue;
            }
            if (node.Type == "raw")
            {
                lines.Add(new BlockLine(node.Text ?? "原始代码", depth, IsRaw: true));
                continue;
            }
            if (node.Type == "motion.move")
                lines.Add(new BlockLine(DescribeBlock(node), depth, Type: node.Type,
                    Prefix: "移动", Parameter: Argument(node, "steps"), Suffix: "步"));
            else if (node.Type == "motion.turn")
                lines.Add(new BlockLine(DescribeBlock(node), depth, Type: node.Type,
                    Prefix: "旋转", Parameter: Argument(node, "degrees"), Suffix: "度"));
            else
                lines.Add(new BlockLine(DescribeBlock(node), depth));
            AppendBlockLines(lines, node.Children, depth + 1);
            if (node.ElseChildren is { Length: > 0 })
            {
                lines.Add(new BlockLine("否则", depth));
                AppendBlockLines(lines, node.ElseChildren, depth + 1);
            }
        }
    }

    private static string DescribeBlock(PythonNode node) => node.Type switch
    {
        "motion.move" => $"移动 {Argument(node, "steps")} 步",
        "motion.turn" => $"旋转 {Argument(node, "degrees")} 度",
        "motion.goto" => $"移动到 x: {Argument(node, "x")}，y: {Argument(node, "y")}",
        "motion.glide_to" => $"在 {Argument(node, "seconds")} 秒内滑行到 x: {Argument(node, "x")}，y: {Argument(node, "y")}",
        "looks.switch_costume" => $"切换到造型 {Argument(node, "name")}",
        "control.wait" => $"等待 {Argument(node, "seconds")} 秒",
        "control.repeat" => $"重复执行 {Argument(node, "count")} 次",
        "control.repeat_range" => $"计数循环 {Argument(node, "name")}：从 {Argument(node, "start")} 到小于 {Argument(node, "stop")}，每步 {Argument(node, "step")}",
        "control.for_each" => $"对列表 {Argument(node, "name")} 中的每一项 {Argument(node, "item")}",
        "control.if" => $"如果 {Argument(node, "condition")}",
        "data.set" => $"设置变量 {Argument(node, "name")} 为 {Argument(node, "value")}",
        "data.add" => $"增加变量 {Argument(node, "name")} 的值 {Argument(node, "value")}",
        "output.print" => $"输出 {Argument(node, "value")}",
        _ => node.Type,
    };

    private static string Argument(PythonNode node, string name) =>
        node.Arguments?.GetValueOrDefault(name) ?? "?";
}
