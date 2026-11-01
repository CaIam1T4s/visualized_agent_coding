namespace VisualizedAgentCoding.Application.Python;

/// <summary>Read-only block tree for one already-dedented script region.</summary>
public sealed record PythonBlockRequest(string Source);
public sealed record PythonBlockNode(string Type, Dictionary<string, string>? Arguments,
    PythonBlockNode[]? Children, PythonBlockNode[]? ElseChildren, string? Text, int Line = 0);
public sealed record PythonBlockTree(string? Error, PythonBlockNode[] Nodes);
