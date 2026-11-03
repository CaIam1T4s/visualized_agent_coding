namespace VisualizedAgentCoding.Domain.Blueprint;

/// <summary>View-only layout for blueprint nodes. It never participates in Python semantics.</summary>
public sealed class BlueprintLayout
{
    public int Version { get; init; } = 1;
    public Dictionary<string, BlueprintScriptLayout> Scripts { get; init; } = [];
}

public sealed class BlueprintScriptLayout
{
    public Dictionary<string, BlueprintRegionLayout> Regions { get; init; } = [];
}

public sealed class BlueprintRegionLayout
{
    public Dictionary<string, BlueprintNodeLayout> Nodes { get; init; } = [];
}

public sealed record BlueprintNodeLayout(
    double X,
    double Y,
    bool Collapsed = false,
    string Fingerprint = "");
