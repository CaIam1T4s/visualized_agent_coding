using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AvaloniaEdit.Document;
using happy_agent_coding.Services;
using VisualizedAgentCoding.Application.Editor.ObjectScripts;
using VisualizedAgentCoding.Domain.Projects;

namespace happy_agent_coding.Views;

public partial class PythonEditorView
{
    private async void OnAddMethodClick(object? sender, RoutedEventArgs e)
    {
        editorMenu?.Hide();
        if (TopLevel.GetTopLevel(this) is not Window owner
            || owner.FindControl<Border>("WindowFrame")?.Child is not Grid host) return;
        var scriptId = viewModel?.OpenScript?.Id;
        var dialog = new AddMethodView(identifier =>
        {
            var reserved = "False None True and as assert async await break class continue def del elif else except finally for from global if import in is lambda nonlocal not or pass raise return try while with yield".Split(' ');
            if (!System.Text.RegularExpressions.Regex.IsMatch(identifier, @"\A[_\p{L}][_\p{L}\p{Nd}\p{Mn}\p{Mc}]*\z")
                || reserved.Contains(identifier) || identifier == "initialize" || identifier.StartsWith("_vac_", StringComparison.Ordinal))
                return "请输入有效且非系统保留的 Python 方法名称";
            if (System.Text.RegularExpressions.Regex.IsMatch(EditorTextBox.Text,
                @"(?m)^\s*(?:async\s+)?def\s+" + System.Text.RegularExpressions.Regex.Escape(identifier) + @"\s*\("))
                return "已存在同名方法";
            return null;
        });
        Grid.SetRowSpan(dialog, 2);
        host.Children.Add(dialog);
        string? result;
        try { result = await dialog.Result; }
        finally { host.Children.Remove(dialog); EditorTextBox.TextArea.Focus(); }
        if (result is null || EditorTextBox.IsReadOnly || viewModel?.OpenScript?.Id != scriptId) return;
        var multi = projection as MultiRegionProjection;
        var id = multi?.Document.Regions!.First(r => r.Kind == "members").Id;
        var region = multi?.EditableRegions.First(r => r.Id == id);
        var offset = region is null ? EditorTextBox.Document.TextLength : region.Span.Offset + region.Span.Length;
        var nl = EditorTextBox.Text.Contains("\r\n") ? "\r\n" : "\n";
        var code = (offset > 0 && EditorTextBox.Document.GetCharAt(offset - 1) != '\n' ? nl : "") + nl
            + "def " + result + (multi is null ? "():" : "(self):") + nl + "    "
            + ProjectScriptTemplates.UserPlaceholderStatement + nl;
        EditorTextBox.Document.Insert(offset, code);
        SelectPlaceholderLine(offset + code.IndexOf("pass", StringComparison.Ordinal));
        EditorTextBox.TextArea.Focus();
        EditorTextBox.ScrollToLine(EditorTextBox.TextArea.Caret.Line);
    }

    private void AddInitializeWithHistory()
    {
        editorMenu?.Hide();
        if (EditorTextBox.IsReadOnly || projection is not MultiRegionProjection multi || viewModel is null) return;
        var updated = ObjectScriptFeatures.AddInitialize(multi.Document);
        var target = new MultiRegionProjection(updated);
        var id = updated.Regions!.First(r => r.Kind == "initialize").Id;
        var region = target.EditableRegions.First(r => r.Id == id);
        var body = target.ViewSource.Substring(region.Span.Offset, region.Span.Length);
        var pass = body.IndexOf("pass", StringComparison.Ordinal);
        var visible = new TextDocument(target.ViewSource);
        var line = visible.GetLineByOffset(region.Span.Offset + Math.Max(0, pass));
        var after = new StructuralEditorState(updated, pass >= 0 ? line.Offset : region.Span.Offset,
            pass >= 0 ? line.Length : 0, pass >= 0 ? line.EndOffset : region.Span.Offset);
        if (ReferenceEquals(updated, multi.Document))
        {
            RestoreStructuralSelection(after);
            return;
        }
        var before = new StructuralEditorState(multi.Document, EditorTextBox.SelectionStart,
            EditorTextBox.SelectionLength, EditorTextBox.CaretOffset);
        StructuralEditorHistory.Apply(EditorTextBox.Document, before, after, BeginStructureChange, RestoreStructure);
    }

    private void BeginStructureChange()
    {
        protectionCancellation?.Cancel();
        protectionGeneration++;
        updatingFromViewModel = true;
    }

    private void RestoreStructure(StructuralEditorState state)
    {
        try
        {
            var restored = EditorProjection.Create(state.Document);
            if (restored is ObjectScriptProjection single && state.ViewSource is not null) single.Update(state.ViewSource);
            projection = restored;
            displayedFullSource = restored.FullSource;
            // Changing IsReadOnly reinstalls AvaloniaEdit's default provider for v1.
            EditorTextBox.IsReadOnly = true;
            EditorTextBox.IsReadOnly = false;
            if (restored is MultiRegionProjection multi)
                EditorTextBox.TextArea.ReadOnlySectionProvider = new MultiRegionReadOnlyProvider(EditorTextBox.Document, multi.EditableRegions);
            viewModel!.RestoreEditorStructure(restored);
            RestoreStructuralSelection(state);
            SetProtectionStatus("用户代码");
        }
        finally { updatingFromViewModel = false; }
    }

    private void RestoreStructuralSelection(StructuralEditorState state)
    {
        var start = state.SelectionStart;
        var end = state.SelectionStart + state.SelectionLength;
        EditorTextBox.TextArea.Selection = AvaloniaEdit.Editing.Selection.Create(EditorTextBox.TextArea,
            state.CaretOffset == state.SelectionStart ? end : start, state.CaretOffset == state.SelectionStart ? start : end);
        EditorTextBox.CaretOffset = state.CaretOffset;
        EditorTextBox.TextArea.Focus();
        EditorTextBox.ScrollToLine(EditorTextBox.TextArea.Caret.Line);
    }

    private void SelectPlaceholderLine(int offset)
    {
        var line = EditorTextBox.Document.GetLineByOffset(offset);
        EditorTextBox.Select(line.Offset, line.Length);
    }
}
