using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using happy_agent_coding.Services;
using VisualizedAgentCoding.Application.Editor;
using VisualizedAgentCoding.Application.Editor.ObjectScripts;
using VisualizedAgentCoding.Domain.Projects;

namespace happy_agent_coding.Views;

public partial class PythonEditorView
{
    private bool showingImports;
    private async void OnManageImportsClick(object? sender, RoutedEventArgs e) => await ManageImportsAsync();
    private async void OnConvertImportClick(object? sender, RoutedEventArgs e)
    {
        var position = CurrentProjection?.ToFullPosition(EditorTextBox.TextArea.Caret.Line, EditorTextBox.TextArea.Caret.Column);
        if (position is { Item1: > 0 }) await ManageImportsAsync(position.Value.Item1);
    }
    private async Task ManageImportsAsync(int? convertLine = null)
    {
        editorMenu?.Hide();
        if (showingImports || EditorTextBox.IsReadOnly || viewModel?.OpenScript is not { } script
            || TopLevel.GetTopLevel(this) is not Window owner || owner.FindControl<Border>("WindowFrame")?.Child is not Grid host) return;
        if (script.Kind != ProjectScriptKind.User && projection?.Document.TemplateVersion != 3)
        { SetProtectionStatus("请先通过“更多 → 升级脚本模板…”升级，再管理导入。"); return; }
        var current = viewModel;
        var originalSource = current.PythonCode;
        var originalView = EditorTextBox.Text;
        var originalProjection = projection;
        var originalUser = userImportProjection?.Document;
        var selection = (EditorTextBox.SelectionStart, EditorTextBox.SelectionLength, EditorTextBox.CaretOffset);
        var before = originalProjection is null ? null : new StructuralEditorState(originalProjection.Document,
            EditorTextBox.SelectionStart, EditorTextBox.SelectionLength, EditorTextBox.CaretOffset, originalView);
        ManageImportsView? dialog = null;
        showingImports = true;
        try
        {
            ScriptImportsResult? result;
            if (convertLine is not null) result = await current.InspectScriptImportsAsync(convertLine: convertLine);
            else
            {
                var inspected = await current.InspectScriptImportsAsync();
                if (inspected.Problem is not null) throw new InvalidOperationException(inspected.Problem);
                if (current.PythonCode != originalSource || EditorTextBox.Text != originalView) return;
                dialog = new ManageImportsView(inspected.Block?.Entries ?? [],
                    current.ImportTargets.Where(t => t.Kind == "user" && t.Id != script.Id).ToArray(),
                    inspected.StandardModules, entries => current.InspectScriptImportsAsync(entries));
                Grid.SetRowSpan(dialog, 2); host.Children.Add(dialog);
                result = await dialog.Result;
            }
            if (result is null) { SetProtectionStatus("已取消导入修改"); return; }
            if (current != viewModel || current.OpenScript?.Id != script.Id || current.PythonCode != originalSource || EditorTextBox.Text != originalView)
                throw new InvalidOperationException("脚本已改变，请重新打开导入管理。");
            if (result.Source == originalSource) return;
            if (script.Kind == ProjectScriptKind.User)
            {
                if (originalUser is null) throw new InvalidOperationException("用户脚本文档尚未准备好。");
                StructuralEditorHistory.ApplyUser(EditorTextBox.Document, originalUser,
                    new(script.Id, result.Uri, result.Source, result.Block), BeginStructureChange, document =>
                    {
                        RestoreUserImports(document);
                        if (ReferenceEquals(document, originalUser))
                        {
                            var start = selection.SelectionStart;
                            var end = start + selection.SelectionLength;
                            EditorTextBox.TextArea.Selection = AvaloniaEdit.Editing.Selection.Create(EditorTextBox.TextArea,
                                selection.CaretOffset == start ? end : start, selection.CaretOffset == start ? start : end);
                            EditorTextBox.CaretOffset = selection.CaretOffset;
                        }
                    });
            }
            else
            {
                var targetDocument = await current.InspectImportObjectAsync(result.Source);
                if (current.PythonCode != originalSource || EditorTextBox.Text != originalView)
                    throw new InvalidOperationException("编辑内容已改变，未应用导入。");
                var target = new MultiRegionProjection(targetDocument);
                var offset = target.EditableRegions.First().Span.Offset;
                StructuralEditorHistory.Apply(EditorTextBox.Document, before!, new(targetDocument, offset, 0, offset), BeginStructureChange, RestoreStructure);
            }
            SetProtectionStatus("导入已更新，可撤销；保存后写入磁盘。");
        }
        catch (Exception ex) { SetProtectionStatus("导入未应用：" + ex.Message); }
        finally
        {
            if (dialog is not null) host.Children.Remove(dialog);
            showingImports = false; EditorTextBox.TextArea.Focus();
        }
    }

    private void RestoreUserImports(UserScriptImportDocument document)
    {
        try
        {
            projection = null;
            userImportProjection = new(document);
            displayedFullSource = userImportProjection.FullSource;
            EditorTextBox.IsReadOnly = false;
            EditorTextBox.TextArea.ReadOnlySectionProvider = new MultiRegionReadOnlyProvider(EditorTextBox.Document, userImportProjection.EditableRegions);
            viewModel!.SetUserImportProjection(userImportProjection);
            viewModel.PythonCode = userImportProjection.FullSource;
            EditorTextBox.CaretOffset = userImportProjection.EditableRegions.Last().Span.Offset;
            EditorTextBox.TextArea.Focus();
        }
        finally { updatingFromViewModel = false; }
    }
}
