using System.Text.RegularExpressions;
using VisualizedAgentCoding.Application.Ports;
using VisualizedAgentCoding.Domain.Projects;

namespace VisualizedAgentCoding.Application.Projects;

/// <summary>Shared file/index checks; Python semantic checks remain in the helper.</summary>
internal static class ScriptRenameRules
{
    internal sealed record FileTarget(ProjectScriptEntry Script, string Path);

    public static string NormalizeModuleName(string name)
    {
        name = name.Trim();
        if (!Regex.IsMatch(name, @"^[\p{L}_][\p{L}\p{N}_]*$"))
            throw new ProjectOperationException("新名称必须是合法的 Python 模块名。");
        return name;
    }

    public static FileTarget ResolveFileTarget(ProjectWorkspace project, string scriptId, string name, IProjectStore store)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (scriptId == project.Manifest.MainScriptId)
            throw new ProjectOperationException("不能重命名主脚本。");
        var script = project.FindScript(scriptId) ?? throw new ProjectOperationException("脚本未被项目索引。");
        var stem = ProjectService.SanitizeScriptName(name)
            ?? throw new ProjectOperationException("脚本名只能包含字母、数字、下划线和连字符。");
        var path = $"{ProjectLayout.DirectoryFor(script.Kind)}/{stem}.py";
        if (!string.Equals(path, script.Path, StringComparison.OrdinalIgnoreCase)
            && (store.ScriptExists(project.RootPath, path)
                || project.Manifest.Scripts.Any(entry => string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase))))
            throw new ProjectOperationException($"脚本已存在：{path}");
        return new(script, path);
    }
}
