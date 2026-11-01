using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using happy_agent_coding.ViewModels;

namespace happy_agent_coding.Views;

public partial class VariableManagerView : UserControl
{
    private readonly MainViewModel? model;
    private readonly VariableManagerModel drafts;
    private readonly TaskCompletionSource<bool> completion = new();
    private bool ready;
    public Task<bool> Result => completion.Task;

    public VariableManagerView() : this(null) { }

    public VariableManagerView(MainViewModel? model)
    {
        this.model = model;
        InitializeComponent();
        drafts = new VariableManagerModel(model?.ProjectVariables ?? [], model?.ProjectLists ?? []);
        DataContext = drafts;
        ready = true;
        Loaded += (_, _) => RefreshReferences();
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ready) RefreshReferences();
    }

    private void OnTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!ready) return;
        drafts.EnsureSelectionForTab();
        RefreshReferences();
    }

    private int referenceVersion;

    private async void RefreshReferences()
    {
        if (!ready) return;
        var version = ++referenceVersion;
        var useList = drafts.TabIndex == 1;
        var variable = useList ? null : drafts.SelectedVariable;
        var list = useList ? drafts.SelectedList : null;
        var name = variable?.Name ?? list?.Name;
        var target = useList ? ListReferences : VariableReferences;
        var other = useList ? VariableReferences : ListReferences;
        if (other is not null) other.IsVisible = false;
        if (target is null) return;
        if (model is null || string.IsNullOrWhiteSpace(name))
        {
            target.Text = "引用：无法检查";
            target.IsVisible = true;
            return;
        }
        IReadOnlyList<VisualizedAgentCoding.Application.Projects.ProjectDataReference> references;
        try { references = await model.FindProjectDataReferencesAsync(name); }
        catch (Exception)
        {
            if (version == referenceVersion) { target.Text = "引用：无法检查"; target.IsVisible = true; }
            return;
        }
        if (version != referenceVersion) return;
        target.Text = references.Count == 0
            ? "引用：无"
            : "引用：" + references.Count + " 处" + Environment.NewLine +
              string.Join(Environment.NewLine, references.Take(5).Select(item => "· " + item.Display));
        target.IsVisible = true;
    }

    private void OnAddVariable(object? sender, RoutedEventArgs e) { drafts.AddVariable(); RefreshReferences(); }
    private void OnAddList(object? sender, RoutedEventArgs e) { drafts.AddList(); RefreshReferences(); }
    private void OnRemove(object? sender, RoutedEventArgs e)
    {
        if (model is null) { ShowError("尚未打开项目。"); return; }
        var variableIndex = drafts.Variables.IndexOf(drafts.SelectedVariable!);
        var listIndex = drafts.Lists.IndexOf(drafts.SelectedList!);
        var variable = drafts.TabIndex == 0 ? drafts.SelectedVariable : null;
        var list = drafts.TabIndex == 1 ? drafts.SelectedList : null;
        var name = variable?.Name ?? list?.Name;
        if (string.IsNullOrWhiteSpace(name)) { ShowError("请先选择要删除的对象。"); return; }
        IReadOnlyList<VisualizedAgentCoding.Application.Projects.ProjectDataReference> references;
        try { references = model.FindProjectDataReferences(name); }
        catch (Exception ex) { ShowError("引用检查失败：" + ex.Message); return; }
        if (references.Count > 0)
        {
            ShowError($"变量/列表“{name}”仍有 {references.Count} 处引用，已拒绝删除。");
            return;
        }
        drafts.RemoveSelected();
        var error = model.SaveProjectData(drafts.ToVariables(), drafts.ToLists());
        if (error is not null)
        {
            if (variable is not null) drafts.Variables.Insert(Math.Max(0, variableIndex), variable);
            if (list is not null) drafts.Lists.Insert(Math.Max(0, listIndex), list);
            ShowError(error);
        }
        else
        {
            ErrorText.IsVisible = false;
        }
        RefreshReferences();
    }
    private void OnCancel(object? sender, RoutedEventArgs e) => completion.TrySetResult(false);

    private void OnAccept(object? sender, RoutedEventArgs e)
    {
        if (model is null) { ShowError("尚未打开项目。"); return; }
        var error = model.SaveProjectData(drafts.ToVariables(), drafts.ToLists());
        if (error is not null) { ShowError(error); return; }
        completion.TrySetResult(true);
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.IsVisible = true;
    }
}
