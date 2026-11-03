using VisualizedAgentCoding.Domain.Blueprint;

namespace VisualizedAgentCoding.Application.Python;

/// <summary>Maps the helper protocol into the UI-neutral blueprint model.</summary>
public static class NodeProjection
{
    public static BlueprintDocument ToDocument(
        PythonNodeTree tree,
        string scriptId,
        string regionId,
        string? title = null)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptId);
        ArgumentException.ThrowIfNullOrWhiteSpace(regionId);
        var document = new BlueprintDocument
        {
            ScriptId = scriptId,
            RegionId = regionId,
            EventTitle = title,
            Error = tree.Error,
        };
        var roots = tree.Nodes ?? [];
        for (var index = 0; index < roots.Length; index++)
            document.Roots.Add(Map(roots[index], index.ToString(), regionId));
        LinkComments(document.Roots);
        return document;
    }

    private static BlueprintNode Map(PythonNode source, string path, string regionId)
    {
        var node = new BlueprintNode
        {
            Id = $"{regionId}:{path}",
            Type = source.Type,
            Kind = KindOf(source),
            DisplayName = Describe(source),
            Text = source.Text ?? (source.Arguments?.GetValueOrDefault("comment")),
            Source = new SourceSpan(source.Start, source.Length, source.Line, source.EndLine),
        };
        foreach (var pair in source.Arguments ?? [])
            node.Arguments[pair.Key] = pair.Value;
        if (source.Arguments?.GetValueOrDefault("comment") is { Length: > 0 } inlineComment)
            node.DisplayName += "  " + inlineComment;
        AddPorts(node, source);
        var children = source.Children ?? [];
        for (var index = 0; index < children.Length; index++)
        {
            var child = Map(children[index], $"{path}.{index}", regionId);
            child.Parent = node;
            node.Children.Add(child);
        }
        var elseChildren = source.ElseChildren ?? [];
        for (var index = 0; index < elseChildren.Length; index++)
        {
            var child = Map(elseChildren[index], $"{path}.else.{index}", regionId);
            child.Parent = node;
            node.ElseChildren.Add(child);
        }
        LinkComments(node.Children);
        LinkComments(node.ElseChildren);
        return node;
    }

    private static void LinkComments(IList<BlueprintNode> nodes)
    {
        for (var index = 0; index + 1 < nodes.Count; index++)
        {
            if (nodes[index].Kind == NodeKind.Comment)
                nodes[index].AnnotationTarget = nodes[index + 1];
        }
    }

    private static NodeKind KindOf(PythonNode source)
    {
        if (!string.IsNullOrEmpty(source.Kind)
            && Enum.TryParse<NodeKind>(source.Kind, ignoreCase: true, out var explicitKind))
            return explicitKind;
        var type = source.Type;
        if (type is "comment" or "docstring") return NodeKind.Comment;
        if (type is "raw") return NodeKind.Opaque;
        if (type is "def") return NodeKind.Function;
        if (type is "class") return NodeKind.Class;
        if (type.StartsWith("control.", StringComparison.Ordinal)) return NodeKind.ControlFlow;
        if (type.StartsWith("data.", StringComparison.Ordinal)
            || type.StartsWith("expr.", StringComparison.Ordinal)) return NodeKind.Data;
        if (type.StartsWith("motion.", StringComparison.Ordinal)
            || type.StartsWith("looks.", StringComparison.Ordinal)
            || type.StartsWith("output.", StringComparison.Ordinal)) return NodeKind.Statement;
        return NodeKind.Statement;
    }

    private static void AddPorts(BlueprintNode node, PythonNode source)
    {
        var type = source.Type;
        if (type == "comment") return;
        if (type is "control.break" or "control.continue")
        {
            node.Ports.Add(new("exec-in", PortKind.Exec, NodeRelation.Next, IsInput: true));
            return;
        }
        if (type == "control.return")
        {
            node.Ports.Add(new("exec-in", PortKind.Exec, NodeRelation.Next, IsInput: true));
            node.Ports.Add(new("value", PortKind.Value, NodeRelation.Value, "any", IsInput: true, IsOptional: true, Label: "返回值"));
            return;
        }
        if (type == "control.if")
        {
            AddIfPorts(node, source);
            return;
        }
        if (type == "control.while")
        {
            AddExecNodePorts(node);
            node.Ports.Add(new("condition-0", PortKind.Value, NodeRelation.Value, "boolean", IsInput: true, Label: "条件"));
            node.Ports.Add(new("branch-0", PortKind.Exec, NodeRelation.Exec, IsInput: false, Label: "循环体"));
            return;
        }
        if (type is "control.repeat" or "control.repeat_range" or "control.for_each")
        {
            AddExecNodePorts(node);
            foreach (var pair in source.Arguments ?? [])
            {
                if (pair.Key == "comment") continue;
                node.Ports.Add(new("arg-" + pair.Key, PortKind.Value, NodeRelation.Value, "any",
                    IsInput: true, Label: pair.Key));
            }
            node.Ports.Add(new("branch-0", PortKind.Exec, NodeRelation.Exec, IsInput: false, Label: "循环体"));
            return;
        }
        if (type == "control.match")
        {
            AddMatchPorts(node, source);
            return;
        }
        if (type == "control.case")
        {
            AddExecNodePorts(node);
            node.Ports.Add(new("pattern", PortKind.Value, NodeRelation.Value, "any", IsInput: true, Label: "模式"));
            node.Ports.Add(new("guard", PortKind.Value, NodeRelation.Value, "boolean", IsInput: true, IsOptional: true, Label: "guard"));
            return;
        }
        if (type is "def" or "class")
        {
            node.Ports.Add(new("exec-in", PortKind.Exec, NodeRelation.Next, IsInput: true));
            node.Ports.Add(new("exec-out", PortKind.Exec, NodeRelation.Next, IsInput: false, Label: "下一条"));
            return;
        }

        node.Ports.Add(new("exec-in", PortKind.Exec, NodeRelation.Next, IsInput: true));
        node.Ports.Add(new("exec-out", PortKind.Exec, NodeRelation.Next, IsInput: false, Label: "下一条"));
        foreach (var pair in source.Arguments ?? [])
        {
            if (pair.Key == "comment") continue;
            node.Ports.Add(new("arg-" + pair.Key, PortKind.Value, NodeRelation.Value, "any",
                IsInput: true, Label: pair.Key));
        }
    }

    private static void AddIfPorts(BlueprintNode node, PythonNode source)
    {
        AddExecNodePorts(node);
        var branchCount = Math.Max(1, (source.Children?.Length ?? 0) + 1);
        for (var index = 0; index < branchCount; index++)
        {
            var isDefault = index == branchCount - 1;
            node.Ports.Add(new($"condition-{index}", PortKind.Value, NodeRelation.Value, "boolean",
                IsInput: true, IsOptional: isDefault,
                Label: isDefault ? "否则（条件可空）" : $"条件 {index + 1}"));
            node.Ports.Add(new($"branch-{index}", PortKind.Exec, NodeRelation.Exec, IsInput: false,
                IsOptional: isDefault, Label: isDefault ? "否则" : $"分支 {index + 1}"));
        }
    }

    private static void AddMatchPorts(BlueprintNode node, PythonNode source)
    {
        AddExecNodePorts(node);
        node.Ports.Add(new("subject", PortKind.Value, NodeRelation.Value, "any", IsInput: true, Label: "匹配值"));
        var caseCount = Math.Max(1, (source.Children?.Length ?? 0) + 1);
        for (var index = 0; index < caseCount; index++)
        {
            var isDefault = index == caseCount - 1;
            node.Ports.Add(new($"case-{index}", PortKind.Value, NodeRelation.Value, "any",
                IsInput: true, IsOptional: true, Label: isDefault ? "默认 _" : $"模式 {index + 1}"));
            node.Ports.Add(new($"branch-{index}", PortKind.Exec, NodeRelation.Exec, IsInput: false,
                IsOptional: isDefault, Label: isDefault ? "默认 _" : $"case {index + 1}"));
        }
    }

    private static void AddExecNodePorts(BlueprintNode node)
    {
        node.Ports.Add(new("exec-in", PortKind.Exec, NodeRelation.Next, IsInput: true));
        node.Ports.Add(new("exec-out", PortKind.Exec, NodeRelation.Next, IsInput: false, Label: "下一条"));
    }

    private static string Describe(PythonNode node) => node.Type switch
    {
        "motion.move" => $"移动 {Argument(node, "steps")} 步",
        "motion.turn" => $"旋转 {Argument(node, "degrees")} 度",
        "motion.goto" => $"移动到 x: {Argument(node, "x")}，y: {Argument(node, "y")}",
        "motion.glide_to" => $"滑行到 x: {Argument(node, "x")}，y: {Argument(node, "y")}",
        "looks.switch_costume" => $"切换到造型 {Argument(node, "name")}",
        "control.wait" => $"等待 {Argument(node, "seconds")} 秒",
        "control.repeat" => $"重复执行 {Argument(node, "count")} 次",
        "control.repeat_range" => $"计数循环 {Argument(node, "name")}",
        "control.for_each" => $"对列表 {Argument(node, "name")} 的每一项",
        "control.if" => $"如果 {Argument(node, "condition")}",
        "control.while" => $"当 {Argument(node, "condition")}",
        "control.match" => $"匹配 {Argument(node, "subject")}",
        "control.case" => $"case {Argument(node, "pattern")}",
        "control.return" => "返回",
        "control.break" => "跳出循环",
        "control.continue" => "继续下一次循环",
        "control.pass" => "空语句",
        "data.set" => $"设置变量 {Argument(node, "name")}",
        "data.add" => $"增加变量 {Argument(node, "name")}",
        "data.set_local" => $"设置 {Argument(node, "name")}",
        "data.add_local" => $"修改 {Argument(node, "name")}",
        "data.set_attribute" => $"设置 {Argument(node, "target")}",
        "data.add_attribute" => $"修改 {Argument(node, "target")}",
        "data.set_class_attribute" => $"设置 {Argument(node, "target")}",
        "data.add_class_attribute" => $"修改 {Argument(node, "target")}",
        "data.annotate" => $"声明 {Argument(node, "name")}",
        "output.print" => $"输出 {Argument(node, "value")}",
        "def" => $"定义 {Argument(node, "name")}",
        "class" => $"类 {Argument(node, "name")}",
        "comment" => node.Text ?? "# 注释",
        "docstring" => node.Text ?? "文档字符串",
        "raw" => node.Text ?? "原始代码",
        _ => node.Type,
    };

    private static string Argument(PythonNode node, string name) =>
        node.Arguments?.GetValueOrDefault(name) ?? "?";
}
