using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace VisualizedAgentCoding.Domain.Projects;

/// <summary>
/// Validates a project manifest and its stable script/event index.
/// IO stays outside the domain, so callers pass in how script existence is answered.
/// </summary>
public static class ProjectManifestValidator
{
    /// <param name="manifest">Manifest loaded from disk.</param>
    /// <param name="scriptExists">Answers whether a project-relative script path exists.</param>
    /// <exception cref="ProjectFormatException">The manifest or its index is not usable.</exception>
    public static void Validate(ProjectManifest manifest, Func<string, bool> scriptExists)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(scriptExists);

        if (manifest.SchemaVersion != ProjectLayout.CurrentSchemaVersion)
            throw new ProjectFormatException(
                $"Unsupported project schema version: {manifest.SchemaVersion}. Expected {ProjectLayout.CurrentSchemaVersion}.");

        if (string.IsNullOrWhiteSpace(manifest.Name))
            throw new ProjectFormatException($"{ProjectLayout.ManifestFileName} does not declare a project name.");

        if (!IsStableId(manifest.ProjectId))
            throw new ProjectFormatException($"{ProjectLayout.ManifestFileName} does not declare a stable project id.");

        if (!string.Equals(manifest.MainScript, ProjectLayout.MainScriptRelativePath, StringComparison.Ordinal))
            throw new ProjectFormatException($"{ProjectLayout.ManifestFileName} declares an unsupported entry script.");

        if (!IsStableId(manifest.MainScriptId))
            throw new ProjectFormatException($"{ProjectLayout.ManifestFileName} does not declare a stable entry script id.");

        if (!scriptExists(manifest.MainScript))
            throw new ProjectFormatException($"The project is missing its entry script: {manifest.MainScript}.");

