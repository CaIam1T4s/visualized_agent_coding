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
    public void CommentStaysCollapsedWhenLayoutHasNoExplicitCollapse()
    {
        var document = new BlueprintDocument { ScriptId = "s", RegionId = "r" };
        var comment = new BlueprintNode
        {
            Id = "c",
            Type = "comment",
            Kind = NodeKind.Comment,
            DisplayName = "注释",
            Text = "note",
            IsCollapsed = true,
        };
        document.Roots.Add(comment);
        var layout = new BlueprintLayout();
        var script = new BlueprintScriptLayout();
        var region = new BlueprintRegionLayout();
        region.Nodes["c"] = new BlueprintNodeLayout(10, 20, Collapsed: false);
        script.Regions["r"] = region;
        layout.Scripts["s"] = script;

        BlueprintLayoutMapper.Apply(layout, document);

        Assert.True(comment.IsCollapsed);
        Assert.Equal(10, comment.X);
        Assert.Equal(20, comment.Y);
    }


    [Fact]
    public void FingerprintWinsWhenPathIdsPointAtDifferentNodes()
    {
        var document = new BlueprintDocument { ScriptId = "s", RegionId = "r" };
        var a = new BlueprintNode { Id = "r:0", Type = "motion.move", DisplayName = "移动", Arguments = { ["steps"] = "1" } };
        var b = new BlueprintNode { Id = "r:1", Type = "motion.move", DisplayName = "移动", Arguments = { ["steps"] = "2" } };
        document.Roots.Add(a);
        document.Roots.Add(b);

        var layout = new BlueprintLayout();
        var script = new BlueprintScriptLayout();
        var region = new BlueprintRegionLayout();
        region.Nodes["r:0"] = new BlueprintNodeLayout(300, 20, Fingerprint: BlueprintLayoutMapper.Fingerprint(b));
        region.Nodes["r:1"] = new BlueprintNodeLayout(100, 10, Fingerprint: BlueprintLayoutMapper.Fingerprint(a));
        script.Regions["r"] = region;
        layout.Scripts["s"] = script;

        BlueprintLayoutMapper.Apply(layout, document);

        Assert.Equal(100, a.X);
        Assert.Equal(10, a.Y);
        Assert.Equal(300, b.X);
        Assert.Equal(20, b.Y);
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
