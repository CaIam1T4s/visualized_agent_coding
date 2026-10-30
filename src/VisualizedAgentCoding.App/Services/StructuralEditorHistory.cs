using System;
using System.Collections.Generic;
using AvaloniaEdit.Document;
using VisualizedAgentCoding.Application.Editor.ObjectScripts;

namespace happy_agent_coding.Services;

public sealed record StructuralEditorState(ObjectScriptDocument Document, int SelectionStart, int SelectionLength, int CaretOffset,
    string? ViewSource = null, IReadOnlyDictionary<string, string>? RegionBodies = null)
{
    public IEditorProjection CreateProjection()
    {
        var projection = EditorProjection.Create(Document);
        if (projection is MultiRegionProjection multi && RegionBodies is not null) multi.UpdateRegions(RegionBodies);
        else if (projection is ObjectScriptProjection single && ViewSource is not null) single.Update(ViewSource);
        return projection;
    }

    public static StructuralEditorState Capture(IEditorProjection projection, string viewSource, int start, int length, int caret)
    {
        if (projection.ViewSource != viewSource) throw new InvalidOperationException("编辑器视图已变化，请重试区域操作。");
        return new(projection.Document, start, length, caret, viewSource,
            (projection as MultiRegionProjection)?.ViewRegionSources());
    }
}

/// <summary>Bracket a native text undo operation with matching model and selection restoration.</summary>
public static class StructuralEditorHistory
{
    public static void RestoreSelection(AvaloniaEdit.TextEditor editor, StructuralEditorState state)
    {
        var start = state.SelectionStart;
        var end = start + state.SelectionLength;
        // Setting TextEditor.CaretOffset clears its selection.
        editor.CaretOffset = state.CaretOffset;
        editor.TextArea.Selection = AvaloniaEdit.Editing.Selection.Create(editor.TextArea,
            state.CaretOffset == start ? end : start, state.CaretOffset == start ? start : end);
    }

    public static void ApplyUser(TextDocument document,
        VisualizedAgentCoding.Application.Editor.UserScriptImportDocument before,
        VisualizedAgentCoding.Application.Editor.UserScriptImportDocument after, Action begin,
        Action<VisualizedAgentCoding.Application.Editor.UserScriptImportDocument> restore)
        => ApplySnapshot(document, before, after,
            new VisualizedAgentCoding.Application.Editor.UserScriptImportProjection(before).ViewSource,
            new VisualizedAgentCoding.Application.Editor.UserScriptImportProjection(after).ViewSource, begin, restore);

    private static void ApplySnapshot<T>(TextDocument document, T before, T after, string original, string replacement,
        Action begin, Action<T> restore)
    {
        if (document.Text != original) throw new InvalidOperationException("编辑内容已变化，未应用区域操作，请重试。");
        document.UndoStack.StartUndoGroup();
        try
        {
            document.UndoStack.Push(new Boundary(() => restore(before), begin));
            begin();
            document.Replace(0, document.TextLength, replacement);
            restore(after);
            document.UndoStack.Push(new Boundary(begin, () => restore(after)));
        }
        finally { document.UndoStack.EndUndoGroup(); }
    }
    public static void Apply(TextDocument document, StructuralEditorState before, StructuralEditorState after,
        Action begin, Action<StructuralEditorState> restore)
    {
        if (document.Text != (before.ViewSource ?? before.CreateProjection().ViewSource))
            throw new InvalidOperationException("编辑内容已变化，未应用区域操作，请重试。");
        var replacement = after.ViewSource ?? after.CreateProjection().ViewSource;
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
