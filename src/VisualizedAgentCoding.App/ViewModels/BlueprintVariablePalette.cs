using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using VisualizedAgentCoding.Domain.Blueprint;

namespace happy_agent_coding.ViewModels;

/// <summary>One draggable variable template. The identity is the structured reference.</summary>
public sealed class VariableTemplateItem
{
    public required VariableRef Reference { get; init; }
    public string Name => Reference.Name;
    public string DisplayName => Reference.DisplayName;
    public string GeneratedCode => Reference.GeneratedCode;
    public string SearchText => $"{Reference.Name} {Reference.ScopeLabel} {Reference.OwnerLabel}";
    public string HoverText => Reference.HoverText;
}

/// <summary>A collapsible variable scope group shown in the node palette.</summary>
public sealed partial class VariableTemplateGroup : ObservableObject
{
    public required NodeScope Scope { get; init; }
    public required string Title { get; init; }
    public ObservableCollection<VariableTemplateItem> Items { get; } = [];
    public ObservableCollection<VariableTemplateItem> FilteredItems { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVisibleItems))]
    private bool isExpanded = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVisibleItems))]
    private bool hasMatches = true;

    public bool HasVisibleItems => IsExpanded && HasMatches && FilteredItems.Count > 0;

    public void RebuildFilter(string query)
    {
        FilteredItems.Clear();
        foreach (var item in Items)
        {
            if (string.IsNullOrWhiteSpace(query)
                || item.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))
                FilteredItems.Add(item);
        }
        HasMatches = FilteredItems.Count > 0;
    }
}

/// <summary>
/// Variable templates grouped by Python scope. Categories can be expanded/collapsed
/// and searched; the panel never merges same-named variables from different scopes.
/// </summary>
public sealed partial class BlueprintVariablePalette : ObservableObject
{
    private static readonly (NodeScope Scope, string Title)[] ScopeOrder =
    [
        (NodeScope.Parameter, "参数"),
        (NodeScope.Local, "局部"),
        (NodeScope.LoopVariable, "循环"),
        (NodeScope.InstanceAttribute, "实例属性"),
        (NodeScope.ClassAttribute, "类属性"),
        (NodeScope.ProjectVariable, "项目变量"),
        (NodeScope.ModuleGlobal, "模块全局"),
    ];

    public ObservableCollection<VariableTemplateGroup> Groups { get; } = [];

    [ObservableProperty]
    private string searchText = string.Empty;

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    public void SetVariables(IEnumerable<VariableRef> references)
    {
        ArgumentNullException.ThrowIfNull(references);
        var expanded = Groups.ToDictionary(group => group.Scope, group => group.IsExpanded);
        Groups.Clear();
        foreach (var (scope, title) in ScopeOrder)
        {
            var items = references
                .Where(reference => reference.Scope == scope)
                .Select(reference => new VariableTemplateItem { Reference = reference })
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (items.Count == 0) continue;
            var group = new VariableTemplateGroup { Scope = scope, Title = title };
            if (expanded.TryGetValue(scope, out var isExpanded)) group.IsExpanded = isExpanded;
            foreach (var item in items) group.Items.Add(item);
            group.RebuildFilter(SearchText);
            Groups.Add(group);
        }
        ApplyFilter();
    }

    public void Add(VariableRef reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var group = Groups.FirstOrDefault(item => item.Scope == reference.Scope);
        if (group is null)
        {
            var title = ScopeOrder.First(item => item.Scope == reference.Scope).Title;
            group = new VariableTemplateGroup { Scope = reference.Scope, Title = title };
            Groups.Add(group);
        }
        if (group.Items.Any(item => item.Reference == reference)) return;
        group.Items.Add(new VariableTemplateItem { Reference = reference });
        group.RebuildFilter(SearchText);
        OnPropertyChanged(nameof(Groups));
    }

    public void ApplyFilter()
    {
        foreach (var group in Groups)
            group.RebuildFilter(SearchText);
        OnPropertyChanged(nameof(HasAnyMatch));
    }

    public bool HasAnyMatch => Groups.Any(group => group.HasMatches);
}
