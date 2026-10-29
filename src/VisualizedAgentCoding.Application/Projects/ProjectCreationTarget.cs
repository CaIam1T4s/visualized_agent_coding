using VisualizedAgentCoding.Domain.Projects;

namespace VisualizedAgentCoding.Application.Projects;

/// <summary>A named child directory, never the user-selected parent itself.</summary>
public sealed record ProjectCreationTarget(string RootPath, string Name)
{
    public static ProjectCreationTarget Resolve(string parentDirectory, string name)
    {
        var normalized = name.Trim();
        if (string.IsNullOrWhiteSpace(normalized)) throw new ProjectFormatException("请输入项目名称。");
        if (normalized.Length > 255 || normalized is "." or ".." || normalized.EndsWith('.')
            || normalized.Any(c => c < 32 || "<>:\"/\\|?*".Contains(c))
            || normalized.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ProjectFormatException("项目名称必须是有效的文件夹名称，不能包含路径或文件名非法字符，也不能以句点结尾。");
        var stem = normalized.Split('.')[0].TrimEnd().ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$"
            || stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && "123456789¹²³".Contains(stem[3]))
            throw new ProjectFormatException("该项目名称是系统保留名称，请使用其他名称。");
        if (string.IsNullOrWhiteSpace(parentDirectory) || !Directory.Exists(parentDirectory))
            throw new ProjectFormatException("所选的项目父目录不存在，请重新选择。");
        var parent = Path.GetFullPath(parentDirectory);
        var root = Path.Combine(parent, normalized);
        if (Directory.Exists(root) || File.Exists(root))
            throw new ProjectFormatException("所选目录下已存在同名文件或文件夹，请使用其他项目名称。");
        return new(root, normalized);
    }
}
