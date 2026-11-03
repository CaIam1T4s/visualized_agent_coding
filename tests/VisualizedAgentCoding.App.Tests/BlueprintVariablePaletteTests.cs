using happy_agent_coding.ViewModels;
using VisualizedAgentCoding.Domain.Blueprint;

namespace VisualizedAgentCoding.App.Tests;

public sealed class BlueprintVariablePaletteTests
{
    [Fact]
    public void GroupsSameNamedVariablesByScope()
    {
        var palette = new BlueprintVariablePalette();
        palette.SetVariables(
        [
            new VariableRef(NodeScope.InstanceAttribute, "health", "Actor"),
            new VariableRef(NodeScope.ClassAttribute, "health", "Base"),
            new VariableRef(NodeScope.ProjectVariable, "score"),
            new VariableRef(NodeScope.Local, "health", OwnerMethod: "on_start"),
        ]);

        Assert.Equal(["局部", "实例属性", "类属性", "项目变量"], palette.Groups.Select(group => group.Title));
        Assert.Equal("health  实例@Actor", palette.Groups[1].Items[0].DisplayName);
        Assert.Equal("health  类@Base", palette.Groups[2].Items[0].DisplayName);
    }

    [Fact]
    public void SearchFiltersAcrossGroupsAndKeepsScope()
    {
        var palette = new BlueprintVariablePalette();
        palette.SetVariables(
        [
            new VariableRef(NodeScope.InstanceAttribute, "health", "Actor"),
            new VariableRef(NodeScope.ProjectVariable, "score"),
        ]);

        palette.SearchText = "score";

        Assert.False(palette.Groups[0].HasMatches);
        Assert.True(palette.Groups[1].HasMatches);
        Assert.Single(palette.Groups[1].FilteredItems);
    }

    [Fact]
    public void AddKeepsCollapsedStateAndDoesNotDuplicate()
    {
        var palette = new BlueprintVariablePalette();
        palette.SetVariables([new VariableRef(NodeScope.Local, "total", OwnerMethod: "run")]);
        palette.Groups[0].IsExpanded = false;

        palette.Add(new VariableRef(NodeScope.Local, "total", OwnerMethod: "run"));
        palette.Add(new VariableRef(NodeScope.Local, "other", OwnerMethod: "run"));

        Assert.False(palette.Groups[0].IsExpanded);
        Assert.Equal(2, palette.Groups[0].Items.Count);
    }
}
