using System;
using System.Collections.Generic;

namespace VisualizedAgentCoding.Domain.Projects;

/// <summary>Stable backdrop identity inside the versioned event-region metadata.</summary>
public static class BackdropSwitchEvents
{
    public const string Prefix = "backdrop-switch:";

    public static string? Target(string? identity) =>
        identity is not null && identity.StartsWith(Prefix, StringComparison.Ordinal)
            ? identity[Prefix.Length..] : null;

    public static bool IsValidTarget(string? target) =>
        !string.IsNullOrWhiteSpace(target) && target == target.Trim()
        && target.Length <= 128 && !target.Any(char.IsControl);

    public static string ValidateTarget(string target) =>
        IsValidTarget(target) ? target
            : throw new ArgumentException("背景切换事件的目标背景无效。");

    public static string Identity(string target) => Prefix + ValidateTarget(target);

    public static string Title(string? identity, IReadOnlyDictionary<string, string>? backdropNames = null)
    {
        var target = Target(identity);
        if (target is null) return "背景切换";
        return backdropNames is not null && backdropNames.TryGetValue(target, out var name)
            ? "当背景切换到 " + name : "当背景切换";
    }
}
