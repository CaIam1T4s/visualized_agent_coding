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
            node.InlineComment = inlineComment.TrimStart('#').Trim();
        if (node.Kind == NodeKind.Comment)
        {
            node.DisplayName = "注释";
            node.IsCollapsed = true;
        }
        AddPorts(node, source);
        AddValueInputs(node);
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
        var index = 0;
        while (index < nodes.Count)
        {
            if (nodes[index].Kind != NodeKind.Comment)
            {
                index++;
                continue;
            }

            var current = nodes[index];
            while (index + 1 < nodes.Count && nodes[index + 1].Kind == NodeKind.Comment)
            {
                var next = nodes[index + 1];
                current.Text = JoinCommentText(current.Text, next.Text);
                if (current.Source.Length > 0 && next.Source.Length > 0)
                {
                    var startOffset = Math.Min(current.Source.Start, next.Source.Start);
                    var endOffset = Math.Max(current.Source.End, next.Source.End);
                    current.Source = new SourceSpan(startOffset, endOffset - startOffset,
                        current.Source.StartLine, next.Source.EndLine);
                }
                nodes.RemoveAt(index + 1);
            }

            current.AnnotationTarget = index + 1 < nodes.Count ? nodes[index + 1] : null;
            index++;
        }
    }

    private static string JoinCommentText(string? first, string? second)
    {
        var parts = new[] { first, second }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim())
            .ToArray();
        return string.Join("\n", parts);
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
        if (type == "comment")
        {
            node.Ports.Add(new("annotation", PortKind.Value, NodeRelation.Annotation,
                "annotation", IsInput: false, Label: "绑定"));
            return;
        }
        if (type is "control.break" or "control.continue")
        {
            node.Ports.Add(new("exec-in", PortKind.Exec, NodeRelation.Next, IsInput: true));
            return;
        }
        if (type == "control.return")
        {
            node.Ports.Add(new("exec-in", PortKind.Exec, NodeRelation.Next, IsInput: true));
            node.Ports.Add(new("value", PortKind.Value, NodeRelation.Value, "any", IsInput: true,
                IsOptional: true, Label: "返回值", DefaultText: Argument(source, "value")));
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
            node.Ports.Add(new("condition-0", PortKind.Value, NodeRelation.Value, "boolean", IsInput: true,
                Label: "条件", DefaultText: Argument(source, "condition")));
            node.Ports.Add(new("branch-0", PortKind.Exec, NodeRelation.Exec, IsInput: false, Label: "循环体"));
            return;
        }
        if (type is "control.repeat" or "control.repeat_range" or "control.for_each")
        {
            AddExecNodePorts(node);
            AddArgumentPorts(node, source);
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
            node.Ports.Add(new("pattern", PortKind.Value, NodeRelation.Value, "any", IsInput: true,
                Label: "模式", DefaultText: Argument(source, "pattern")));
            node.Ports.Add(new("guard", PortKind.Value, NodeRelation.Value, "boolean", IsInput: true,
                IsOptional: true, Label: "guard", DefaultText: Argument(source, "guard")));
            return;
        }
        if (type == "control.branch")
        {
            node.Ports.Add(new("branch-0", PortKind.Exec, NodeRelation.Exec, IsInput: false,
                Label: "条件", DefaultText: Argument(source, "condition")));
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
        AddArgumentPorts(node, source);
    }

    private static void AddValueInputs(BlueprintNode node)
    {
        foreach (var port in node.Ports.Where(port => port.IsInput && port.IsValue))
        {
            var expression = port.DefaultText?.Trim();
            if (string.IsNullOrEmpty(expression) || port.Type == "string" || IsBasicLiteral(expression)) continue;
            var condition = port.Type == "boolean";
            var input = new BlueprintNode
            {
                Id = node.Id + ":value:" + port.Id,
                Type = condition ? "condition.expression" : "value.expression",
                Kind = NodeKind.Expression,
                DisplayName = condition ? "条件" : "值",
                Text = expression,
                ValueInputPortId = port.Id,
                Parent = node,
            };
            input.Ports.Add(new("value-out", PortKind.Value, NodeRelation.Value,
                port.Type ?? "any", IsInput: false, Label: "值"));
            node.ValueInputs.Add(input);
        }
    }

    private static bool IsBasicLiteral(string text)
    {
        var value = text.Trim();
        if (value.Length == 0) return true;
        if (value is "True" or "False" or "None") return true;
        if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"')
            || (value[0] == '\'' && value[^1] == '\''))) return true;
        return double.TryParse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out _);
    }

    private static void AddArgumentPorts(BlueprintNode node, PythonNode source)
    {
        foreach (var pair in source.Arguments ?? [])
        {
            if (pair.Key is "comment" or "target" or "kind" or "owner") continue;
            node.Ports.Add(new("arg-" + pair.Key, PortKind.Value, NodeRelation.Value,
                ParameterType(pair.Key), IsInput: true, Label: ParameterLabel(pair.Key),
                DefaultText: pair.Value));
        }
    }

    private static string ParameterType(string key) => key switch
    {
        "name" => "string",
        "condition" or "guard" => "boolean",
        _ => "any",
    };

    private static string ParameterLabel(string key) => key switch
    {
        "steps" => "步数",
        "degrees" => "角度",
        "x" => "X 坐标",
        "y" => "Y 坐标",
        "seconds" => "秒数",
        "name" => "名称",
        "value" => "值",
        "count" => "次数",
        "start" => "开始",
        "stop" => "结束",
        "step" => "步长",
        "item" => "项目",
        "target" => "目标",
        "owner" => "所有者",
        "kind" => "作用域",
        "operator" => "运算符",
        "annotation" => "类型注解",
        "parameters" => "参数",
        "decorators" => "装饰器",
        "bases" => "基类",
        "condition" => "条件",
        "subject" => "匹配值",
        "pattern" => "模式",
        "guard" => "guard",
        _ => key,
    };

    private static void AddIfPorts(BlueprintNode node, PythonNode source)
    {
        AddExecNodePorts(node);
        var branches = source.Children ?? [];
        var hasElse = (source.ElseChildren?.Length ?? 0) > 0;
        var branchCount = Math.Max(1, branches.Length + (hasElse ? 1 : 0));
        for (var index = 0; index < branchCount; index++)
        {
            var isDefault = index >= branches.Length;
            var condition = isDefault ? "" : branches[index].Arguments?.GetValueOrDefault("condition") ?? "";
            node.Ports.Add(new($"condition-{index}", PortKind.Value, NodeRelation.Value, "boolean",
                IsInput: true, IsOptional: isDefault,
                Label: isDefault ? "否则" : "条件", DefaultText: condition));
            node.Ports.Add(new($"branch-{index}", PortKind.Exec, NodeRelation.Exec, IsInput: false,
                IsOptional: isDefault, Label: isDefault ? "否则" : "分支"));
        }
    }

    private static void AddMatchPorts(BlueprintNode node, PythonNode source)
    {
        AddExecNodePorts(node);
        node.Ports.Add(new("subject", PortKind.Value, NodeRelation.Value, "any", IsInput: true,
            Label: "匹配值", DefaultText: Argument(source, "subject")));
        var cases = source.Children ?? [];
        var caseCount = Math.Max(1, cases.Length);
        for (var index = 0; index < caseCount; index++)
        {
            var pattern = index < cases.Length ? cases[index].Arguments?.GetValueOrDefault("pattern") ?? "" : "_";
            var isDefault = pattern == "_";
            node.Ports.Add(new($"case-{index}", PortKind.Value, NodeRelation.Value, "any",
                IsInput: true, IsOptional: isDefault, Label: "模式", DefaultText: pattern));
            node.Ports.Add(new($"branch-{index}", PortKind.Exec, NodeRelation.Exec, IsInput: false,
                IsOptional: isDefault, Label: isDefault ? "默认" : "分支"));
        }
    }

    private static void AddExecNodePorts(BlueprintNode node)
    {
        node.Ports.Add(new("exec-in", PortKind.Exec, NodeRelation.Next, IsInput: true));
        node.Ports.Add(new("exec-out", PortKind.Exec, NodeRelation.Next, IsInput: false, Label: "下一条"));
    }

    private static string Describe(PythonNode node) => node.Type switch
    {
        "motion.move" => "移动",
        "motion.turn" => "旋转",
        "motion.goto" => "移动到",
        "motion.glide_to" => "滑行到",
        "looks.switch_costume" => "切换到造型",
        "control.wait" => "等待",
        "control.repeat" => "重复执行",
        "control.repeat_range" => "计数循环",
        "control.for_each" => "对列表每一项",
        "control.if" => "如果",
        "control.branch" => "条件",
        "control.while" => "循环",
        "control.match" => "匹配",
        "control.case" => "case",
        "control.return" => "返回",
        "control.break" => "跳出循环",
        "control.continue" => "继续下一次循环",
        "control.pass" => "空语句",
        "data.set" => "设置变量",
        "data.add" => "增加变量",
        "data.set_local" => "设置变量",
        "data.add_local" => "修改变量",
        "data.set_attribute" => "设置属性",
        "data.add_attribute" => "修改属性",
        "data.set_class_attribute" => "设置类属性",
        "data.add_class_attribute" => "修改类属性",
        "data.annotate" => "声明",
        "output.print" => "输出",
        "def" => "定义",
        "class" => "类",
        "comment" => "注释",
        "docstring" => "文档字符串",
        "raw" => "原始代码",
        _ => node.Type,
    };

    private static string Argument(PythonNode node, string name) =>
        node.Arguments?.GetValueOrDefault(name) ?? "?";
}
