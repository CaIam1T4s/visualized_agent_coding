using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using VisualizedAgentCoding.Application.Python;
using VisualizedAgentCoding.Domain.Blueprint;

namespace happy_agent_coding.ViewModels;

/// <summary>One flattened, read-only line in the current block projection.</summary>
public sealed record BlockLine(string Text, int Depth, bool IsRaw = false, bool IsHat = false,
    string Type = "", string Prefix = "", string Parameter = "", string Suffix = "")
{
    public Thickness IndentMargin => new(Depth * 18, 0, 0, 0);
    public string KindLabel => IsHat ? "事件" : IsRaw ? "原始代码" : "节点";
    public bool IsBlueprintNode => Type is "motion.move" or "motion.turn";
    public bool IsStandardNode => !IsBlueprintNode;
}

public partial class BlueprintEditorModel : ObservableObject
{
    public ObservableCollection<BlockLine> Lines { get; } = [];
    public BlueprintVariablePalette VariablePalette { get; } = new();
    public BlueprintClassPageModel ClassPage { get; } = new();
    public BlueprintNodeInspector Inspector { get; } = new();
    public ObservableCollection<BlueprintImportCallTemplate> ImportTemplates { get; } = [];

    [ObservableProperty] private BlueprintDocument? document;
    [ObservableProperty] private string title = "节点编辑区";
    [ObservableProperty] private string status = "切换到图形化编辑器以读取当前脚本。";
    [ObservableProperty] private bool isLoading;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool hasBlocks;

    public bool IsEmpty => !HasBlocks;

    public void Clear(string message)
    {
        Lines.Clear();
        Document = null;
        HasBlocks = false;
        Title = "积木编辑区";
        Status = message;
        IsLoading = false;
    }

    public void SetLoading()
    {
        Lines.Clear();
        if (Document is null) HasBlocks = false;
        IsLoading = true;
        Status = "正在读取节点…";
    }

    public void SetStaleError(string message)
    {
        Status = message;
        IsLoading = false;
        if (Document is null) HasBlocks = false;
    }

    public void SetError(string message)
    {
        Lines.Clear();
        Document = null;
        HasBlocks = false;
        Status = message;
        IsLoading = false;
    }

    public void SetDocument(BlueprintDocument? document)
    {
        Document = document;
        IsLoading = false;
    }

    public void RefreshDocument() => OnPropertyChanged(nameof(Document));

    public void SetVariables(IEnumerable<VisualizedAgentCoding.Domain.Blueprint.VariableRef> references)
        => VariablePalette.SetVariables(references);

    public void SelectNode(VisualizedAgentCoding.Domain.Blueprint.BlueprintNode? node)
        => Inspector.SetNode(node);

    public void SetTree(string title, IEnumerable<BlockLine> lines, string status = "只读预览")
    {
        Title = title;
        Lines.Clear();
        foreach (var line in lines) Lines.Add(line);
        HasBlocks = Lines.Count > 0;
        Status = status;
        IsLoading = false;
    }
}
