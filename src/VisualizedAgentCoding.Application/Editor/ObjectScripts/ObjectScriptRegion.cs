namespace VisualizedAgentCoding.Application.Editor.ObjectScripts;

public sealed record ObjectScriptRegion(string Id, string Kind, SourceSpan Span, int Indentation, string? EventName = null);
