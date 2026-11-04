using System;
using System.Collections.Generic;
using System.Linq;
using VisualizedAgentCoding.Domain.Blueprint;

namespace VisualizedAgentCoding.Blueprint.Core;

public sealed record ReconnectValidation(bool IsValid, IReadOnlyList<string> Problems)
{
    public static ReconnectValidation Ok { get; } = new(true, []);
    public static ReconnectValidation Fail(params string[] problems) => new(false, problems);
}

/// <summary>
/// Conservative checks for endpoint reconnects. Cross-method and cross-loop moves are
/// intentionally rejected until an explicit insertion slot is available.
/// </summary>
public static class BlueprintReconnectValidator
{
    public static ReconnectValidation Validate(BlueprintNode source, BlueprintNode target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        var problems = new List<string>();
        if (source.Id == target.Id) problems.Add("不能把节点连接到自身。");
        if (source.Kind == NodeKind.Start)
        {
            if (target.Parent?.Id == source.Id) return ReconnectValidation.Ok;
            return ReconnectValidation.Fail("起始节点只能连接到它的第一条语句。");
        }
        if (source.Parent != target.Parent) problems.Add("首版只允许同一语句链内重连。");
        if (IsDescendant(target, source)) problems.Add("不能把节点连接到自己或自己的子节点。");
        if (!source.Ports.Any(port => port.IsExec && !port.IsInput)) problems.Add("源节点没有执行输出。");
        if (!target.Ports.Any(port => port.IsExec && port.IsInput)) problems.Add("目标节点没有执行输入。");
        if (source.Type is "control.return" or "control.break" or "control.continue")
            problems.Add("终止节点没有后继，不能作为重连源。");
        var targetLoop = EnclosingLoop(target);
        if (source.Type is "control.break" or "control.continue")
        {
            var sourceLoop = EnclosingLoop(source);
            if (sourceLoop is null || targetLoop?.Id != sourceLoop.Id)
                problems.Add("break/continue 只能留在原循环体内。");
        }
        if (source.Type == "control.return" && target.Type != "control.return")
            problems.Add("return 不能移动到普通语句之后。");
        return problems.Count == 0 ? ReconnectValidation.Ok : new ReconnectValidation(false, problems);
    }

    private static bool IsDescendant(BlueprintNode candidate, BlueprintNode ancestor) =>
        candidate.Parent is not null && (candidate.Parent.Id == ancestor.Id || IsDescendant(candidate.Parent, ancestor));

    private static BlueprintNode? EnclosingLoop(BlueprintNode node)
    {
        var current = node.Parent;
        while (current is not null)
        {
            if (current.Type is "control.while" or "control.repeat" or "control.repeat_range" or "control.for_each")
                return current;
            current = current.Parent;
        }
        return null;
    }
}
