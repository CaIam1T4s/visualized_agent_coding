using System;
using System.Linq;
using Avalonia.Controls;
using AvaloniaEdit.Document;
using happy_agent_coding.Services;
using VisualizedAgentCoding.Application.Editor.ObjectScripts;
using VisualizedAgentCoding.Domain.Projects;

namespace happy_agent_coding.Views;

public partial class PythonEditorView
{
    private bool showingMessages;

    private async void EditMessage(string? id = null, bool delete = false)
    {
        editorMenu?.Hide();
        if (showingMessages || EditorTextBox.IsReadOnly || projection is not MultiRegionProjection multi
            || multi.Document.TemplateVersion != 3 || viewModel is null
            || TopLevel.GetTopLevel(this) is not Window owner
            || owner.FindControl<Border>("WindowFrame")?.Child is not Grid host) return;
        var current = viewModel;
        var original = multi.Document;
        var before = new StructuralEditorState(original, EditorTextBox.SelectionStart, EditorTextBox.SelectionLength, EditorTextBox.CaretOffset);
        var selected = original.Regions!.FirstOrDefault(r => r.Id == id);
        var name = MessageEvents.Name(selected?.EventName) ?? "";
        var preview = delete && selected is not null ? multi.ViewSource.Substring(
            multi.EditableRegions.First(r => r.Id == id).Span.Offset, multi.EditableRegions.First(r => r.Id == id).Span.Length) : null;
        var dialog = new MessageEventView(delete ? "删除消息区域" : id is null ? "添加消息事件" : "修改消息名称", name, value =>
        {
            try { if (!delete) ObjectScriptFeatures.ValidateMessage(original, value, id); return null; }
            catch (ArgumentException ex) { return ex.Message; }
        }, preview, renaming: id is not null && !delete);
        showingMessages = true;
        Grid.SetRowSpan(dialog, 2);
        host.Children.Add(dialog);
        try
        {
            var result = await dialog.Result;
            if (result is null) return;
            if (current != viewModel || current.OpenScript?.Id != original.ScriptId || current.PythonCode != original.FullSource
                || projection is not MultiRegionProjection active || active.Document != original)
                throw new InvalidOperationException("脚本已改变，请重新打开消息事件操作。");
            var updated = delete ? ObjectScriptFeatures.DeleteMessage(original, id!)
                : id is null ? ObjectScriptFeatures.AddMessage(original, result) : ObjectScriptFeatures.RenameMessage(original, id, result);
            if (ReferenceEquals(updated, original)) return;
            var target = new MultiRegionProjection(updated);
            var targetId = delete ? updated.Regions!.First().Id : id ?? updated.Regions!.Last().Id;
            var region = target.EditableRegions.First(r => r.Id == targetId);
            var offset = region.Span.Offset;
            var length = 0;
            if (id is null)
            {
                var pass = target.ViewSource.IndexOf(ProjectScriptTemplates.UserPlaceholderStatement, offset, StringComparison.Ordinal);
                if (pass >= offset && pass < offset + region.Span.Length)
                {
                    var line = new TextDocument(target.ViewSource).GetLineByOffset(pass);
                    offset = line.Offset; length = line.Length;
                }
            }
            StructuralEditorHistory.Apply(EditorTextBox.Document, before, new(updated, offset, length, offset + length),
                BeginStructureChange, RestoreStructure);
            SetProtectionStatus("消息区域已更新，可撤销；保存后写入磁盘。");
        }
        catch (Exception ex) { SetProtectionStatus("消息区域未修改：" + ex.Message); }
        finally
        {
            host.Children.Remove(dialog);
            showingMessages = false;
            EditorTextBox.TextArea.Focus();
        }
    }
}
