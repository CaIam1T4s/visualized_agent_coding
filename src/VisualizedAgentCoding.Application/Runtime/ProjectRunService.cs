using VisualizedAgentCoding.Application.Editor;
using VisualizedAgentCoding.Application.Editor.ObjectScripts;
using VisualizedAgentCoding.Application.Ports;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Application.Python;
using VisualizedAgentCoding.Domain.Projects;

namespace VisualizedAgentCoding.Application.Runtime;

/// <summary>Freeze and validate project sources before starting user execution.</summary>
public sealed class ProjectRunService(IProjectStore store, IPythonBridge bridge)
{
    public async Task<PythonRunRequest> PrepareAsync(ProjectWorkspace project, string executable, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ManagedProjectEntry.Ensure(store, project);
        var sources = await Task.Run(() => new ProjectSourceService(store).Collect(project, [], token), token);
        var imports = new ScriptImportService(bridge);
        var documents = new ObjectScriptDocumentService(bridge);
        var snapshots = new List<RuntimeSourceSnapshot>();
        var handlers = project.Manifest.EventHandlers.ToList();
        var presenter = new RuntimeErrorPresentationService();
        foreach (var source in sources)
        {
            token.ThrowIfCancellationRequested();
            var path = new Uri(source.Uri).LocalPath;
            var relative = Path.GetRelativePath(project.RootPath, path).Replace('\\', '/');
            // Product-owned entry imports a private product API, not user-managed imports.
            // Its entire content is checked instead of applying user-script import rules.
            if (relative == project.Manifest.MainScript)
            {
                if (!ManagedProjectEntry.IsCurrent(source.Source))
                    throw new ProjectOperationException("系统入口在运行准备期间已变化，请重新运行。");
                continue;
            }
            var script = project.Manifest.Scripts.FirstOrDefault(s => s.Path == relative);
            ObjectScriptDocument? document = null;
            if (script is { Kind: not ProjectScriptKind.User })
            {
                document = await documents.InspectAsync(project, script.Id, 0, source.Source, token);
                if (document.Status != ObjectScriptStatus.Recognized)
                {
                    var unknown = new RuntimeSourceSnapshot(project.RootPath, script, source.Source, null);
                    throw new RuntimePreparationException(presenter.PresentDiagnostics(unknown,
                        [new("error", "无法确认对象脚本结构，请先修复或升级模板。", 0, 0)], "对象脚本结构检查失败", string.Join("\n", document.Problems)));
                }
                if (document.HasRegions)
                {
                    // Region source is authoritative; deleted regions cannot leave ghost bindings.
                    handlers.RemoveAll(h => h.ScriptId == script.Id && h.Event is ProjectEventKinds.Broadcast or ProjectEventKinds.SpriteClick or ProjectEventKinds.StageClick or ProjectEventKinds.BackdropSwitch or ProjectEventKinds.Key or ProjectEventKinds.Timer or ProjectEventKinds.CloneStart);
                    foreach (var region in document.Regions!)
                    {
                        if (region.Kind == "event" && MessageEvents.Name(region.EventName) is { } message)
                            handlers.Add(new(script.Id, ProjectEventKinds.Broadcast, MessageEvents.ValidateName(message), "_vac_event_" + region.Id));
                        else if (region.Kind == "event" && region.EventName == ProjectEventKinds.SpriteClick)
                        {
                            if (script.Kind != ProjectScriptKind.Object) throw new ProjectOperationException("角色点击事件只能用于角色脚本。");
                            handlers.Add(new(script.Id, ProjectEventKinds.SpriteClick, null, "_vac_event_" + region.Id));
                        }
                        else if (region.Kind == "event" && region.EventName == ProjectEventKinds.StageClick)
                        {
                            if (script.Kind != ProjectScriptKind.Backdrop) throw new ProjectOperationException("背景点击事件只能用于背景脚本。");
                            handlers.Add(new(script.Id, ProjectEventKinds.StageClick, null, "_vac_event_" + region.Id));
                        }
                        else if (region.Kind == "event" && region.EventName == ProjectEventKinds.CloneStart)
                        {
                            if (script.Kind != ProjectScriptKind.Object) throw new ProjectOperationException("克隆体启动事件只能用于角色脚本。");
                            handlers.Add(new(script.Id, ProjectEventKinds.CloneStart, null, "_vac_event_" + region.Id));
                        }
                        else if (region.Kind == "event" && BackdropSwitchEvents.Target(region.EventName) is { } backdrop)
                        {
                            handlers.Add(new(script.Id, ProjectEventKinds.BackdropSwitch,
                                BackdropSwitchEvents.ValidateTarget(backdrop), "_vac_event_" + region.Id));
                        }
                        else if (region.Kind == "event" && KeyEvents.Name(region.EventName) is { } key)
                        {
                            handlers.Add(new(script.Id, ProjectEventKinds.Key, KeyEvents.ValidateName(key), "_vac_event_" + region.Id));
                        }
                        else if (region.Kind == "event" && TimerEvents.ParseIdentity(region.EventName) is { } timer)
                        {
                            handlers.Add(new(script.Id, ProjectEventKinds.Timer, TimerEvents.FormatArgument(timer), "_vac_event_" + region.Id));
                        }
                    }
                }
            }
            var parsed = await bridge.ParseAsync(new(source.Uri, 0, source.Source), token);
            ScriptImportsResult? result = null;
            if (script is not null)
                result = await imports.InspectAsync(project, script, 0, source.Source, document, token: token);
            var projection = script is null ? null : RuntimeSourceSnapshot.CreateProjection(script, source.Uri, source.Source, document, result?.Block);
            var snapshot = script is null ? null : new RuntimeSourceSnapshot(project.RootPath, script, source.Source, projection, document, result?.Block);
            if (snapshot is not null) snapshots.Add(snapshot);
            var syntax = parsed.Diagnostics.Where(d => d.Severity == "error").ToArray();
            if (syntax.Length > 0)
            {
                if (snapshot is null) throw new ProjectOperationException("项目支持文件存在语法错误，请检查运行环境。");
                var visible = ObjectScriptDiagnostics.Present(syntax, projection);
                throw new RuntimePreparationException(presenter.PresentDiagnostics(snapshot,
                    visible.Length > 0 ? visible : [new("error", "存在语法错误，暂无法定位。", 0, 0)], "存在语法错误",
                    string.Join("\n", syntax.Select(d => $"{relative}:{d.Line}:{d.Column} {d.Message}"))));
            }
            if (result is not null && (!result.SyntaxValid || result.Problem is not null || result.Diagnostics.Any(d => d.Severity == "error")))
                throw new RuntimePreparationException(presenter.PresentDiagnostics(snapshot!,
                    ObjectScriptDiagnostics.Present(result.Diagnostics.Where(d => d.Severity == "error").DefaultIfEmpty(
                        new("error", result.Problem ?? "导入检查失败", 0, 0)), projection), "导入规则错误",
                    string.Join("\n", result.Diagnostics.Select(d => $"{relative}:{d.Line}:{d.Column} {d.Message}"))));
        }
        // Refuse changes made while analysis was in flight, including manifest edits.
        var disk = store.ReadManifest(project.RootPath);
        if (System.Text.Json.JsonSerializer.Serialize(disk) != System.Text.Json.JsonSerializer.Serialize(project.Manifest))
            throw new ProjectOperationException("运行准备期间项目清单已变化，请重新打开项目。");
        foreach (var source in sources)
            if (store.ReadScript(project.RootPath, Path.GetRelativePath(project.RootPath, new Uri(source.Uri).LocalPath)) != source.Source)
                throw new ProjectOperationException("运行准备期间磁盘源码已变化，请重新运行。");
        var data = new RuntimeProjectData(
            (project.Manifest.Variables ?? []).Select(v => new RuntimeVariableDefinition(v.Name, v.Type, v.Value)).ToArray(),
            (project.Manifest.Lists ?? []).Select(l => new RuntimeListDefinition(l.Name, l.Values.ToArray())).ToArray());
        return new(Guid.NewGuid().ToString("N"), executable, project.RootPath, project.Manifest.MainScript,
            sources.Select(s => new PythonRunSource(Path.GetRelativePath(project.RootPath, new Uri(s.Uri).LocalPath).Replace('\\', '/'), s.Source)).ToArray(),
            project.Manifest.Scripts.Select(s => new PythonRunScript(s.Id, s.ObjectId, s.Path, s.Kind.ToString().ToLowerInvariant())).ToArray(),
            handlers.ToArray(), snapshots.ToArray(), ProjectData: data);

    }
}
