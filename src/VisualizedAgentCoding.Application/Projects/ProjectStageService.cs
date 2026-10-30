using VisualizedAgentCoding.Application.Ports;
using VisualizedAgentCoding.Domain.Projects;

namespace VisualizedAgentCoding.Application.Projects;

public sealed class ProjectStageService(IStageStore store, IProjectImageStore images)
{
    public StageStorageSnapshot Load(ProjectWorkspace project)
    {
        var snapshot = store.Load(project.RootPath);
        StageRules.Validate(snapshot.State);
        return snapshot;
    }
    public StageStorageSnapshot Save(ProjectWorkspace project, StageState state, string? revision)
    {
        StageRules.Validate(state);
        var ids = project.Manifest.Scripts.Where(s => s.ObjectId is not null).Select(s => s.ObjectId!).ToHashSet();
        if (state.Objects.Any(o => !ids.Contains(o.ObjectId))) throw new ProjectOperationException("舞台包含已不属于当前项目的对象。");
        if (state.CurrentBackdropId is { } backdrop && !project.Manifest.Scripts.Any(s => s.ObjectId == backdrop && s.Kind == ProjectScriptKind.Backdrop))
            throw new ProjectOperationException("当前背景已不属于项目。");
        return store.Save(project.RootPath, state, revision);
    }
    public string ImportImage(ProjectWorkspace project, string objectId, string sourcePath, byte[]? contents = null)
    {
        if (!project.Manifest.Scripts.Any(s => s.ObjectId == objectId)) throw new ProjectOperationException("导入图片的对象已不存在。");
        return images.Import(project.RootPath, sourcePath, contents);
    }
    public string? CleanUnusedImages(ProjectWorkspace project, IEnumerable<string?> retainedPaths, string? sceneRevision, string? sourceOverlay = null) =>
        images.CleanUnused(project.RootPath, retainedPaths.OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), sceneRevision, sourceOverlay);
}
