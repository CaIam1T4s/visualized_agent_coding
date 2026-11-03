using VisualizedAgentCoding.Application.Python;
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
        Assert.Empty(comment.Ports);
        Assert.Same(document.Roots[1], comment.AnnotationTarget);
        Assert.Equal("# note", comment.Text);
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
        Assert.Empty(document.Roots[0].Ports);

        var variable = new VariableRef(NodeScope.ProjectVariable, "score");
        Assert.Equal("score  项目", variable.DisplayName);
        Assert.Equal("get_variable(\"score\")", variable.GeneratedCode);
    }
}
