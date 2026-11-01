using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;

namespace happy_agent_coding.ViewModels;

/// <summary>One flattened, read-only line in the current block projection.</summary>
public sealed record BlockLine(string Text, int Depth, bool IsRaw = false, bool IsHat = false)
{
    public Thickness IndentMargin => new(Depth * 18, 0, 0, 0);
    public string KindLabel => IsHat ? "事件" : IsRaw ? "原始代码" : "积木";
}

public partial class BlockEditorModel : ObservableObject
{
    public ObservableCollection<BlockLine> Lines { get; } = [];

    [ObservableProperty] private string title = "积木编辑区";
    [ObservableProperty] private string status = "切换到图形化编辑器以读取当前脚本。";
    [ObservableProperty] private bool isLoading;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool hasBlocks;

    public bool IsEmpty => !HasBlocks;

    public void Clear(string message)
    {
        Lines.Clear();
        HasBlocks = false;
        Title = "积木编辑区";
        Status = message;
        IsLoading = false;
    }

    public void SetLoading()
    {
        Lines.Clear();
        HasBlocks = false;
        IsLoading = true;
        Status = "正在读取积木…";
    }

    public void SetError(string message)
    {
        Lines.Clear();
        HasBlocks = false;
        Status = message;
        IsLoading = false;
    }

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