        ValidateScripts(manifest, scriptExists);
        ValidateEventHandlers(manifest);
        foreach (var path in manifest.PendingScriptDeletions ?? throw new ProjectFormatException("Missing pending script deletion list."))
        {
            var normalized = NormalizePath(path);
            if (string.IsNullOrWhiteSpace(normalized) || Path.IsPathRooted(path) || ProjectLayout.KindOfScript(normalized) is null
                || !normalized.EndsWith(".py", StringComparison.OrdinalIgnoreCase)
                || normalized.Split('/').Any(part => part is ".." or "." or "")
                || normalized.Contains(':')
                || manifest.Scripts.Any(script => string.Equals(NormalizePath(script.Path), normalized, StringComparison.OrdinalIgnoreCase)))
                throw new ProjectFormatException($"Invalid pending script deletion: {path}");
        }
    }

    /// <summary>Returns scripts present in script directories but absent from the index.</summary>
    public static IReadOnlyList<string> FindUndeclaredScripts(
        ProjectManifest manifest,
        IEnumerable<string> scriptFilesOnDisk)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(scriptFilesOnDisk);

        var declared = manifest.Scripts
            .Select(script => NormalizePath(script.Path))
            .Append(NormalizePath(manifest.MainScript))
            .Concat(manifest.PendingScriptDeletions.Select(NormalizePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return scriptFilesOnDisk
            .Select(NormalizePath)
            .Where(path => !declared.Contains(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void ValidateScripts(ProjectManifest manifest, Func<string, bool> scriptExists)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal) { manifest.MainScriptId };
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { NormalizePath(manifest.MainScript) };
        var objectIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var script in manifest.Scripts ?? [])
        {
            if (script is null)
                throw new ProjectFormatException($"The project script index contains an empty entry.");

            if (!IsStableId(script.Id))
                throw new ProjectFormatException($"A project script has no stable id: {script.Path}.");
            if (!ids.Add(script.Id))
                throw new ProjectFormatException($"Duplicate project script id: {script.Id}.");

            var path = NormalizePath(script.Path);
            if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
                throw new ProjectFormatException($"A project script path is not a Python file: {script.Path}.");
            if (!paths.Add(path))
                throw new ProjectFormatException($"Duplicate project script path: {script.Path}.");

            var kind = ProjectLayout.KindOfScript(path);
            if (kind is null)
                throw new ProjectFormatException($"A project script is outside the script directories: {script.Path}.");
            if (kind != script.Kind)
                throw new ProjectFormatException(
                    $"Project script {script.Path} is declared as {script.Kind} but lives in {ProjectLayout.DirectoryFor(kind.Value)}.");

            if (kind == ProjectScriptKind.User)
            {
                if (script.ObjectId is not null)
                    throw new ProjectFormatException($"User script {script.Path} must not declare an object id.");
            }
            else
            {
                if (!IsStableId(script.ObjectId))
                    throw new ProjectFormatException($"Script {script.Path} does not declare a stable object id.");
                if (!objectIds.Add(script.ObjectId!))
                    throw new ProjectFormatException($"Duplicate object id: {script.ObjectId}.");
            }

            if (!scriptExists(path))
                throw new ProjectFormatException($"The project script declared in the index is missing: {script.Path}.");
        }
    }

    private static void ValidateEventHandlers(ProjectManifest manifest)
    {
        var scriptIds = new HashSet<string>(StringComparer.Ordinal) { manifest.MainScriptId };
        foreach (var script in manifest.Scripts ?? [])
            if (script is not null && !string.IsNullOrWhiteSpace(script.Id))
                scriptIds.Add(script.Id);

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var handler in manifest.EventHandlers ?? [])
        {
            if (handler is null)
                throw new ProjectFormatException("The event handler index contains an empty entry.");

            if (!scriptIds.Contains(handler.ScriptId))
                throw new ProjectFormatException($"Event handler references an unknown script id: {handler.ScriptId}.");

            if (!ProjectEventKinds.IsKnown(handler.Event))
                throw new ProjectFormatException($"Event handler declares an unknown event: {handler.Event}.");

            var argument = NormalizeArgument(handler.Argument);
            if (ProjectEventKinds.RequiresArgument(handler.Event) && argument is null)
                throw new ProjectFormatException($"Event handler for {handler.Event} needs an argument.");
            if (!ProjectEventKinds.RequiresArgument(handler.Event) && argument is not null)
                throw new ProjectFormatException($"Event handler for {handler.Event} must not declare an argument.");

            if (string.IsNullOrWhiteSpace(handler.Handler))
                throw new ProjectFormatException($"Event handler for {handler.Event} does not declare a Python function.");

            if (handler.Event == ProjectEventKinds.BackdropSwitch
                && (!BackdropSwitchEvents.IsValidTarget(argument)
                    || !(manifest.Scripts ?? []).Any(script => script.Kind == ProjectScriptKind.Backdrop && script.ObjectId == argument)))
                throw new ProjectFormatException($"Event handler for {handler.Event} targets an unknown backdrop.");
            if (handler.Event == ProjectEventKinds.CloneStart
                && !(manifest.Scripts ?? []).Any(script => script.Id == handler.ScriptId && script.Kind == ProjectScriptKind.Object))
                throw new ProjectFormatException("Clone-start handlers must belong to an object script.");

            var key = HandlerKey(handler.ScriptId, handler.Event, argument);
            if (!keys.Add(key))
                throw new ProjectFormatException($"Duplicate event handler: {key}.");
        }
    }

    /// <summary>Stable composite key for de-duplicating handler bindings.</summary>
    public static string HandlerKey(string scriptId, string eventKind, string? argument) =>
        $"{scriptId}|{eventKind}|{NormalizeArgument(argument) ?? string.Empty}";

    public static string? NormalizeArgument(string? argument) =>
        string.IsNullOrWhiteSpace(argument) ? null : argument.Trim();

    public static string NormalizePath(string? path) =>
        (path ?? string.Empty).Replace('\\', '/').Trim().TrimStart('/');

    private static bool IsStableId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsWhiteSpace);
}
