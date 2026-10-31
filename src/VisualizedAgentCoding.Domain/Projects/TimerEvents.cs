using System;
using System.Globalization;

namespace VisualizedAgentCoding.Domain.Projects;

/// <summary>One timer binding: a one-shot threshold or a repeating interval.</summary>
public readonly record struct TimerSpec(double Seconds, bool Repeat);

/// <summary>Run-scoped timer event identity inside the versioned event-region metadata.</summary>
public static class TimerEvents
{
    public const string Prefix = "timer:";
    public const string RepeatPrefix = "every:";
    public const double MinSeconds = 0.01;
    public const double MaxSeconds = 86_400;

    public static TimerSpec? ParseIdentity(string? identity) =>
        identity is not null && identity.StartsWith(Prefix, StringComparison.Ordinal)
            ? ParseArgument(identity[Prefix.Length..])
            : null;

    public static TimerSpec? ParseArgument(string? argument)
    {
        if (argument is null) return null;
        var text = argument;
        var repeat = text.StartsWith(RepeatPrefix, StringComparison.Ordinal);
        if (repeat) text = text[RepeatPrefix.Length..];
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            || !double.IsFinite(seconds) || seconds < MinSeconds || seconds > MaxSeconds)
            return null;
        return new(seconds, repeat);
    }

    public static string FormatArgument(TimerSpec spec) =>
        (spec.Repeat ? RepeatPrefix : "") + FormatSeconds(spec.Seconds);

    public static string FormatIdentity(TimerSpec spec) => Prefix + FormatArgument(spec);

    /// <summary>Validates a user-entered number of seconds and returns its canonical argument.</summary>
    public static string ValidateArgument(string text, bool repeat)
    {
        if (text.Contains(':', StringComparison.Ordinal))
            throw new ArgumentException("请输入秒数，例如 1.5。");
        var spec = ParseArgument(text.Trim());
        if (spec is null)
            throw new ArgumentException($"计时器秒数须在 {FormatSeconds(MinSeconds)}–{FormatSeconds(MaxSeconds)} 之间。");
        return FormatArgument(spec.Value with { Repeat = repeat });
    }

    public static string Display(TimerSpec spec) =>
        spec.Repeat ? "每隔 " + FormatSeconds(spec.Seconds) + " 秒" : "当计时器到达 " + FormatSeconds(spec.Seconds) + " 秒";

    private static string FormatSeconds(double seconds) => seconds.ToString("0.###", CultureInfo.InvariantCulture);
}
