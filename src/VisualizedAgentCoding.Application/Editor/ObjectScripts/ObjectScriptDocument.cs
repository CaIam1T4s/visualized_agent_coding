using System.Collections.Generic;

namespace VisualizedAgentCoding.Application.Editor.ObjectScripts;

/// <summary>A half-open UTF-16 span in the original, unnormalized full source.</summary>
public sealed record SourceSpan(int Offset, int Length);

public enum ObjectScriptStatus
{
    Recognized,
    LegacyUnmarked,
    InvalidStructure,
    UnsupportedVersion,
}

/// <summary>FullSource is authoritative; UserSource is a read-only projection, not another buffer.</summary>
public sealed record ObjectScriptDocument(string ScriptId, string Uri, long Version, string FullSource,
    ObjectScriptStatus Status, int? TemplateVersion, SourceSpan? UserRegion, string[] Problems,
    ObjectScriptRegion[]? Regions = null, VisualizedAgentCoding.Application.Editor.ManagedImportsBlock? Imports = null,
    IReadOnlyDictionary<string, string>? BackdropNames = null)
{
    public bool HasRegions => Status == ObjectScriptStatus.Recognized && Regions is { Length: > 0 };
    public bool HasUserRegion => Status == ObjectScriptStatus.Recognized && UserRegion is not null;
    public string? UserSource => HasUserRegion ? FullSource.Substring(UserRegion!.Offset, UserRegion.Length) : null;
}
