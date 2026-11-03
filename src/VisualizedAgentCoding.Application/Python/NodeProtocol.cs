namespace VisualizedAgentCoding.Application.Python;

/// <summary>Read-only node request for one already-dedented script region.</summary>
public sealed record PythonNodeRequest(string Source, string? Mode = null);

/// <summary>
/// Projected node. New fields are optional so older helper responses remain readable.
/// Source offsets are relative to the region body, in UTF-16 code units.
/// </summary>
public sealed record PythonNode(
    string Type,
    Dictionary<string, string>? Arguments,
    PythonNode[]? Children,
    PythonNode[]? ElseChildren,
    string? Text,
    int Line = 0,
    int Start = 0,
    int Length = 0,
    int EndLine = 0,
    string? Scope = null,
    string? Kind = null,
    PythonNodePort[]? Ports = null);

public sealed record PythonNodePort(
    string Id,
    string Kind,
    string Relation,
    string? Type = null,
    bool IsInput = true,
    bool IsOptional = false,
    string? Label = null);

public sealed record PythonNodeTree(string? Error, PythonNode[] Nodes);
