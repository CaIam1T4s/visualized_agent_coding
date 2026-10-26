namespace VisualizedAgentCoding.Domain.Projects;

/// <summary>The persisted project manifest stored in <c>project.json</c>.</summary>
public sealed record ProjectManifest(int SchemaVersion, string Name, string MainScript);
