using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Domain.Projects;

namespace VisualizedAgentCoding.Application.Editor.ObjectScripts;

/// <summary>Pure v2 merge planning. Reuses ObjectScriptSaveService.Apply for conflict checks and persistence.</summary>
public static class ObjectScriptRegionSaveService
{
    public static ObjectScriptSavePlan Prepare(ProjectWorkspace project, ObjectScriptDocument document,
        long version, IReadOnlyDictionary<string, string> replacements)
    {
        var script = project.FindScript(document.ScriptId) ?? throw new ProjectOperationException("脚本未被项目索引。");
        if (script.Kind == ProjectScriptKind.User || document.TemplateVersion != 2 || !document.HasRegions
            || document.Uri != new Uri(project.ToAbsolutePath(script.Path)).AbsoluteUri)
            throw new ProjectOperationException("缺少当前脚本已确认的 v2 多区域文档。");
        if (version < document.Version) throw new ProjectOperationException("编辑版本已过期。");
        var regions = document.Regions!;
        try { ObjectScriptRegionDefinition.Validate(regions.Select(r => new ObjectScriptRegionDefinition(r.Id, r.Kind, r.EventName)).ToArray()); }
        catch (ArgumentException exception) { throw new ProjectOperationException("多区域身份或类型无效：" + exception.Message); }
        if (replacements.Keys.Any(id => !regions.Any(r => r.Id == id)))
            throw new ProjectOperationException("不能通过内容替换添加未知区域。");
        var source = document.FullSource;
        var updated = new ObjectScriptRegion[regions.Length];
        var output = new System.Text.StringBuilder();
        var previousEnd = 0;
        var previousStart = -1;
        for (var index = 0; index < regions.Length; index++)
        {
            var region = regions[index];
            var range = region.Span;
            if (range.Offset < previousEnd || range.Offset <= previousStart || range.Length < 0 || range.Offset > source.Length
                || range.Length > source.Length - range.Offset)
                throw new ProjectOperationException("多区域范围无效。");
            var body = replacements.TryGetValue(region.Id, out var replacement)
                ? replacement : source.Substring(range.Offset, range.Length);
            if (body is null || (body.Length > 0 && !body.EndsWith('\n')))
                throw new ProjectOperationException("区域内容须为空或以换行结束，保留标记独占行。");
            output.Append(source, previousEnd, range.Offset - previousEnd);
            updated[index] = region with { Span = new(output.Length, body.Length) };
            output.Append(body);
            previousEnd = range.Offset + range.Length;
            previousStart = range.Offset;
        }
        output.Append(source, previousEnd, source.Length - previousEnd);
        var merged = output.ToString();
        return new(project, script.Id, script.Path, version, source, merged,
            document with { Version = version, FullSource = merged, Regions = updated });
    }
}
