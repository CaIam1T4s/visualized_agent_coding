using System.Collections.Generic;

namespace VisualizedAgentCoding.Domain.Projects;

/// <summary>The visual event kinds a project script can handle.</summary>
public static class ProjectEventKinds
{
    public const string Start = "start";
    public const string Key = "key";
    public const string SpriteClick = "sprite-click";
    public const string StageClick = "stage-click";
    public const string BackdropSwitch = "backdrop-switch";
    public const string Timer = "timer";
    public const string Broadcast = "broadcast";
    public const string CloneStart = "clone-start";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(System.StringComparer.Ordinal)
    {
        Start,
        Key,
        SpriteClick,
        StageClick,
        BackdropSwitch,
        Timer,
        Broadcast,
        CloneStart,
    };

    public static bool IsKnown(string? value) => value is not null && All.Contains(value);

    /// <summary>True when the event cannot be matched without an argument.</summary>
    public static bool RequiresArgument(string? value) =>
        value is Key or BackdropSwitch or Timer or Broadcast;
}
