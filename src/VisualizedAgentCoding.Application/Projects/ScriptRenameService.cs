using System.Text.Json;
using VisualizedAgentCoding.Application.Ports;
using VisualizedAgentCoding.Application.Python;
using VisualizedAgentCoding.Domain.Projects;
using VisualizedAgentCoding.Application.Editor;
using VisualizedAgentCoding.Application.Editor.ObjectScripts;

namespace VisualizedAgentCoding.Application.Projects;

/// <summary>Plans without saving; applies only against unchanged sources, restoring originals on an I/O failure.</summary>
public sealed class ScriptRenameService(IPythonBridge bridge, IProjectStore store)
{
    private readonly ProjectSourceService sources = new(store);
    public async Task<ScriptRenamePlan> PlanAsync(ProjectWorkspace project, string scriptId, string newName, long version,
        IReadOnlyList<PythonSourceDocument> overlays, CancellationToken token = default)
    {
        newName = ScriptRenameRules.NormalizeModuleName(newName);
        var target = ScriptRenameRules.ResolveFileTarget(project, scriptId, newName, store);
        var script = target.Script;
        var newPath = target.Path;
        if (string.Equals(script.Path, newPath, StringComparison.OrdinalIgnoreCase))
            throw new ProjectOperationException("请输入不同的新名称。");
        var manifest = JsonSerializer.Serialize(store.ReadManifest(project.RootPath));
        if (manifest != JsonSerializer.Serialize(project.Manifest)) throw new ProjectOperationException("项目清单已变化，请重新打开项目。");
        var documents = sources.Collect(project, overlays, token);
        if (documents.Any(d => Path.GetFileNameWithoutExtension(new Uri(d.Uri).LocalPath).Equals(newName, StringComparison.OrdinalIgnoreCase)))
            throw new ProjectOperationException("项目中已存在同名模块，无法安全重命名。");
        var originals = documents.ToDictionary(d => d.Uri, d => store.ReadScript(project.RootPath,
            Path.GetRelativePath(project.RootPath, new Uri(d.Uri).LocalPath)), StringComparer.OrdinalIgnoreCase);
        var overlayUris = overlays.Select(d => d.Uri).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (documents.Any(d => !overlayUris.Contains(d.Uri) && d.Source != originals[d.Uri]))
            throw new ProjectOperationException("收集源码期间文件已变化，请重新生成预览。");
        // A rename may persist editor overlays too. Apply the same object-shell
        // checks without saving so this path cannot bypass ordinary region saves.
        var objectSaves = new ObjectScriptSaveService(new ObjectScriptDocumentService(bridge), store);
        foreach (var overlay in overlays)
        {
            var entry = project.Manifest.Scripts.FirstOrDefault(s => s.Kind != ProjectScriptKind.User
                && string.Equals(new Uri(project.ToAbsolutePath(s.Path)).AbsoluteUri, overlay.Uri, StringComparison.OrdinalIgnoreCase));
            if (entry is not null && originals[overlay.Uri] != overlay.Source)
                await objectSaves.PrepareFullBufferAsync(project, entry.Id, overlay.Version, originals[overlay.Uri], overlay.Source,
                    token: token).ConfigureAwait(false);
        }
        var uri = new Uri(project.ToAbsolutePath(script.Path)).AbsoluteUri;
        var oldClassName = script.Kind == ProjectScriptKind.User ? null
            : ProjectService.ToClassName(Path.GetFileNameWithoutExtension(script.Path));
        var newClassName = script.Kind == ProjectScriptKind.User ? null
            : ProjectService.ToClassName(Path.GetFileNameWithoutExtension(newPath));
        var result = await bridge.PlanModuleRenameAsync(new(project.RootPath, uri, version, newName, documents,
            oldClassName, newClassName), token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (result.Uri != uri || result.Version != version) throw new ProjectOperationException("重命名分析版本不匹配。");
        if (result.IsIncomplete || result.Issues.Length > 0) throw new ProjectOperationException("无法安全生成重命名计划：\n" +
            string.Join("\n", result.Issues.Select(issue => $"{Path.GetFileName(new Uri(issue.Uri).LocalPath)}: {issue.Message}")));
        var updates = result.Changes.ToDictionary(change => change.Uri, StringComparer.OrdinalIgnoreCase);
        if (updates.Keys.Any(key => !originals.ContainsKey(key))) throw new ProjectOperationException("分析返回了项目外的修改。");
        var planned = documents.Select(d =>
        {
            var path = Path.GetRelativePath(project.RootPath, new Uri(d.Uri).LocalPath);
            var normalizedPath = path.Replace('\\', '/');
            var after = updates.TryGetValue(d.Uri, out var change) ? change.Source : d.Source;
            var entry = project.Manifest.Scripts.FirstOrDefault(s => string.Equals(
                s.Path.Replace('\\', '/'), normalizedPath, StringComparison.OrdinalIgnoreCase));
            var isTarget = string.Equals(normalizedPath, script.Path.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase);
            var nextPath = isTarget ? newPath : path;
            var oldClass = entry is { Kind: not ProjectScriptKind.User }
                ? ProjectService.ToClassName(Path.GetFileNameWithoutExtension(path)) : null;
            var newClass = oldClass is null ? null
                : ProjectService.ToClassName(Path.GetFileNameWithoutExtension(nextPath));
            return new ScriptRenameDocument(path, originals[d.Uri], d.Source, after,
                Kind: entry?.Kind, OldClassName: oldClass, NewClassName: newClass, NewPath: nextPath, IsTarget: isTarget);
        }).ToList();
        await PopulateVisiblePreviewsAsync(project, planned, token).ConfigureAwait(false);
        return new(project, scriptId, newName, newPath, version, manifest, planned.ToArray());
    }

    /// <summary>Build editor-facing previews without exposing template wrappers or region markers.</summary>
    private async Task PopulateVisiblePreviewsAsync(ProjectWorkspace project, List<ScriptRenameDocument> documents,
        CancellationToken token)
    {
        var imports = new ScriptImportService(bridge);
        var objectScripts = new ObjectScriptDocumentService(bridge);
        for (var index = 0; index < documents.Count; index++)
        {
            var document = documents[index];
            if (document.Before == document.After && string.Equals(document.Path, document.NewPath, StringComparison.Ordinal)) continue;
            var script = project.Manifest.Scripts.FirstOrDefault(s =>
                string.Equals(s.Path.Replace('\\', '/'), document.Path.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
            if (script is null)
            {
                documents[index] = document with { BeforeVisible = document.Before, AfterVisible = document.After };
                continue;
            }
            try
            {
                if (script.Kind == ProjectScriptKind.User)
                {
                    var uri = new Uri(project.ToAbsolutePath(document.Path)).AbsoluteUri;
                    var before = await imports.InspectAsync(project, script, 0, document.Before, token: token).ConfigureAwait(false);
                    var after = await imports.InspectAsync(project, script, 0, document.After, token: token).ConfigureAwait(false);
                    documents[index] = document with
                    {
                        BeforeVisible = new UserScriptImportProjection(new(script.Id, uri, document.Before, before.Block)).ViewSource,
                        AfterVisible = new UserScriptImportProjection(new(script.Id, uri, document.After, after.Block)).ViewSource,
                    };
                }
                else
                {
                    var before = await objectScripts.InspectAsync(project, script.Id, 0, document.Before, token: token).ConfigureAwait(false);
                    var after = await objectScripts.InspectAsync(project, script.Id, 0, document.After, token: token,
                        expectedClassName: document.NewClassName).ConfigureAwait(false);
                    documents[index] = document with
                    {
                        BeforeVisible = EditorProjection.Create(before).ViewSource,
                        AfterVisible = EditorProjection.Create(after).ViewSource,
                    };
                }
            }
            catch (Exception) when (!token.IsCancellationRequested)
            {
                // Keep the rename plan usable if a read-only preview projection cannot be built.
                documents[index] = document with { BeforeVisible = document.Before, AfterVisible = document.After };
            }
        }
    }

    public ProjectWorkspace Apply(ScriptRenamePlan plan)
    {
        var project = plan.Project;
        var root = project.RootPath;
        if (JsonSerializer.Serialize(store.ReadManifest(root)) != plan.ManifestSnapshot || store.ScriptExists(root, plan.NewPath))
            throw new ProjectOperationException("项目清单或目标路径已变化，请重新生成预览。");
        var currentPaths = sources.Collect(project, [], CancellationToken.None)
            .Select(d => Path.GetRelativePath(root, new Uri(d.Uri).LocalPath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!currentPaths.SetEquals(plan.Documents.Select(d => d.Path)))
            throw new ProjectOperationException("项目脚本集合已变化，请重新生成预览。");
        foreach (var document in plan.Documents)
            if (store.ReadScript(root, document.Path) != document.DiskSource)
                throw new ProjectOperationException($"{document.Path} 已被外部修改，请重新生成预览。");
        var written = new List<ScriptRenameDocument>();
        try
        {
            foreach (var document in plan.Documents.Where(d => d.After != d.DiskSource))
            {
                written.Add(document);
                store.WriteScript(root, document.Path, document.After);
            }
            return new ProjectService(store).CommitScriptFileRename(project, plan.ScriptId, plan.NewName,
                synchronizeClass: project.FindScript(plan.ScriptId)!.Kind != ProjectScriptKind.User);
        }
        catch (Exception failure)
        {
            var rollbackErrors = new List<Exception>();
            foreach (var document in written.AsEnumerable().Reverse())
                try { store.WriteScript(root, document.Path, document.DiskSource); } catch (Exception ex) { rollbackErrors.Add(ex); }
            try
            {
                if (store.ScriptExists(root, plan.NewPath))
                {
                    // Keep failed cleanup excluded from Open's external-file adoption.
                    store.WriteManifest(root, project.Manifest with
                    {
                        PendingScriptDeletions = [.. project.Manifest.PendingScriptDeletions, plan.NewPath],
                    });
                    store.DeleteScript(root, plan.NewPath);
                }
                store.WriteManifest(root, project.Manifest);
            }
            catch (Exception ex) { rollbackErrors.Add(ex); }
            if (rollbackErrors.Count > 0) throw new ProjectOperationException("重命名失败，部分恢复失败。请保留预览并检查磁盘文件：" +
                string.Join("；", rollbackErrors.Select(e => e.Message)));
            throw new ProjectOperationException("重命名失败，已恢复原文件和项目清单：" + failure.Message);
        }
    }
}
