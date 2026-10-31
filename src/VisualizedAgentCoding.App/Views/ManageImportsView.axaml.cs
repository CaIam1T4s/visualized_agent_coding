using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using VisualizedAgentCoding.Application.Editor;

namespace happy_agent_coding.Views;

public partial class ManageImportsView : UserControl
{
    private sealed record Row(ScriptImport Entry) { public override string ToString() => Entry.Summary; }
    private sealed record Target(string Name, ScriptImport Entry) { public override string ToString() => Name; }
    private readonly ObservableCollection<Row> rows = [];
    private readonly ImportScriptTarget[] targets;
    private readonly string[] standard;
    private readonly Func<ScriptImport[], Task<ScriptImportsResult>> validate;
    private readonly TaskCompletionSource<ScriptImportsResult?> completion = new();
    public Task<ScriptImportsResult?> Result => completion.Task;
    public ManageImportsView() : this([], [], [], _ => throw new InvalidOperationException()) { }
    public ManageImportsView(ScriptImport[] entries, ImportScriptTarget[] targets, string[] standard,
        Func<ScriptImport[], Task<ScriptImportsResult>> validate)
    {
        this.targets = targets; this.standard = standard; this.validate = validate;
        InitializeComponent();
        foreach (var entry in entries) rows.Add(new(entry));
        ImportList.ItemsSource = rows;
        SourceKind.ItemsSource = new[] { "项目用户脚本", "Python 标准库", "产品运行时 API" };
        SourceKind.SelectedIndex = 0;
        Loaded += (_, _) => TargetModule.Focus();
        Unloaded += (_, _) => completion.TrySetResult(null);
        AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Escape) { completion.TrySetResult(null); e.Handled = true; } }, RoutingStrategies.Tunnel);
    }
    private void OnSourceChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (TargetModule is null) return;
        TargetModule.ItemsSource = SourceKind.SelectedIndex switch
        {
            0 => targets.Select(t => new Target(t.DisplayName, new ScriptImport(t.Module, Alias: t.Module.Split('.').Last(), Source: "project", ScriptId: t.Id))).ToArray(),
            1 => standard.Select(m => new Target(m, new ScriptImport(m))).ToArray(),
            _ => new[]
            {
                new Target("消息广播 broadcast", new ScriptImport("vac_runtime", Name: "broadcast", Source: "runtime")),
                new Target("运行计时器 timer", new ScriptImport("vac_runtime", Name: "timer", Source: "runtime")),
                new Target("切换背景 switch_backdrop", new ScriptImport("vac_runtime", Name: "switch_backdrop", Source: "runtime")),
                new Target("等待 wait", new ScriptImport("vac_runtime", Name: "wait", Source: "runtime")),
            },
        };
        SourceHint.Text = SourceKind.SelectedIndex == 2 ? "broadcast：同步广播；timer()/self.timer：运行秒数；switch_backdrop：切换背景；wait(秒)：等待并继续处理事件。"
            : SourceKind.SelectedIndex == 0 ? "仅列出其他普通用户脚本；角色、背景和当前脚本不能导入。" : "可用模块来自当前 Python 环境，不自动安装包。";
        TargetModule.SelectedIndex = -1;
    }
    private void OnTargetChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ImportAlias is not null && TargetModule.SelectedItem is Target target) ImportAlias.Text = target.Entry.Alias ?? "";
    }
    private void OnImportSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (ImportList.SelectedItem is not Row row) return;
        SourceKind.SelectedIndex = row.Entry.Source == "project" ? 0 : row.Entry.Source == "standard" ? 1 : 2;
        TargetModule.SelectedItem = TargetModule.Items.OfType<Target>().FirstOrDefault(t => t.Entry.Module == row.Entry.Module && t.Entry.Name == row.Entry.Name);
        ImportAlias.Text = row.Entry.Alias ?? "";
    }
    private ScriptImport? Choice()
    {
        if (TargetModule.SelectedItem is not Target target) { ShowError("请选择要导入的模块。"); return null; }
        return target.Entry with { Alias = AliasFor(target.Entry) };
    }
    private void OnAdd(object? sender, RoutedEventArgs e) { if (Choice() is { } entry) rows.Add(new(entry)); }
    private void OnUpdate(object? sender, RoutedEventArgs e)
    {
        var index = ImportList.SelectedIndex;
        if (index < 0) { ShowError("请先选择已导入项。"); return; }
        // Editing an existing 'from' import changes its alias, not its imported symbol.
        var existing = rows[index].Entry;
        rows[index] = new(existing with { Alias = AliasFor(existing) });
    }
    private string? AliasFor(ScriptImport entry) => string.IsNullOrWhiteSpace(ImportAlias.Text)
        ? entry.Source == "project" && entry.Name is null ? entry.Module.Split('.').Last() : null : ImportAlias.Text.Trim();
    private void OnRemove(object? sender, RoutedEventArgs e) { if (ImportList.SelectedIndex >= 0) rows.RemoveAt(ImportList.SelectedIndex); }
    private void OnCancel(object? sender, RoutedEventArgs e) => completion.TrySetResult(null);
    private async void OnAccept(object? sender, RoutedEventArgs e)
    {
        if (!ApplyButton.IsEnabled) return;
        ApplyButton.IsEnabled = false;
        try { completion.TrySetResult(await validate(rows.Select(r => r.Entry).ToArray())); }
        catch (Exception ex) { ShowError(ex.Message); }
        finally { ApplyButton.IsEnabled = true; }
    }
    private void ShowError(string message) { ValidationError.Text = message; ValidationError.IsVisible = true; }
}
