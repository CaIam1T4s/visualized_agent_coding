using VisualizedAgentCoding.Blueprint.Core;
using VisualizedAgentCoding.Domain.Blueprint;

namespace VisualizedAgentCoding.App.Tests;

public sealed class BlueprintLayoutMapperTests
{
    [Fact]
    public void CaptureAndApplyRoundTripPositionAndCollapse()
    {
        var document = new BlueprintDocument { ScriptId = "s", RegionId = "r" };
        document.Roots.Add(new BlueprintNode
        {
            Id = "n1",
            Type = "motion.move",
            DisplayName = "移动",
            X = 40,
            Y = 50,
            HasPosition = true,
            IsCollapsed = true,
        });

        var layout = BlueprintLayoutMapper.Capture(document);
        document.Roots[0].X = 0;
        document.Roots[0].Y = 0;
        document.Roots[0].IsCollapsed = false;
        BlueprintLayoutMapper.Apply(layout, document);

        Assert.Equal(40, document.Roots[0].X);
        Assert.Equal(50, document.Roots[0].Y);
        Assert.True(document.Roots[0].IsCollapsed);
    }

    [Fact]
    public void CapturePreservesOtherScriptsAndRegions()
    {
        var existing = new BlueprintLayout();
        existing.Scripts["other"] = new BlueprintScriptLayout();
        var document = new BlueprintDocument { ScriptId = "s", RegionId = "r" };

        var layout = BlueprintLayoutMapper.Capture(document, existing);

        Assert.True(layout.Scripts.ContainsKey("other"));
        Assert.True(layout.Scripts["s"].Regions.ContainsKey("r"));
    }
}
