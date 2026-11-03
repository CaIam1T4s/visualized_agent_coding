using System;
using System.Collections.Generic;
using System.Linq;
using VisualizedAgentCoding.Domain.Blueprint;

namespace VisualizedAgentCoding.Blueprint.Core;

/// <summary>Maps between a projected document and the disposable view layout file.</summary>
public static class BlueprintLayoutMapper
{
    public static BlueprintLayout Capture(BlueprintDocument document, BlueprintLayout? existing = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var layout = existing ?? new BlueprintLayout();
        if (!layout.Scripts.TryGetValue(document.ScriptId, out var script))
        {
            script = new BlueprintScriptLayout();
            layout.Scripts[document.ScriptId] = script;
        }
        var region = new BlueprintRegionLayout();
        foreach (var node in Walk(document.Roots))
        {
            region.Nodes[node.Id] = new BlueprintNodeLayout(node.X, node.Y, node.IsCollapsed, Fingerprint(node));
        }
        script.Regions[document.RegionId] = region;
        return layout;
    }

    public static void Apply(BlueprintLayout? layout, BlueprintDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (layout is null) return;
        if (layout.Version != 1) return;
        if (!layout.Scripts.TryGetValue(document.ScriptId, out var script)) return;
        if (!script.Regions.TryGetValue(document.RegionId, out var region)) return;
        var byFingerprint = region.Nodes.Values
            .GroupBy(item => item.Fingerprint)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single());
        foreach (var node in Walk(document.Roots))
        {
            if (region.Nodes.TryGetValue(node.Id, out var item)
                || byFingerprint.TryGetValue(Fingerprint(node), out item))
            {
                node.X = item.X;
                node.Y = item.Y;
                node.HasPosition = true;
                node.IsCollapsed = item.Collapsed;
            }
        }
    }

    public static string Fingerprint(BlueprintNode node) =>
        node.Type + "|" + node.DisplayName + "|" + (node.Text ?? "");

    private static IEnumerable<BlueprintNode> Walk(IEnumerable<BlueprintNode> roots)
    {
        foreach (var root in roots)
        {
            yield return root;
            foreach (var child in Walk(root.AllChildren)) yield return child;
        }
    }
}
