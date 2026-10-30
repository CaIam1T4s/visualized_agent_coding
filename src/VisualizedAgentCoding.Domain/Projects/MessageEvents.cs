namespace VisualizedAgentCoding.Domain.Projects;

/// <summary>Message identity inside the existing versioned event-region metadata.</summary>
public static class MessageEvents
{
    public const string Prefix = "message:";
    public const int MaxNameLength = 248;
    public static string? Name(string? identity) => identity?.StartsWith(Prefix, StringComparison.Ordinal) == true
        ? identity[Prefix.Length..] : null;
    public static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name != name.Trim() || name.Length > MaxNameLength || name.Any(char.IsControl))
            throw new ArgumentException($"消息名称须为 1–{MaxNameLength} 个字符，不能包含控制字符或首尾空白。");
        return name;
    }
    public static string Title(string? identity) => Name(identity) is { } name
        ? "当收到消息：" + name : "事件：" + identity;
}
