using System.Collections.Generic;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;

namespace happy_agent_coding.Services;

/// <summary>Protects the editor while its source projection is unavailable or being refreshed.</summary>
internal sealed class LockedEditorProtection : IReadOnlySectionProvider
{
    public static readonly LockedEditorProtection Instance = new();
    public bool CanInsert(int offset) => false;
    public IEnumerable<ISegment> GetDeletableSegments(ISegment segment) => System.Array.Empty<ISegment>();
}
