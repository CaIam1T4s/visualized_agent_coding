using System;
using AvaloniaEdit.Document;
using VisualizedAgentCoding.Application.Editor.ObjectScripts;

namespace happy_agent_coding.Services;

public sealed record StructuralEditorState(ObjectScriptDocument Document, int SelectionStart, int SelectionLength, int CaretOffset,
    string? ViewSource = null);

/// <summary>Bracket a native text undo operation with matching model and selection restoration.</summary>
public static class StructuralEditorHistory
{
    public static void Apply(TextDocument document, StructuralEditorState before, StructuralEditorState after,
        Action begin, Action<StructuralEditorState> restore)
    {
        if (document.Text != (before.ViewSource ?? EditorProjection.Create(before.Document).ViewSource))
            throw new InvalidOperationException("The editor has changed before the structural operation.");
        var replacement = after.ViewSource ?? EditorProjection.Create(after.Document).ViewSource;
        document.UndoStack.StartUndoGroup();
        try
        {
            // Undo runs these boundaries in reverse order. Only the native text
            // operation modifies the document during playback.
            document.UndoStack.Push(new Boundary(() => restore(before), begin));
            begin();
            document.Replace(0, document.TextLength, replacement);
            restore(after);
            document.UndoStack.Push(new Boundary(begin, () => restore(after)));
        }
        finally { document.UndoStack.EndUndoGroup(); }
    }

    private sealed class Boundary(Action undo, Action redo) : IUndoableOperation
    {
        public void Undo() => undo();
        public void Redo() => redo();
    }
}
