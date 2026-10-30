using VisualizedAgentCoding.Domain.Projects;

namespace VisualizedAgentCoding.Application.Ports;

public sealed record StageStorageSnapshot(StageState State, string? Revision);
public interface IStageStore
{
    StageStorageSnapshot Load(string root);
    StageStorageSnapshot Save(string root, StageState state, string? expectedRevision);
}
public interface IProjectImageStore
{
    string Import(string root, string sourcePath, byte[]? contents = null);
    string? CleanUnused(string root, IReadOnlyCollection<string> retainedPaths, string? sceneRevision, string? sourceOverlay = null);
}
