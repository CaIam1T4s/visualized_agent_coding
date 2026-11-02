using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VisualizedAgentCoding.Application.Editor.ObjectScripts;
using VisualizedAgentCoding.Application.Python;
using VisualizedAgentCoding.Domain.Projects;

namespace happy_agent_coding.ViewModels;

public partial class MainViewModel
{
    public BlockEditorModel BlockEditor { get; } = new();
    private CancellationTokenSource? blockRefreshCancellation;

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
                BlockEditor.Clear("打开角色脚本后显示启动区域积木。");
                return;
            }
            if (script.Kind != ProjectScriptKind.Object)
            {
                BlockEditor.Clear("当前脚本没有可预览的对象区域。");
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
                BlockEditor.Clear("正在等待脚本结构确认…");
                return;
            }
            var region = projection.Document.Regions?
                .FirstOrDefault(item => item.Kind == "start");
            if (region is null)
            {
                BlockEditor.Clear("当前脚本没有启动区域。");
                return;
            }
            if (executionBridge is null)
            {
                BlockEditor.SetError("Python Helper 不可用，无法读取积木。");
                return;
            }
            var body = projection.ViewRegionSources().GetValueOrDefault(region.Id);
            if (body is null)
            {
                BlockEditor.SetError("无法读取启动区域源码。");
                return;
            }
            BlockEditor.SetLoading();
            var tree = await executionBridge.BuildBlockTreeAsync(new PythonBlockRequest(body), token);
            if (token.IsCancellationRequested) return;
            if (tree.Error is { Length: > 0 } error)
            {
                BlockEditor.SetError("无法解析启动区域：" + error);
                return;
            }
            var title = region.EventName is null
                ? "当启动时"
                : MessageEvents.Title(region.EventName, projection.Document.BackdropNames);
            var lines = new List<BlockLine> { new(title, 0, IsHat: true) };
            AppendBlockLines(lines, tree.Nodes, 1);
            BlockEditor.SetTree(title, lines,
                lines.Count == 1 ? "启动区域暂时没有积木。" : "只读预览 · 尚未接入编辑");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested) BlockEditor.SetError("读取积木失败：" + ex.Message);
        }
    }

    private static void AppendBlockLines(ICollection<BlockLine> lines, IReadOnlyList<PythonBlockNode>? nodes, int depth)
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

    private static string DescribeBlock(PythonBlockNode node) => node.Type switch
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

    private static string Argument(PythonBlockNode node, string name) =>
        node.Arguments?.GetValueOrDefault(name) ?? "?";
}
