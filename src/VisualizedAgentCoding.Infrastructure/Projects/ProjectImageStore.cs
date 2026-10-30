using VisualizedAgentCoding.Application.Ports;
using VisualizedAgentCoding.Domain.Projects;

namespace VisualizedAgentCoding.Infrastructure.Projects;

public sealed class ProjectImageStore : IProjectImageStore
{
    public string? CleanUnused(string root, IReadOnlyCollection<string> retainedPaths, string? sceneRevision, string? sourceOverlay = null)
    {
        var directory = Path.GetFullPath(Path.Combine(root, "assets", "images"));
        if (!Directory.Exists(directory)) return null;
        try
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0
                || (File.GetAttributes(Path.Combine(root, "assets")) & FileAttributes.ReparsePoint) != 0)
                return "图片目录为链接，未自动清理旧图片。";
            var scene = Path.Combine(root, "scenes", "stage.json");
            if ((File.Exists(scene) ? File.ReadAllText(scene) : null) != sceneRevision) return "舞台文件已变化，未清理旧图片。";
            // Keep shared images and explicit references from all project scripts/JSON files.
            var references = TextFiles(root).Select(File.ReadAllText).Append(sourceOverlay ?? "").ToArray();
            var retained = retainedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var failed = false;
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                var absolute = Path.GetFullPath(file);
                if (!string.Equals(Path.GetDirectoryName(absolute), directory, StringComparison.OrdinalIgnoreCase)) continue;
                var name = Path.GetFileName(file);
                if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(name), "N", out _)
                    || Path.GetExtension(file).ToLowerInvariant() is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp")
                    || (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0
                    || retained.Contains("assets/images/" + name)
                    || references.Any(text => text.Contains(name, StringComparison.OrdinalIgnoreCase))) continue;
                try { File.Delete(absolute); }
                catch (IOException) { failed = true; }
                catch (UnauthorizedAccessException) { failed = true; }
            }
            return failed ? "布局已保存，但部分旧图片无法清理；下次保存时重试。" : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return "无法完整检查资源引用，未继续清理旧图片；下次保存时重试。"; }
    }

    private static IEnumerable<string> TextFiles(string root)
    {
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            foreach (var file in Directory.EnumerateFiles(directory))
                if (Path.GetExtension(file).ToLowerInvariant() is ".json" or ".py") yield return file;
            foreach (var child in Directory.EnumerateDirectories(directory))
                if (Path.GetFileName(child) is not (".git" or "bin" or "obj" or "artifacts")
                    && (File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child);
        }
    }
    public string Import(string root, string sourcePath, byte[]? contents = null)
    {
        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp"))
            throw new ProjectOperationException("请选择 PNG、JPEG、BMP 或 WebP 图片。");
        if ((contents?.LongLength ?? new FileInfo(sourcePath).Length) > 20 * 1024 * 1024) throw new ProjectOperationException("图片不能超过 20 MB。");
        var directory = Path.Combine(root, "assets", "images");
        foreach (var folder in new[] { Path.Combine(root, "assets"), directory })
            if (Directory.Exists(folder) && (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
                throw new ProjectOperationException("资源目录为链接，无法安全导入图片。");
        Directory.CreateDirectory(directory);
        var relative = "assets/images/" + Guid.NewGuid().ToString("N") + extension;
        var destination = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        if (contents is null) File.Copy(sourcePath, destination);
        else { using var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write); stream.Write(contents); }
        return relative;
    }
}
