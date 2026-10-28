namespace VisualizedAgentCoding.Application.Editor.ObjectScripts;

public sealed record ObjectScriptInspectionRequest(string Uri, long Version, string Source, string ExpectedClassName, string Kind);
public sealed record ObjectScriptInspectionResult(string Uri, long Version, string Status, int? TemplateVersion,
    SourceSpan? UserRegion, string[] Problems);
