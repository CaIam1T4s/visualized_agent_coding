using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace VisualizedAgentCoding.Domain.Projects;

/// <summary>Supported initial value types for project variables.</summary>
public static class ProjectDataTypes
{
    public const string Number = "number";
    public const string Text = "text";
    public const string Boolean = "boolean";

    public static bool IsKnown(string? value) => value is Number or Text or Boolean;

    public static bool IsValidValue(string? type, string? value) => type switch
    {
        Number => value is not null
            && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            && double.IsFinite(number),
        Text => value is not null && value.Length <= 4096,
        Boolean => value is not null && bool.TryParse(value, out _),
        _ => false,
    };

    public static string NormalizeValue(string type, string value) => type switch
    {
        Boolean => bool.TryParse(value, out var boolean)
            ? boolean ? "true" : "false" : value.Trim(),
        _ => value.Trim(),
    };
}

public sealed record ProjectVariable(string Id, string Name, string Type, string Value);

/// <summary>A project-level list. First version stores textual values.</summary>
public sealed record ProjectListDefinition(string Id, string Name, List<string> Values);

public static class ProjectDataRules
{
    public const int MaximumNameLength = 64;
    public const int MaximumValueLength = 4096;
    public const int MaximumListItems = 1000;

    public static string ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name != name.Trim() || name.Length > MaximumNameLength
            || name.Any(char.IsControl))
            throw new ProjectFormatException($"变量/列表名称须为 1–{MaximumNameLength} 个字符，不能包含控制字符或首尾空白。");
        return name;
    }

    public static void Validate(IReadOnlyList<ProjectVariable>? variables, IReadOnlyList<ProjectListDefinition>? lists)
    {
        variables ??= [];
        lists ??= [];
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var variableNames = new HashSet<string>(StringComparer.Ordinal);
        var listNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var variable in variables)
        {
            if (variable is null || string.IsNullOrWhiteSpace(variable.Id) || !ids.Add(variable.Id)
                || !variableNames.Add(ValidateName(variable.Name))
                || !ProjectDataTypes.IsKnown(variable.Type)
                || !ProjectDataTypes.IsValidValue(variable.Type, variable.Value))
                throw new ProjectFormatException("项目变量身份、名称、类型或初始值无效。");
        }
        foreach (var list in lists)
        {
            if (list is null || string.IsNullOrWhiteSpace(list.Id) || !ids.Add(list.Id)
                || !listNames.Add(ValidateName(list.Name)) || list.Values is null
                || list.Values.Count > MaximumListItems
                || list.Values.Any(value => value is null || value.Length > MaximumValueLength))
                throw new ProjectFormatException("项目列表身份、名称或初始值无效。");
        }
    }
}
