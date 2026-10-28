using VisualizedAgentCoding.Application.Ports;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Domain.Projects;

namespace VisualizedAgentCoding.Application.Editor.ObjectScripts;

/// <summary>Optimistic save: preserve the system shell and reject changed disk sources before writing.</summary>
public sealed class ObjectScriptSaveService(ObjectScriptDocumentService documents, IProjectStore store)
{
    public async Task<ObjectScriptSavePlan> PrepareFullBufferAsync(ProjectWorkspace project, string scriptId,
        long version, string originalSource, string editedSource, ObjectScriptDocument? trustedDocument = null,
        CancellationToken token = default)
    {
        var script = project.FindScript(scriptId) ?? throw new ProjectOperationException("脚本未被项目索引。");
        if (script.Kind == ProjectScriptKind.User) throw new ProjectOperationException("普通用户脚本不使用对象脚本合并。");
        var document = trustedDocument;
        if (document is null || document.ScriptId != scriptId || document.FullSource != originalSource
            || document.Uri != new Uri(project.ToAbsolutePath(script.Path)).AbsoluteUri)
            document = await documents.InspectAsync(project, scriptId, version, originalSource, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (document.Status == ObjectScriptStatus.LegacyUnmarked)
            return new(project, scriptId, script.Path, version, originalSource, editedSource, null);
        if (document.Status == ObjectScriptStatus.InvalidStructure)
        {
            // Reopened files may contain a saved unterminated string. Accept an
            // explicit full-buffer repair only after the new wrapper is verified;
            // never rewrite or infer missing source on the user's behalf.
            var repaired = await documents.InspectAsync(project, scriptId, version, editedSource, token).ConfigureAwait(false);
            if (repaired.HasUserRegion)
                return new(project, scriptId, script.Path, version, originalSource, editedSource, repaired);
        }
        if (!document.HasUserRegion)
            throw new ProjectOperationException("对象脚本结构无法确认，未保存。请保留编辑内容并修复模板：" + string.Join("；", document.Problems));
        var region = document.UserRegion!;
        var prefix = originalSource[..region.Offset];
        var suffix = originalSource[(region.Offset + region.Length)..];
        if (editedSource.Length < prefix.Length + suffix.Length
            || !editedSource.StartsWith(prefix, StringComparison.Ordinal) || !editedSource.EndsWith(suffix, StringComparison.Ordinal))
            throw new ProjectOperationException("系统区域或标记已被修改，未保存。只能修改两个用户代码标记之间的内容。");
        var userSource = editedSource.Substring(prefix.Length, editedSource.Length - prefix.Length - suffix.Length);
        return Merge(project, scriptId, script.Path, version, document, userSource);
    }

    /// <summary>For a future projected user-code editor. Does not infer boundaries from edited Python.</summary>
    public ObjectScriptSavePlan PrepareUserRegion(ProjectWorkspace project, ObjectScriptDocument document,
        long version, string userSource)
    {
        var script = project.FindScript(document.ScriptId) ?? throw new ProjectOperationException("脚本未被项目索引。");
        if (script.Kind == ProjectScriptKind.User || !document.HasUserRegion
            || document.Uri != new Uri(project.ToAbsolutePath(script.Path)).AbsoluteUri)
            throw new ProjectOperationException("文档不属于当前对象脚本或没有已确认区域。");
        return Merge(project, script.Id, script.Path, version, document, userSource);
    }

    private static ObjectScriptSavePlan Merge(ProjectWorkspace project, string scriptId, string path, long version,
        ObjectScriptDocument document, string userSource)
    {
        if (version < document.Version) throw new ProjectOperationException("编辑版本已过期，未生成保存计划。");
        if (userSource.Length > 0 && !userSource.EndsWith('\n'))
            throw new ProjectOperationException("结束标记必须保留在独立的一行，未保存。");
        var region = document.UserRegion!;
        var merged = document.FullSource[..region.Offset] + userSource + document.FullSource[(region.Offset + region.Length)..];
        var updated = document with { Version = version, FullSource = merged, UserRegion = new(region.Offset, userSource.Length) };
        return new(project, scriptId, path, version, document.FullSource, merged, updated);
    }

    public void Apply(ObjectScriptSavePlan plan, long currentVersion)
    {
        if (plan.Version != currentVersion) throw new ProjectOperationException("编辑内容已变化，保存计划已过期。");
        var indexed = store.ReadManifest(plan.Project.RootPath).Scripts.FirstOrDefault(s => s.Id == plan.ScriptId);
        var original = plan.Project.FindScript(plan.ScriptId);
        if (indexed is null || original is null || indexed != original || indexed.Path != plan.Path)
            throw new ProjectOperationException("脚本索引或路径已被外部修改，未保存。");
        if (store.ReadScript(plan.Project.RootPath, plan.Path) != plan.OriginalSource)
            throw new ProjectOperationException("磁盘脚本已被外部修改，未覆盖。当前编辑内容已保留。");
        if (plan.MergedSource != plan.OriginalSource)
            store.WriteScript(plan.Project.RootPath, plan.Path, plan.MergedSource);
    }
}
