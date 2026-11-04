using VisualizedAgentCoding.Application.Python;
using VisualizedAgentCoding.Blueprint.Core;
using VisualizedAgentCoding.Domain.Blueprint;

namespace VisualizedAgentCoding.App.Tests;

public sealed class NodeProjectionTests
{
    [Fact]
    public void ProjectsSourceSpansKindsAndTypedPorts()
    {
        var tree = new PythonNodeTree(null,
        [
            new PythonNode("control.if",
                new Dictionary<string, string> { ["condition"] = "ready" },
                [new PythonNode("motion.move", new Dictionary<string, string> { ["steps"] = "10" }, [], [], "", 2, 24, 22, 2)],
                [new PythonNode("control.pass", [], [], [], "", 4, 48, 8, 4)],
                "", 1, 4, 40, 4)
        ]);

        var document = NodeProjection.ToDocument(tree, "script-1", "start", "当启动时");

        Assert.Equal("当启动时", document.EventTitle);
        var root = Assert.Single(document.Roots);
        Assert.Equal(NodeKind.ControlFlow, root.Kind);
        Assert.Equal(new SourceSpan(4, 40, 1, 4), root.Source);
        Assert.Contains(root.Ports, port => port.Id == "condition-0" && port.Type == "boolean");
        Assert.Contains(root.Ports, port => port.Relation == NodeRelation.Exec && port.IsInput == false);
        Assert.Equal("motion.move", Assert.Single(root.Children).Type);
        Assert.Equal("control.pass", Assert.Single(root.ElseChildren).Type);
    }

    [Fact]
    public void MatchUsesMatchStatementAndCasePorts()
    {
        var tree = new PythonNodeTree(null,
        [
            new PythonNode("control.match",
                new Dictionary<string, string> { ["subject"] = "command" },
                [
                    new PythonNode("control.case",
                        new Dictionary<string, string> { ["pattern"] = "\"go\"", ["guard"] = "speed > 0" },
                        [new PythonNode("motion.move", new Dictionary<string, string> { ["steps"] = "1" }, [], [], "", 3, 40, 10, 3)],
                        [], "", 2, 20, 30, 3)
                ],
                [], "", 2, 10, 60, 3)
        ]);
        var document = NodeProjection.ToDocument(tree, "s", "r");
        var match = document.Roots[0];
        Assert.Contains(match.Ports, port => port.Id == "subject");
        Assert.Contains(match.Ports, port => port.Relation == NodeRelation.Exec && port.IsInput == false);
        var caseNode = Assert.Single(match.Children);
        Assert.Contains(caseNode.Ports, port => port.Id == "pattern");
        Assert.Contains(caseNode.Ports, port => port.Id == "guard" && port.IsOptional);
    }

    [Fact]
    public void CommentBindsToFollowingNodeAndHasNoPorts()
    {
        var tree = new PythonNodeTree(null,
        [
            new PythonNode("comment", [], [], [], "# note", 1, 0, 6, 1),
            new PythonNode("motion.move", new Dictionary<string, string> { ["steps"] = "1" }, [], [], "", 2, 8, 12, 2)
        ]);
        var document = NodeProjection.ToDocument(tree, "s", "r");
        var comment = document.Roots[0];
        Assert.Equal(NodeKind.Comment, comment.Kind);
        var annotation = Assert.Single(comment.Ports);
        Assert.Equal(NodeRelation.Annotation, annotation.Relation);
        Assert.Same(document.Roots[1], comment.AnnotationTarget);
        Assert.Equal("# note", comment.Text);
    }


