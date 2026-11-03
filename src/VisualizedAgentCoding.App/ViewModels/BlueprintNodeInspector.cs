using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using VisualizedAgentCoding.Domain.Blueprint;

namespace happy_agent_coding.ViewModels;

/// <summary>One editable argument row. Text is disabled once a value wire is connected.</summary>
public sealed partial class BlueprintArgumentItem : ObservableObject
{
    private readonly Action<string, string>? changed;

    public BlueprintArgumentItem(string key, string value, bool isBasicExpression, Action<string, string>? changed)
    {
        Key = key;
        this.changed = changed;
        IsBasicExpression = isBasicExpression;
        this.value = value;
    }

    public string Key { get; }
    public bool IsBasicExpression { get; }
    public bool IsTextEnabled => IsBasicExpression;

    [ObservableProperty] private string value;

    partial void OnValueChanged(string value) => changed?.Invoke(Key, value);
}

/// <summary>Inspector for the selected node. Editing is local until the write-back step.</summary>
public sealed partial class BlueprintNodeInspector : ObservableObject
{
    public ObservableCollection<BlueprintArgumentItem> Arguments { get; } = [];

    [ObservableProperty] private BlueprintNode? node;
    [ObservableProperty] private string title = "未选择节点";
    [ObservableProperty] private string subtitle = string.Empty;
    [ObservableProperty] private string newStatement = "self.health = 100";

    public void SetNode(BlueprintNode? node)
    {
        Node = node;
        Arguments.Clear();
        if (node is null)
        {
            Title = "未选择节点";
            Subtitle = string.Empty;
            return;
        }
        Title = node.DisplayName;
        Subtitle = node.Kind + " · " + node.Type;
        foreach (var (key, value) in node.Arguments)
        {
            if (key == "comment") continue;
            Arguments.Add(new BlueprintArgumentItem(key, value, isBasicExpression: true,
                (name, text) => node.Arguments[name] = text));
        }
    }
}
