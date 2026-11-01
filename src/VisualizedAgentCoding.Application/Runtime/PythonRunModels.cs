using VisualizedAgentCoding.Domain.Projects;
using System.Text.Json.Serialization;

namespace VisualizedAgentCoding.Application.Runtime;

public sealed record PythonEnvironmentInfo(string Executable, string Version);
public sealed record PythonRunSource(string Path, string Source);
public sealed record PythonRunScript(string Id, string? ObjectId, string Path, string Kind);
public sealed record RuntimeImageSize(double Width, double Height);
public sealed record RuntimeVariableDefinition(string Name, string Type, string Value);
public sealed record RuntimeListDefinition(string Name, string[] Values);
public sealed record RuntimeProjectData(RuntimeVariableDefinition[] Variables, RuntimeListDefinition[] Lists);
public sealed record PythonRunRequest(string SessionId, string Executable, string Root,
    string MainScript, PythonRunSource[] Sources, PythonRunScript[] Scripts, ProjectEventHandler[] Handlers,
    [property: JsonIgnore] RuntimeSourceSnapshot[]? PresentationSources = null, StageState? InitialStage = null,
    bool KeepAlive = false, IReadOnlyDictionary<string, RuntimeImageSize>? StageImageSizes = null,
    IReadOnlyDictionary<string, RuntimeImageSize>? StageCostumeImageSizes = null,
    RuntimeProjectData? ProjectData = null);
// Columns from the runner are converted to one-based UTF-16 before crossing the protocol.
public sealed record PythonRunFrame(string Path, int Line, string Function, int? Column = null, int? EndLine = null, int? EndColumn = null);
public sealed record PythonExceptionData(string Type, string Message, PythonRunFrame[] Frames,
    PythonExceptionData? Cause = null, PythonExceptionData? Context = null, bool SuppressContext = false,
    PythonExceptionData[]? Children = null, bool Truncated = false, string[]? Notes = null);
public sealed record PythonRunEvent(string Kind, string Text = "", PythonRunFrame[]? Frames = null,
    PythonExceptionData? Exception = null, string? RawTraceback = null, int ProtocolVersion = 1, RuntimeStageFrame? Stage = null);
public sealed record RuntimeStageFrame(string SessionId, long Sequence, StageObjectState[] Objects, string? CurrentBackdropId = null);
