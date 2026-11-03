using System;
using System.Collections.Generic;
using System.Linq;
using VisualizedAgentCoding.Application.Editor;
using VisualizedAgentCoding.Domain.Blueprint;

namespace VisualizedAgentCoding.Application.Python;

/// <summary>A preview item for renaming an instance/class attribute.</summary>
public sealed record AttributeRenameOccurrence(int Line, string Before, string After);

public sealed record AttributeRenamePreview(
    string OldName,
    string NewName,
    int Count,
    IReadOnlyList<AttributeRenameOccurrence> Occurrences)
{
    public bool HasChanges => Count > 0;
}

/// <summary>A call template generated from a managed import.</summary>
public sealed record BlueprintImportCallTemplate(
    string Module,
    string? Name,
    string? Alias,
    bool IsClass,
    string DisplayName,
    string SearchText,
    string GeneratedCall);

/// <summary>
/// Indexes class members and variables from an already-projected document. It does not
/// execute user code; import signatures are limited to statically known bindings.
/// </summary>
public static class BlueprintMemberIndex
{
    public static IReadOnlyList<VariableRef> CollectVariables(
        BlueprintDocument document,
        string? className,
        IEnumerable<ProjectVariableRef>? projectVariables = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        var result = new List<VariableRef>();
        var seen = new HashSet<VariableRef>();
        foreach (var node in Walk(document.Roots))
        {
            if (node.Type is "data.set_attribute" or "data.add_attribute")
            {
                var name = node.Arguments.GetValueOrDefault("name") ?? node.Arguments.GetValueOrDefault("target");
                if (string.IsNullOrWhiteSpace(name)) continue;
                Add(new VariableRef(NodeScope.InstanceAttribute, name, className));
            }
            else if (node.Type is "data.set_class_attribute" or "data.add_class_attribute")
            {
                var name = node.Arguments.GetValueOrDefault("name") ?? node.Arguments.GetValueOrDefault("target");
                var owner = node.Arguments.GetValueOrDefault("owner") ?? className;
                if (string.IsNullOrWhiteSpace(name)) continue;
                Add(new VariableRef(NodeScope.ClassAttribute, name, owner));
            }
            else if (node.Type == "control.repeat_range" || node.Type == "control.for_each")
            {
                var name = node.Arguments.GetValueOrDefault("name");
                if (!string.IsNullOrWhiteSpace(name))
                    Add(new VariableRef(NodeScope.LoopVariable, name, OwnerMethod: null));
            }
            else if (node.Type == "def")
            {
                var parameters = node.Arguments.GetValueOrDefault("parameters") ?? "";
                foreach (var parameter in parameters.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (parameter == "self") continue;
                    Add(new VariableRef(NodeScope.Parameter, parameter.TrimStart('*'), OwnerMethod: node.Arguments.GetValueOrDefault("name")));
                }
            }
        }
        foreach (var project in projectVariables ?? [])
            Add(new VariableRef(NodeScope.ProjectVariable, project.Name) { ValueText = project.Value });
        return result;

        void Add(VariableRef reference)
        {
            if (seen.Add(reference)) result.Add(reference);
        }
    }

    public static IReadOnlyList<BlueprintImportCallTemplate> ImportTemplates(IEnumerable<ScriptImport> imports)
    {
        var result = new List<BlueprintImportCallTemplate>();
        foreach (var import in imports)
        {
            var name = import.Name ?? import.Module.Split('.').Last();
            var isClass = name.Length > 0 && char.IsUpper(name[0]);
            var display = import.Alias ?? name;
            result.Add(new BlueprintImportCallTemplate(import.Module, import.Name, import.Alias, isClass,
                display,
                $"{import.Module} {name} {display}",
                isClass ? $"{display}(...)" : $"{display}(...)"));
        }
        return result;
    }

    public static AttributeRenamePreview PreviewAttributeRename(
        string source, string className, string oldName, string newName)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(oldName);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        var occurrences = new List<AttributeRenameOccurrence>();
        var lines = source.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].TrimEnd('\r');
            var instance = $"self.{oldName}";
            var classAccess = $"{className}.{oldName}";
            if (line.Contains(instance, StringComparison.Ordinal) || line.Contains(classAccess, StringComparison.Ordinal))
            {
                occurrences.Add(new AttributeRenameOccurrence(index + 1, line,
                    line.Replace(instance, $"self.{newName}", StringComparison.Ordinal)
                        .Replace(classAccess, $"{className}.{newName}", StringComparison.Ordinal)));
            }
        }
        return new AttributeRenamePreview(oldName, newName, occurrences.Count, occurrences);
    }

    private static IEnumerable<BlueprintNode> Walk(IEnumerable<BlueprintNode> roots)
    {
        foreach (var root in roots)
        {
            yield return root;
            foreach (var child in Walk(root.AllChildren)) yield return child;
        }
    }
}

/// <summary>Minimal project variable input for member indexing.</summary>
public sealed record ProjectVariableRef(string Name, string Value);
