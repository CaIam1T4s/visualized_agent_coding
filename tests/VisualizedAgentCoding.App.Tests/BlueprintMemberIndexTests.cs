using VisualizedAgentCoding.Application.Editor;
using VisualizedAgentCoding.Application.Python;
using VisualizedAgentCoding.Domain.Blueprint;

namespace VisualizedAgentCoding.App.Tests;

public sealed class BlueprintMemberIndexTests
{
    [Fact]
    public void CollectsInstanceAndClassAttributesAndProjectVariables()
    {
        var document = new BlueprintDocument { ScriptId = "s", RegionId = "r" };
        document.Roots.Add(new BlueprintNode
        {
            Id = "1",
            Type = "data.set_attribute",
            Arguments = { ["name"] = "health", ["target"] = "self.health" },
        });
        document.Roots.Add(new BlueprintNode
        {
            Id = "2",
            Type = "data.set_class_attribute",
            Arguments = { ["name"] = "max_health", ["owner"] = "Actor" },
        });
        document.Roots.Add(new BlueprintNode
        {
            Id = "3",
            Type = "def",
            Arguments = { ["name"] = "heal", ["parameters"] = "self, amount" },
        });

        var variables = BlueprintMemberIndex.CollectVariables(document, "Actor",
            [new ProjectVariableRef("score", "0")]);

        Assert.Contains(variables, item => item.Scope == NodeScope.InstanceAttribute && item.Name == "health");
        Assert.Contains(variables, item => item.Scope == NodeScope.ClassAttribute && item.Name == "max_health");
        Assert.Contains(variables, item => item.Scope == NodeScope.Parameter && item.Name == "amount");
        Assert.Contains(variables, item => item.Scope == NodeScope.ProjectVariable && item.HoverText == "项目变量：score = 0");
    }

    [Fact]
    public void AttributeRenamePreviewReportsBothAccessForms()
    {
        var source = "self.health = 1\nActor.health = 2\nprint(self.health)";
        var preview = BlueprintMemberIndex.PreviewAttributeRename(source, "Actor", "health", "hp");

        Assert.Equal(3, preview.Count);
        Assert.Contains("self.hp", preview.Occurrences[0].After);
        Assert.Contains("Actor.hp", preview.Occurrences[1].After);
    }

    [Fact]
    public void ImportTemplatesUseAliasAndClassHint()
    {
        var templates = BlueprintMemberIndex.ImportTemplates(
        [
            new ScriptImport("helpers", "Healer", "H"),
            new ScriptImport("math", "sqrt")
        ]);

        Assert.Equal("Healer", templates[0].Name);
        Assert.Equal("H", templates[0].Alias);
        Assert.True(templates[0].IsClass);
        Assert.False(templates[1].IsClass);
    }
}
