using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using VisualizedAgentCoding.Domain.Projects;

namespace happy_agent_coding.ViewModels;

public sealed partial class VariableDraft : ObservableObject
{
    public VariableDraft(string id, string name, string type, string value)
    {
        Id = id;
        Name = name;
        TypeIndex = type switch { ProjectDataTypes.Text => 1, ProjectDataTypes.Boolean => 2, _ => 0 };
        Value = value;
    }

    public string Id { get; }
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private int typeIndex;
    [ObservableProperty] private string value = string.Empty;
    public string Type => TypeIndex switch
    {
        1 => ProjectDataTypes.Text,
        2 => ProjectDataTypes.Boolean,
        _ => ProjectDataTypes.Number,
    };
}

public sealed partial class ListDraft : ObservableObject
{
    public ListDraft(string id, string name, IReadOnlyList<string> values)
    {
        Id = id;
        Name = name;
        ValuesText = string.Join('\n', values);
    }

    public string Id { get; }
    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private string valuesText = string.Empty;

    public IReadOnlyList<string> Values()
    {
        var text = ValuesText.Replace("\r\n", "\n");
        if (text.Length == 0) return [];
        var values = text.Split('\n');
        return values.Length > 0 && values[^1].Length == 0 ? values[..^1] : values;
    }
}

public sealed partial class VariableManagerModel : ObservableObject
{
    public ObservableCollection<VariableDraft> Variables { get; } = [];
    public ObservableCollection<ListDraft> Lists { get; } = [];
    [ObservableProperty] private VariableDraft? selectedVariable;
    [ObservableProperty] private ListDraft? selectedList;
    [ObservableProperty] private int tabIndex;

    public VariableManagerModel(IReadOnlyList<ProjectVariable> variables, IReadOnlyList<ProjectListDefinition> lists)
    {
        foreach (var variable in variables)
            Variables.Add(new(variable.Id, variable.Name, variable.Type, variable.Value));
        foreach (var list in lists)
            Lists.Add(new(list.Id, list.Name, list.Values));
        SelectedVariable = Variables.FirstOrDefault();
        SelectedList = Lists.FirstOrDefault();
    }

    public void AddVariable()
    {
        var item = new VariableDraft("var-" + Guid.NewGuid().ToString("N"), "新的变量", ProjectDataTypes.Number, "0");
        Variables.Add(item);
        SelectedVariable = item;
        SelectedList = null;
        TabIndex = 0;
    }

    public void AddList()
    {
        var item = new ListDraft("list-" + Guid.NewGuid().ToString("N"), "新的列表", []);
        Lists.Add(item);
        SelectedList = item;
        SelectedVariable = null;
        TabIndex = 1;
    }

    public void RemoveSelected()
    {
        if (TabIndex == 0 && SelectedVariable is { } variable)
        {
            Variables.Remove(variable);
            SelectedVariable = Variables.FirstOrDefault();
        }
        else if (TabIndex == 1 && SelectedList is { } list)
        {
            Lists.Remove(list);
            SelectedList = Lists.FirstOrDefault();
        }
    }

    public void EnsureSelectionForTab()
    {
        if (TabIndex == 0 && SelectedVariable is null) SelectedVariable = Variables.FirstOrDefault();
        if (TabIndex == 1 && SelectedList is null) SelectedList = Lists.FirstOrDefault();
    }

    public IReadOnlyList<ProjectVariable> ToVariables() => Variables
        .Select(item => new ProjectVariable(item.Id, item.Name.Trim(), item.Type,
            ProjectDataTypes.NormalizeValue(item.Type, item.Value)))
        .ToArray();

    public IReadOnlyList<ProjectListDefinition> ToLists() => Lists
        .Select(item => new ProjectListDefinition(item.Id, item.Name.Trim(), item.Values().ToList()))
        .ToArray();
}
