using happy_agent_coding.ViewModels;
using VisualizedAgentCoding.Domain.Blueprint;

namespace VisualizedAgentCoding.App.Tests;

public sealed class BlueprintNodeInspectorTests
{
    [Fact]
    public void EditingAnArgumentUpdatesTheNode()
    {
        var node = new BlueprintNode
        {
            Id = "n",
            Type = "motion.move",
            DisplayName = "移动 10 步",
            Arguments = { ["steps"] = "10" },
        };
        var inspector = new BlueprintNodeInspector();
        inspector.SetNode(node);

        inspector.Arguments[0].Value = "20";

        Assert.Equal("20", node.Arguments["steps"]);
        Assert.Equal("节点", "节点");
    }

    [Fact]
    public void HoverUsesScopeAndUnknownValuePlaceholder()
    {
        var variable = new VariableRef(NodeScope.InstanceAttribute, "health", "Actor");
        Assert.Equal("实例属性：health = /", variable.HoverText);
        Assert.Equal("项目变量：score = 0", new VariableRef(NodeScope.ProjectVariable, "score") { ValueText = "0" }.HoverText);
    }
}
