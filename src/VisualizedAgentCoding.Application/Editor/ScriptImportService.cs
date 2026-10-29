using VisualizedAgentCoding.Application.Ports;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Application.Python;
using VisualizedAgentCoding.Application.Editor.ObjectScripts;
using VisualizedAgentCoding.Domain.Projects;

namespace VisualizedAgentCoding.Application.Editor;

public sealed class ScriptImportService(IPythonBridge bridge)
{
    public static ScriptImportContext Context(ProjectWorkspace project, ProjectScriptEntry script,
        ObjectScriptDocument? document = null) => new(script.Id, script.Kind.ToString().ToLowerInvariant(),
            project.Manifest.Scripts.Select(s => new ImportScriptTarget(s.Id,
                Path.ChangeExtension(s.Path, null)!.Replace('\\', '.').Replace('/', '.'),
                s.Kind.ToString().ToLowerInvariant(), Path.GetFileNameWithoutExtension(s.Path))).ToArray(),
            document?.Regions?.Select(r => r.Span).ToArray() ?? []);

    public async Task<ScriptImportsResult> InspectAsync(ProjectWorkspace project, ProjectScriptEntry script, long version,
        string source, ObjectScriptDocument? document = null, ScriptImport[]? imports = null, int? convertLine = null,
        CancellationToken token = default)
    {
        if ((imports is not null || convertLine is not null) && script.Kind != ProjectScriptKind.User && document?.TemplateVersion != 3)
            throw new ProjectOperationException("请先通过“更多 → 升级脚本模板…”升级到支持导入的模板。");
        var uri = new Uri(project.ToAbsolutePath(script.Path)).AbsoluteUri;
        var result = await bridge.InspectImportsAsync(new(uri, version, source, Context(project, script, document), imports, convertLine), token);
        if (result.Uri != uri || result.Version != version || result.StandardModules is null || result.Diagnostics is null
            || result.InsertOffset < 0 || result.InsertOffset > result.Source.Length)
            throw new PythonBridgeException("导入分析返回了无效的文档或目录。");
        if (result.Block is { } block && (block.Span.Offset < 0 || block.Span.Length < 0
            || block.Span.Offset + block.Span.Length > result.Source.Length || block.Entries is null))
            throw new PythonBridgeException("导入区范围无效。");
        if (result.Problem is not null && (imports is not null || convertLine is not null))
            throw new ProjectOperationException(result.Problem);
        return result;
    }
}
