using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using AvaloniaEdit;
using AvaloniaEdit.Utils;
using AvaloniaEdit.Document;
using happy_agent_coding.Services;
using VisualizedAgentCoding.Application.Editor.ObjectScripts;

namespace happy_agent_coding.Views;

public partial class PythonEditorView
{
    private void ConfigureIndentationPaste()
    {
        // AvaloniaEdit's default paste replaces ALL tabs when ConvertTabsToSpaces is set,
        // including string contents. Override that command rather than repairing lost text.
        EditorTextBox.TextArea.CommandBindings.Insert(0, new RoutedCommandBinding(ApplicationCommands.Paste,
            OnPasteCode, (_, e) => { e.CanExecute = !EditorTextBox.IsReadOnly; e.Handled = true; }));
    }

    private async void OnPasteCode(object? sender, ExecutedRoutedEventArgs e)
    {
        e.Handled = true;
        if (EditorTextBox.IsReadOnly) return;
        var original = EditorTextBox.Text;
        var originalVersion = EditorTextBox.Document.Version;
        var originalViewModel = viewModel;
        var revision = viewModel?.EditorSourceRevision;
        var script = viewModel?.OpenScript?.Id;
        var selection = EditorTextBox.TextArea.Selection;
        var caretOffset = EditorTextBox.CaretOffset;
        string? text;
        try
        {
            var clipboard = TopLevel.GetTopLevel(EditorTextBox)?.Clipboard;
            if (clipboard is null) return;
            using var data = await clipboard.TryGetDataAsync();
            text = data is null ? null : await data.TryGetTextAsync();
        }
        catch (Exception) { SetProtectionStatus("无法读取剪贴板，未粘贴"); return; }
        if (text is null || EditorTextBox.IsReadOnly || original != EditorTextBox.Text
            || originalVersion != EditorTextBox.Document.Version || originalViewModel != viewModel
            || revision != viewModel?.EditorSourceRevision
            || script != viewModel?.OpenScript?.Id || selection != EditorTextBox.TextArea.Selection
            || caretOffset != EditorTextBox.CaretOffset) return;
        var start = EditorTextBox.SelectionStart;
        var newline = TextUtilities.GetNewLineFromDocument(EditorTextBox.Document, EditorTextBox.TextArea.Caret.Line);
        text = TextUtilities.NormalizeNewLines(text, newline);
        using (EditorTextBox.Document.RunUpdate())
        {
            EditorTextBox.TextArea.Selection.ReplaceSelectionWithText(text);
            var end = EditorTextBox.CaretOffset;
            // Do not normalize anything outside the successful, contiguous insertion.
            if (end - start == text.Length && EditorTextBox.Document.GetText(start, text.Length) == text)
                NormalizeIndentation(start, end);
        }
        EditorTextBox.TextArea.Caret.BringCaretToView();
    }

    private void NormalizeIndentation(int start = 0, int end = int.MaxValue)
    {
        if (EditorTextBox.IsReadOnly) return;
        var document = EditorTextBox.Document;
        var spans = EditorTextBox.TextArea.ReadOnlySectionProvider is MultiRegionReadOnlyProvider regions
            ? regions.ReadSpans() : new[] { new SourceSpan(0, document.TextLength) };
        var edits = spans.SelectMany(span => PythonWrapperIndentation.FindIndentationTabs(
                document.GetText(span.Offset, span.Length), end == int.MaxValue && projection is MultiRegionProjection multi
                    ? multi.Document.Regions!.First(r => r.Id == multi.EditableRegions.First(p => p.Span.Offset == span.Offset).Id).Indentation
                    : 0).Select(edit =>
                new PythonWrapperIndentation.IndentationEdit(span.Offset + edit.Offset, edit.Replacement)))
            .Where(edit => edit.Offset >= start && edit.Offset < end).OrderByDescending(edit => edit.Offset).ToArray();
        if (edits.Length == 0) return;
        var caret = document.CreateAnchor(EditorTextBox.CaretOffset);
        caret.SurviveDeletion = true;
        caret.MovementType = AnchorMovementType.AfterInsertion;
        document.UndoStack.StartUndoGroup();
        try
        {
            using (document.RunUpdate())
                foreach (var edit in edits) document.Replace(edit.Offset, 1, edit.Replacement);
        }
        finally { document.UndoStack.EndUndoGroup(); }
        EditorTextBox.CaretOffset = caret.Offset;
        if (end == int.MaxValue)
        {
            EditorTextBox.TextArea.Focus();
            SetProtectionStatus("已将代码行首 Tab 转换为空格（4 列缩进），可撤销");
        }
    }
}
