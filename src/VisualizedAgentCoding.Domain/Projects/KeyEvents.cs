namespace VisualizedAgentCoding.Domain.Projects;

/// <summary>Keyboard event identity inside the versioned event-region metadata.</summary>
public static class KeyEvents
{
    public const string Prefix = "key:";
    public const int MaxNameLength = 32;
    public const string AnyKey = "any";

    /// <summary>Lower-case key names that are not a single letter or digit.</summary>
    public static readonly IReadOnlySet<string> SpecialKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        AnyKey, "space", "enter", "tab", "escape", "backspace", "delete", "insert",
        "left", "right", "up", "down", "home", "end", "pageup", "pagedown",
    };

    public static string? Name(string? identity) => identity?.StartsWith(Prefix, StringComparison.Ordinal) == true
        ? identity[Prefix.Length..] : null;

    public static bool IsValidName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength) return false;
        if (SpecialKeys.Contains(name)) return true;
        return name.Length == 1 && name[0] is (>= 'a' and <= 'z') or (>= '0' and <= '9');
    }

    public static string ValidateName(string name)
    {
        if (!IsValidName(name))
            throw new ArgumentException("按键名称须为单个小写字母、数字，或 space/enter/方向键等预设名称。");
        return name;
    }

    public static string Display(string name) => SpecialKeys.Contains(name) ? name : name.ToUpperInvariant();

    public static string Title(string name) => "当按下按键：" + Display(name);
}
