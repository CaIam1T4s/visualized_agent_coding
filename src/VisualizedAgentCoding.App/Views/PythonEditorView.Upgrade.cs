using System;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AvaloniaEdit.Document;
using happy_agent_coding.Services;
using VisualizedAgentCoding.Application.Editor.ObjectScripts;

namespace happy_agent_coding.Views;

public partial class PythonEditorView
{
    private bool showingUpgrade;
    private async void OnUpgradeScriptClick(object? sender, RoutedEventArgs e)
    {
        editorMenu?.Hide();
        if (showingUpgrade || EditorTextBox.IsReadOnly || projection is not { Document.TemplateVersion: 1 or 2 } original || viewModel is null
            || TopLevel.GetTopLevel(this) is not Window owner || owner.FindControl<Border>("WindowFrame")?.Child is not Grid host) return;
        showingUpgrade = true;
        var current = viewModel;
        var before = new StructuralEditorState(original.Document, EditorTextBox.SelectionStart, EditorTextBox.SelectionLength,
            EditorTextBox.CaretOffset, EditorTextBox.Text);
        ScriptUpgradeView? preview = null;
        try
        {
            SetProtectionStatus("正在准备升级预览…");
            var upgraded = await current.PrepareScriptUpgradeAsync(CancellationToken.None);
            var target = new MultiRegionProjection(upgraded);
            preview = new ScriptUpgradeView(before.ViewSource!, target.ViewSource);
            Grid.SetRowSpan(preview, 2);
            host.Children.Add(preview);
            if (!await preview.Result) { SetProtectionStatus("已取消模板升级"); return; }
            if (viewModel != current || projection != original || current.PythonCode != before.Document.FullSource
                || EditorTextBox.Text != before.ViewSource)
            { SetProtectionStatus("脚本内容已变化，请重新预览升级"); return; }
            var members = target.EditableRegions.First(r => r.Id == "members").Span;
            var after = new StructuralEditorState(upgraded, members.Offset, 0, members.Offset);
            StructuralEditorHistory.Apply(EditorTextBox.Document, before, after, BeginStructureChange, RestoreStructure);
        }
        catch (Exception ex) { SetProtectionStatus("升级未应用：" + ex.Message); }
        finally
        {
            if (preview is not null) host.Children.Remove(preview);
            showingUpgrade = false;
            EditorTextBox.TextArea.Focus();
        }
    }
}
