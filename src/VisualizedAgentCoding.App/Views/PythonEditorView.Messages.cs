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

    private async void EditMessage(string? id = null, bool delete = false, bool spriteClick = false, bool stageClick = false, bool keyEvent = false)
    {
        editorMenu?.Hide();
        if (showingMessages || EditorTextBox.IsReadOnly || projection is not MultiRegionProjection multi
            || multi.Document.TemplateVersion != 3 || viewModel is null
            || TopLevel.GetTopLevel(this) is not Window owner
            || owner.FindControl<Border>("WindowFrame")?.Child is not Grid host) return;
        if (spriteClick && viewModel.OpenScript?.Kind != ProjectScriptKind.Object) return;
        if (stageClick && viewModel.OpenScript?.Kind != ProjectScriptKind.Backdrop) return;
        var current = viewModel;
        var original = multi.Document;
        StructuralEditorState before;
        try { before = StructuralEditorState.Capture(multi, EditorTextBox.Text, EditorTextBox.SelectionStart,
            EditorTextBox.SelectionLength, EditorTextBox.CaretOffset); }
        catch (InvalidOperationException ex) { SetProtectionStatus(ex.Message); return; }
        var selected = original.Regions!.FirstOrDefault(r => r.Id == id);
        var name = MessageEvents.Name(selected?.EventName) ?? KeyEvents.Name(selected?.EventName) ?? "";
        var preview = delete && selected is not null ? multi.ViewSource.Substring(
            multi.EditableRegions.First(r => r.Id == id).Span.Offset, multi.EditableRegions.First(r => r.Id == id).Span.Length) : null;
        var title = spriteClick ? (delete ? "删除角色点击区域" : "添加角色点击事件")
            : stageClick ? (delete ? "删除舞台点击区域" : "添加背景点击事件")
            : keyEvent ? (delete ? "删除按键事件区域" : "添加按键事件")
            : delete ? "删除消息区域" : id is null ? "添加消息事件" : "修改消息名称";
        var dialog = new MessageEventView(title, name, value =>
        {
            try { if (!delete) { if (spriteClick) ObjectScriptFeatures.ValidateSpriteClick(original); else if (stageClick) ObjectScriptFeatures.ValidateStageClick(original); else if (keyEvent) ObjectScriptFeatures.ValidateKey(original, value, id); else ObjectScriptFeatures.ValidateMessage(original, value, id); } return null; }
            catch (ArgumentException ex) { return ex.Message; }
        }, preview, renaming: id is not null && !delete, fixedEvent: spriteClick || stageClick, keyEvent: keyEvent);
        showingMessages = true;
        Grid.SetRowSpan(dialog, 2);
        host.Children.Add(dialog);
        try
        {
            var result = await dialog.Result;
            if (result is null) return;
            if (current != viewModel || current.OpenScript?.Id != original.ScriptId || current.PythonCode != original.FullSource
                || EditorTextBox.Text != before.ViewSource
                || projection is not MultiRegionProjection active || active.Document != original)
                throw new InvalidOperationException("脚本已改变，请重新打开消息事件操作。");
            var updated = spriteClick
                ? delete ? ObjectScriptFeatures.DeleteSpriteClick(original, id!) : ObjectScriptFeatures.AddSpriteClick(original)
                : stageClick
                ? delete ? ObjectScriptFeatures.DeleteStageClick(original, id!) : ObjectScriptFeatures.AddStageClick(original)
                : keyEvent
                ? delete ? ObjectScriptFeatures.DeleteKey(original, id!) : ObjectScriptFeatures.AddKey(original, result)
                : delete ? ObjectScriptFeatures.DeleteMessage(original, id!)
                : id is null ? ObjectScriptFeatures.AddMessage(original, result) : ObjectScriptFeatures.RenameMessage(original, id, result);
            if (ReferenceEquals(updated, original)) return;
            var target = new MultiRegionProjection(updated);
            target.UpdateRegions(before.RegionBodies!.Where(pair => updated.Regions!.Any(r => r.Id == pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value));
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
            var after = StructuralEditorState.Capture(target, target.ViewSource, offset, length, offset + length);
            StructuralEditorHistory.Apply(EditorTextBox.Document, before, after,
                BeginStructureChange, RestoreStructure);
            SetProtectionStatus("事件区域已更新，可撤销；保存后写入磁盘。");
        }
        catch (Exception ex) { SetProtectionStatus("事件区域未修改：" + ex.Message); }
        finally
        {
            host.Children.Remove(dialog);
            showingMessages = false;
            EditorTextBox.TextArea.Focus();
        }
    }
}
