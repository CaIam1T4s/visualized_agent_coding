using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Domain.Projects;

namespace VisualizedAgentCoding.Application.Editor.ObjectScripts;

/// <summary>Explicit v1 migration. Preserves the complete user body; never infers startup statements.</summary>
public sealed class ObjectScriptUpgradeService(ObjectScriptDocumentService documents)
{
    public async Task<ObjectScriptDocument> PrepareAsync(ProjectWorkspace project, ObjectScriptDocument original,
        long version, CancellationToken token = default)
    {
        var script = project.FindScript(original.ScriptId) ?? throw new ProjectOperationException("脚本已不在项目中。");
        if (script.Kind == ProjectScriptKind.User || original.TemplateVersion != 1 || !original.HasUserRegion
            || original.Uri != new Uri(project.ToAbsolutePath(script.Path)).AbsoluteUri)
            throw new ProjectOperationException("只能升级区域结构已确认的 v1 角色或背景脚本。");
        // Confirm even a trusted editor snapshot before changing its wrapper.
        var confirmed = await documents.InspectAsync(project, script.Id, version, original.FullSource, token);
        if (!confirmed.HasUserRegion || confirmed.TemplateVersion != 1 || confirmed.UserRegion != original.UserRegion)
            throw new ProjectOperationException("旧脚本区域结构无法确认，请修复后再升级。");
        var newline = original.FullSource.Contains("\r\n") ? "\r\n" : "\n";
        var source = ProjectScriptTemplates.CreateMultiRegion(script.Kind,
            ProjectService.ToClassName(Path.GetFileNameWithoutExtension(script.Path))).Replace("\n", newline);
        var empty = await documents.InspectAsync(project, script.Id, version, source, token);
        var members = empty.Regions!.Single(r => r.Kind == "members").Span;
        source = source[..members.Offset] + original.UserSource + source[(members.Offset + members.Length)..];
        var migrated = await documents.InspectAsync(project, script.Id, version, source, token);
        if (!migrated.HasRegions || migrated.TemplateVersion != 2)
            throw new ProjectOperationException("升级后的区域结构无法确认，原脚本未修改。");
        return migrated;
    }
}
