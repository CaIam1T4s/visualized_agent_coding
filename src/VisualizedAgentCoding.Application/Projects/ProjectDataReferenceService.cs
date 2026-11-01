using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using VisualizedAgentCoding.Application.Ports;
using VisualizedAgentCoding.Domain.Projects;

namespace VisualizedAgentCoding.Application.Projects;

public sealed record ProjectDataReference(string Path, int Line, int Column, string Api, string Name)
{
    public string Display => $"{Path} 第 {Line} 行，第 {Column} 列 · {Api}";
}

/// <summary>Changed source returned before it is written, so an open unsaved editor can stay open.</summary>
public sealed record ProjectDataRewrite(string Path, string Before, string After);

/// <summary>A single literal-name span in a source buffer, used for undoable editor edits.</summary>
public sealed record ProjectDataTextEdit(int Offset, int Length, string Replacement);

/// <summary>Literal-name reference lookup for the project data API. Dynamic names are not discoverable.</summary>
public sealed class ProjectDataReferenceService(IProjectStore store)
{
    private static readonly Regex Calls = new(
        @"(?<![\w.])(?<api>get_variable|set_variable|add_to_variable|change_variable|list_append|list_remove|list_clear|list_length|list_get|list_set|list_values)\s*\(\s*(?<quote>['""])(?<name>[^'""\r\n]*)\k<quote>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public IReadOnlyList<ProjectDataReference> Find(ProjectWorkspace project, string name,
        IReadOnlyDictionary<string, string>? overlays = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        _ = ProjectDataRules.ValidateName(name);
        var result = new List<ProjectDataReference>();
        foreach (var (path, source) in Sources(project, overlays))
        {
            foreach (Match match in Calls.Matches(source))
            {
                if (!string.Equals(match.Groups["name"].Value, name, StringComparison.Ordinal)) continue;
                var offset = match.Index;
                var line = 1;
                var lineStart = 0;
                for (var index = 0; index < offset; index++)
                {
                    if (source[index] != '\n') continue;
                    line++;
                    lineStart = index + 1;
                }
                result.Add(new(path, line, offset - lineStart + 1, match.Groups["api"].Value, name));
            }
        }
        return result;
    }

    /// <summary>Finds only the literal-name spans so an open editor can apply them as undoable edits.</summary>
    public static IReadOnlyList<ProjectDataTextEdit> FindLiteralEdits(string source, string oldName, string newName)
    {
        var edits = new List<ProjectDataTextEdit>();
        foreach (Match match in Calls.Matches(source))
        {
            if (!string.Equals(match.Groups["name"].Value, oldName, StringComparison.Ordinal)) continue;
            var group = match.Groups["name"];
            edits.Add(new(group.Index, group.Length, Escape(newName)));
        }
        edits.Sort((left, right) => right.Offset.CompareTo(left.Offset));
        return edits;
    }

    /// <summary>Returns source changes for literal first arguments without writing them.</summary>
    public static string RewriteSource(string source, string oldName, string newName)
    {
        var edits = FindLiteralEdits(source, oldName, newName);
        if (edits.Count == 0) return source;
        var builder = new System.Text.StringBuilder(source);
        foreach (var edit in edits)
        {
            builder.Remove(edit.Offset, edit.Length);
            builder.Insert(edit.Offset, edit.Replacement);
        }
        return builder.ToString();
    }

    public IReadOnlyList<ProjectDataRewrite> Rewrite(ProjectWorkspace project, string oldName, string newName,
        IReadOnlyDictionary<string, string>? overlays = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        _ = ProjectDataRules.ValidateName(oldName);
        _ = ProjectDataRules.ValidateName(newName);
        var changed = new List<ProjectDataRewrite>();
        foreach (var (path, source) in Sources(project, overlays))
        {
            var updated = RewriteSource(source, oldName, newName);
            if (updated != source) changed.Add(new(path, source, updated));
        }
        return changed;
    }

    private IEnumerable<(string Path, string Source)> Sources(ProjectWorkspace project,
        IReadOnlyDictionary<string, string>? overlays)
    {
        foreach (var path in store.ListScriptFiles(project.RootPath))
        {
            if (string.Equals(path, project.Manifest.MainScript, StringComparison.OrdinalIgnoreCase)) continue;
            var source = overlays is not null && overlays.TryGetValue(path, out var overlay)
                ? overlay : store.ReadScript(project.RootPath, path);
            yield return (path, source);
        }
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\"", "\\\"");
}
