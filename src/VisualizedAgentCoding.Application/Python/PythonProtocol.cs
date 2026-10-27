namespace VisualizedAgentCoding.Application.Python;

public sealed record PythonHelperInfo(int ProtocolVersion, string PythonVersion, string[] Capabilities);
public sealed record PythonParseRequest(string Uri, long Version, string Source);
public sealed record PythonSourceRange(int Line, int Column, int EndLine, int EndColumn);
public sealed record PythonDiagnostic(string Severity, string Message, int Line, int Column)
{
    public string DisplayText => $"第 {Line} 行，第 {Column} 列：{Message}";
}
public sealed record PythonSymbol(string Name, PythonSourceRange Range);
public sealed record PythonImport(string Module, string[] Names, PythonSourceRange Range);
public sealed record PythonSymbols(PythonSymbol[] Classes, PythonSymbol[] Functions, PythonImport[] Imports);
public sealed record PythonParseResult(string Uri, long Version, PythonDiagnostic[] Diagnostics, PythonSymbols? Symbols);
public sealed record PythonSourceDocument(string Uri, long Version, string Source);
public sealed record PythonReferencesRequest(string ProjectRoot, string Uri, long Version, int Line, int Column, PythonSourceDocument[] Documents);
public sealed record PythonReferenceLocation(string Uri, PythonSourceRange Range, bool IsDefinition, string Name);
public sealed record PythonReferenceIssue(string Uri, string Message);
public sealed record PythonReferencesResult(string Uri, long Version, PythonReferenceLocation[] References, PythonReferenceIssue[] Issues, bool IsIncomplete);

public sealed class PythonBridgeException(string message, Exception? inner = null, int? errorCode = null)
    : Exception(message, inner)
{
    public int? ErrorCode { get; } = errorCode;
}
