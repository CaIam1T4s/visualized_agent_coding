using VisualizedAgentCoding.Application.Projects;

namespace VisualizedAgentCoding.Application.Editor.ObjectScripts;

/// <summary>A source-only merge plan. Preparing it never saves or changes the editor.</summary>
public sealed record ObjectScriptSavePlan(ProjectWorkspace Project, string ScriptId, string Path, long Version,
    string OriginalSource, string MergedSource, ObjectScriptDocument? Document);
