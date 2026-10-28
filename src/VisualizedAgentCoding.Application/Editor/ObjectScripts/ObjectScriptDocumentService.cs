using VisualizedAgentCoding.Application.Ports;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Application.Python;
using VisualizedAgentCoding.Domain.Projects;

namespace VisualizedAgentCoding.Application.Editor.ObjectScripts;

/// <summary>Read-only inspection. Does not migrate templates, save sources or modify editor state.</summary>
public sealed class ObjectScriptDocumentService(IPythonBridge bridge)
{
    public async Task<ObjectScriptDocument> InspectAsync(ProjectWorkspace project, string scriptId, long version,
        string fullSource, CancellationToken token = default)
    {
        var script = project.FindScript(scriptId) ?? throw new ProjectOperationException("脚本未被项目索引。");
        var kind = script.Kind switch
        {
            ProjectScriptKind.Object => "object",
            ProjectScriptKind.Backdrop => "backdrop",
            _ => throw new ProjectOperationException("仅角色和背景脚本使用对象脚本文档模型。"),
        };
        ArgumentNullException.ThrowIfNull(fullSource);
        if (version < 0) throw new ArgumentOutOfRangeException(nameof(version));
        token.ThrowIfCancellationRequested();
        var uri = new Uri(project.ToAbsolutePath(script.Path)).AbsoluteUri;
        var expectedClass = ProjectService.ToClassName(Path.GetFileNameWithoutExtension(script.Path));
        var result = await bridge.InspectObjectScriptAsync(new(uri, version, fullSource, expectedClass, kind), token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        ValidateResponse(uri, version, fullSource, result);
        var status = result.Status switch
        {
            "recognized" => ObjectScriptStatus.Recognized,
            "legacyUnmarked" => ObjectScriptStatus.LegacyUnmarked,
            "invalidStructure" => ObjectScriptStatus.InvalidStructure,
            "unsupportedVersion" => ObjectScriptStatus.UnsupportedVersion,
            _ => throw new PythonBridgeException("未知对象脚本识别状态。"),
        };
        return new(script.Id, uri, version, fullSource, status, result.TemplateVersion, result.UserRegion, result.Problems);
    }

    private static void ValidateResponse(string uri, long version, string source, ObjectScriptInspectionResult result)
    {
        if (result.Uri != uri || result.Version != version)
            throw new PythonBridgeException("对象脚本识别返回了不匹配的文档版本。");
        if (result.Problems is null)
            throw new PythonBridgeException("对象脚本识别缺少问题列表。");
        if (result.Status == "recognized" && (result.UserRegion is null || result.TemplateVersion != 1 || result.Problems.Length != 0))
            throw new PythonBridgeException("对象脚本识别返回了不一致的结构状态。");
        if (result.Status != "recognized" && result.UserRegion is not null)
            throw new PythonBridgeException("未确认结构的文档不能提供可编辑区域。");
        if (result.UserRegion is { } span && (span.Offset < 0 || span.Length < 0 || span.Offset > source.Length
            || span.Length > source.Length - span.Offset || SplitsSurrogate(source, span.Offset)
            || SplitsSurrogate(source, span.Offset + span.Length)))
            throw new PythonBridgeException("对象脚本识别返回了无效的 UTF-16 区间。");
    }

    private static bool SplitsSurrogate(string source, int offset) => offset > 0 && offset < source.Length
        && char.IsHighSurrogate(source[offset - 1]) && char.IsLowSurrogate(source[offset]);
}