    [Fact]
    public void TitlesAreTypeOnlyAndParametersBecomeInputValues()
    {
        var tree = new PythonNodeTree(null,
        [
            new PythonNode("motion.move", new Dictionary<string, string> { ["steps"] = "10" }, [], [], "", 1, 0, 12, 1),
            new PythonNode("control.case",
                new Dictionary<string, string> { ["pattern"] = "\"go\"", ["guard"] = "speed > 0" },
                [], [], "", 2, 20, 30, 2)
        ]);

        var document = NodeProjection.ToDocument(tree, "s", "r");

        Assert.Equal("移动", document.Roots[0].DisplayName);
        var moveRow = Assert.Single(BlueprintNodeRows.Build(document.Roots[0]));
        Assert.Equal("步数", moveRow.Label);
        Assert.Equal("10", moveRow.Value);

        var caseNode = document.Roots[1];
        Assert.Equal("case", caseNode.DisplayName);
        var caseRows = BlueprintNodeRows.Build(caseNode);
        Assert.Equal(2, caseRows.Count);
        Assert.Equal(("模式", "\"go\""), (caseRows[0].Label, caseRows[0].Value));
        Assert.Equal(("guard", "speed > 0"), (caseRows[1].Label, caseRows[1].Value));
        Assert.Null(caseRows[1].Output);
    }


    [Fact]
    public void WhileIsNamedLoopAndComplexConditionGetsValueInputNode()
    {
        var tree = new PythonNodeTree(null,
        [
            new PythonNode("control.while",
                new Dictionary<string, string> { ["condition"] = "self.x > 0" },
                [], [], "", 1, 0, 20, 1),
            new PythonNode("control.while",
                new Dictionary<string, string> { ["condition"] = "True" },
                [], [], "", 3, 30, 20, 3)
        ]);

        var document = NodeProjection.ToDocument(tree, "s", "r");

        Assert.Equal("循环", document.Roots[0].DisplayName);
        var condition = Assert.Single(document.Roots[0].ValueInputs);
        Assert.Equal("condition.expression", condition.Type);
        Assert.Equal("self.x > 0", condition.Text);
        Assert.Empty(document.Roots[1].ValueInputs);
    }


    [Fact]
    public void NameParameterIsStringAndDoesNotCreateValueNode()
    {
        var tree = new PythonNodeTree(null,
        [
            new PythonNode("data.set_local",
                new Dictionary<string, string> { ["name"] = "health", ["value"] = "1" },
                [], [], "", 1, 0, 20, 1)
        ]);

        var document = NodeProjection.ToDocument(tree, "s", "r");
        var node = document.Roots[0];

        Assert.Empty(node.ValueInputs);
        var name = Assert.Single(node.Ports, port => port.Label == "名称");
        Assert.Equal("string", name.Type);
        Assert.Equal("health", name.DefaultText);
    }

    [Fact]
    public void CommentTargetingCommentMergesAndKeepsFinalTarget()
    {
        var tree = new PythonNodeTree(null,
        [
            new PythonNode("comment", [], [], [], "gay\npussy", 1, 0, 8, 1),
            new PythonNode("comment", [], [], [], "line", 2, 10, 4, 2),
            new PythonNode("motion.move", new Dictionary<string, string> { ["steps"] = "1" }, [], [], "", 3, 20, 12, 3)
        ]);

        var document = NodeProjection.ToDocument(tree, "s", "r");

        Assert.Equal(2, document.Roots.Count);
        var comment = document.Roots[0];
        Assert.Equal("gay\npussy\nline", comment.Text);
        Assert.Same(document.Roots[1], comment.AnnotationTarget);
    }

    [Fact]
    public void CommentHasNoPortsAndProjectVariableUsesGeneratedCode()
    {
        var tree = new PythonNodeTree(null,
        [
            new PythonNode("comment", [], [], [], "# note", 1, 0, 6, 1),
            new PythonNode("data.set_local", new Dictionary<string, string> { ["name"] = "total", ["value"] = "1" }, [], [], "", 2, 8, 9, 2)
        ]);
        var document = NodeProjection.ToDocument(tree, "s", "r");
        Assert.Contains(document.Roots[0].Ports, port => port.Relation == NodeRelation.Annotation);

        var variable = new VariableRef(NodeScope.ProjectVariable, "score");
        Assert.Equal("score  项目", variable.DisplayName);
        Assert.Equal("get_variable(\"score\")", variable.GeneratedCode);
    }
}
