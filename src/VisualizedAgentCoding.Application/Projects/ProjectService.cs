using System;
using System.IO;
using System.Linq;
using System.Text;
using VisualizedAgentCoding.Application.Ports;
using VisualizedAgentCoding.Domain.Projects;

namespace VisualizedAgentCoding.Application.Projects;

/// <summary>Result of adding a script to a project: the refreshed workspace plus the new entry.</summary>
public sealed record ProjectScriptCreation(ProjectWorkspace Project, ProjectScriptEntry Script);

/// <summary>Use cases for creating, opening, saving and indexing project workspaces.</summary>
public sealed class ProjectService : IProjectValidityChecker
{
    private const string DefaultMainScript =
        "def main():\n    pass\n\n\nif __name__ == \"__main__\":\n    main()\n";

    private readonly IProjectStore store;

    public ProjectService(IProjectStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        this.store = store;
    }

    /// <summary>Creates a project in an empty folder and returns the opened workspace.</summary>
    /// <exception cref="ProjectFormatException">The folder cannot host a new project.</exception>
    public ProjectWorkspace Create(string rootPath, string? name = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        var root = Path.GetFullPath(rootPath);
        if (File.Exists(root))
            throw new ProjectFormatException("The selected path is a file, not a project folder.");

        var projectName = string.IsNullOrWhiteSpace(name) ? new DirectoryInfo(root).Name : name.Trim();
        if (string.IsNullOrWhiteSpace(projectName))
            throw new ProjectFormatException("The project name cannot be empty.");

        var manifest = new ProjectManifest
        {
            SchemaVersion = ProjectLayout.CurrentSchemaVersion,
            ProjectId = NewId("project"),
            Name = projectName,
            MainScript = ProjectLayout.MainScriptRelativePath,
            MainScriptId = NewId("script"),
        };

        store.CreateProject(root, manifest, DefaultMainScript);
        return new ProjectWorkspace(root, manifest);
    }

    /// <summary>Opens an existing project folder, rejecting anything that is not a valid project.</summary>
    /// <exception cref="ProjectFormatException">The folder is not a valid project workspace.</exception>
    public ProjectWorkspace Open(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        var root = Path.GetFullPath(rootPath);
        if (!Directory.Exists(root))
            throw new ProjectFormatException("The selected folder does not exist.");

        var stored = store.ReadManifest(root);
        var manifest = ProjectManifestMigration.Upgrade(stored);
        ProjectManifestValidator.Validate(manifest, path => store.ScriptExists(root, path));
        store.EnsureLayout(root);
        manifest = RetryPendingDeletions(root, manifest);

        var adopted = AdoptUndeclaredScripts(root, manifest) ?? manifest;
        ProjectManifestValidator.Validate(adopted, path => store.ScriptExists(root, path));

        if (!ReferenceEquals(adopted, stored))
            store.WriteManifest(root, adopted);

        return new ProjectWorkspace(root, adopted);
    }

    /// <summary>Checks whether a folder is a valid project without mutating it.</summary>
    public bool IsProject(string? rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
            return false;
        try
        {
            var root = Path.GetFullPath(rootPath);
            if (!Directory.Exists(root))
                return false;

            var manifest = ProjectManifestMigration.Upgrade(store.ReadManifest(root));
            ProjectManifestValidator.Validate(manifest, path => store.ScriptExists(root, path));
            return true;
        }
        catch (Exception ex) when (ex is ProjectFormatException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public string LoadMainScript(ProjectWorkspace project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return store.ReadMainScript(project.RootPath);
    }

    public void SaveMainScript(ProjectWorkspace project, string content)
    {
        ArgumentNullException.ThrowIfNull(project);
        store.WriteMainScript(project.RootPath, content);
    }

    public string LoadScript(ProjectWorkspace project, ProjectScriptEntry script)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(script);
        return store.ReadScript(project.RootPath, script.Path);
    }

    public void SaveScript(ProjectWorkspace project, ProjectScriptEntry script, string content)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(script);
        store.WriteScript(project.RootPath, script.Path, content);
    }

    /// <summary>Internal file/index commit primitive. Public callers must use ScriptRenameService to preserve references.</summary>
    /// <exception cref="ProjectOperationException">The script is the entry script, is not a user script, or the new name is unusable or taken.</exception>
    internal ProjectWorkspace CommitScriptFileRename(ProjectWorkspace project, string scriptId, string newName)
    {
        var target = ScriptRenameRules.ResolveFileTarget(project, scriptId, newName, store);
        var script = target.Script;
        var newPath = target.Path;
        if (string.Equals(newPath, script.Path, StringComparison.OrdinalIgnoreCase)) return project;
        project = WithPersistedPendingDeletions(project);

        var content = store.ReadScript(project.RootPath, script.Path);
        var renamed = script with { Path = newPath };
        var manifest = project.Manifest with
        {
            Scripts = project.Manifest.Scripts.Select(item => item.Id == scriptId ? renamed : item).ToList(),
            PendingScriptDeletions = [.. project.Manifest.PendingScriptDeletions, script.Path],
        };

        // Record cleanup intent before creating an unindexed file. A failed commit or
        // rollback must never cause Open to adopt this file as a new script.
        store.WriteManifest(project.RootPath, project.Manifest with
        {
            PendingScriptDeletions = [.. project.Manifest.PendingScriptDeletions, newPath],
        });
        try
        {
            store.WriteScript(project.RootPath, newPath, content);
            ProjectManifestValidator.Validate(manifest, path => store.ScriptExists(project.RootPath, path));
            store.WriteManifest(project.RootPath, manifest);
        }
        catch
        {
            TryDeleteScript(project.RootPath, newPath);
            throw;
        }

        TryDeleteScript(project.RootPath, script.Path);
        return project.WithManifest(manifest);
    }

    /// <summary>Removes a script from the index, drops its event bindings, then deletes its file.</summary>
    /// <exception cref="ProjectOperationException">The script is the entry script or is not indexed.</exception>
    public ProjectWorkspace DeleteScript(ProjectWorkspace project, string scriptId)
    {
        ArgumentNullException.ThrowIfNull(project);
        var script = ResolveIndexedScript(project, scriptId);
        project = WithPersistedPendingDeletions(project);
        var manifest = project.Manifest with
        {
            Scripts = project.Manifest.Scripts.Where(item => item.Id != scriptId).ToList(),
            EventHandlers = project.Manifest.EventHandlers.Where(handler => handler.ScriptId != scriptId).ToList(),
            PendingScriptDeletions = [.. project.Manifest.PendingScriptDeletions, script.Path],
        };

        ProjectManifestValidator.Validate(manifest, path => store.ScriptExists(project.RootPath, path));
        store.WriteManifest(project.RootPath, manifest);
        TryDeleteScript(project.RootPath, script.Path);
        return project.WithManifest(manifest);
    }

    /// <summary>Resolves an indexed script for rename or delete, refusing the entry script.</summary>
    private static ProjectScriptEntry ResolveIndexedScript(ProjectWorkspace project, string scriptId)
    {
        if (string.IsNullOrWhiteSpace(scriptId))
            throw new ProjectOperationException("缺少脚本 ID。");

        if (string.Equals(project.Manifest.MainScriptId, scriptId, StringComparison.Ordinal))
            throw new ProjectOperationException("入口脚本 main.py 不能重命名或删除。");

        return project.FindScript(scriptId)
            ?? throw new ProjectOperationException($"脚本索引中没有该脚本：{scriptId}");
    }

    /// <summary>Creates a script file and registers it in the project index with a stable id.</summary>
    /// <exception cref="ProjectOperationException">The name collides with an indexed script.</exception>
    public ProjectScriptCreation CreateScript(
        ProjectWorkspace project,
        ProjectScriptKind kind,
        string scriptName,
        string? objectId = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        project = WithPersistedPendingDeletions(project);
        var fileName = SanitizeScriptName(scriptName);
        if (fileName is null)
            throw new ProjectOperationException("脚本名只能包含字母、数字、下划线和连字符。");

        var relativePath = $"{ProjectLayout.DirectoryFor(kind)}/{fileName}.py";
        if (store.ScriptExists(project.RootPath, relativePath) || HasPath(project.Manifest, relativePath))
            throw new ProjectOperationException($"脚本已存在：{relativePath}");

        var scriptId = NewId("script");
        var stableObjectId = kind == ProjectScriptKind.User ? null : objectId ?? NewId("object");
        var content = ProjectScriptTemplates.Create(kind, ToClassName(fileName));

        store.WriteScript(project.RootPath, relativePath, content);

        var entry = new ProjectScriptEntry(scriptId, relativePath, kind, stableObjectId);
        var manifest = project.Manifest with
        {
            Scripts = [.. project.Manifest.Scripts, entry],
        };

        ProjectManifestValidator.Validate(manifest, path => store.ScriptExists(project.RootPath, path));
        store.WriteManifest(project.RootPath, manifest);

        return new ProjectScriptCreation(project.WithManifest(manifest), entry);
    }

    /// <summary>Binds a handler function to a visual event for one script.</summary>
    /// <exception cref="ProjectOperationException">The binding is invalid or already present.</exception>
    public ProjectWorkspace RegisterEventHandler(
        ProjectWorkspace project,
        string scriptId,
        EventHandlerRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(registration);

        var owner = ResolveScript(project, scriptId);
        return PersistHandlers(project, AddHandler(project.Manifest, owner, registration));
    }

    /// <summary>Removes a handler binding.</summary>
    public ProjectWorkspace UnregisterEventHandler(
        ProjectWorkspace project,
        string scriptId,
        string eventKind,
        string? argument = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        var key = ProjectManifestValidator.HandlerKey(scriptId, eventKind, argument);
        var remaining = project.Manifest.EventHandlers
            .Where(handler => !string.Equals(
                ProjectManifestValidator.HandlerKey(handler.ScriptId, handler.Event, handler.Argument),
                key,
                StringComparison.Ordinal))
            .ToList();

        if (remaining.Count == project.Manifest.EventHandlers.Count)
            return project;

        return PersistHandlers(project, project.Manifest with { EventHandlers = remaining });
    }

    /// <summary>Handlers the entry script should route for one event occurrence.</summary>
    public IReadOnlyList<ProjectEventHandler> MatchHandlers(
        ProjectWorkspace project,
        string eventKind,
        string? argument)
    {
        ArgumentNullException.ThrowIfNull(project);

        var normalized = ProjectManifestValidator.NormalizeArgument(argument);
        return project.Manifest.EventHandlers
            .Where(handler => string.Equals(handler.Event, eventKind, StringComparison.Ordinal)
                && string.Equals(
                    ProjectManifestValidator.NormalizeArgument(handler.Argument),
                    ProjectEventKinds.RequiresArgument(eventKind) ? normalized : null,
                    StringComparison.Ordinal))
            .ToArray();
    }

    private static ProjectManifest AddHandler(
        ProjectManifest manifest,
        ProjectScriptEntry owner,
        EventHandlerRegistration registration)
    {
        if (!ProjectEventKinds.IsKnown(registration.Event))
            throw new ProjectOperationException($"未知事件类型：{registration.Event}");

        var argument = ProjectManifestValidator.NormalizeArgument(registration.Argument);
        if (ProjectEventKinds.RequiresArgument(registration.Event) && argument is null)
            throw new ProjectOperationException($"事件 {registration.Event} 需要参数。");
        if (!ProjectEventKinds.RequiresArgument(registration.Event) && argument is not null)
            throw new ProjectOperationException($"事件 {registration.Event} 不接受参数。");

        var handlerName = registration.Handler?.Trim();
        if (string.IsNullOrWhiteSpace(handlerName) || !IsIdentifier(handlerName))
            throw new ProjectOperationException($"处理器必须是合法的 Python 函数名：{registration.Handler}");

        if (string.Equals(registration.Event, ProjectEventKinds.SpriteClick, StringComparison.Ordinal)
            && owner.Kind == ProjectScriptKind.User)
            throw new ProjectOperationException("角色点击处理器必须绑定到对象脚本。");
        if (string.Equals(registration.Event, ProjectEventKinds.StageClick, StringComparison.Ordinal)
            && owner.Kind == ProjectScriptKind.Object)
            throw new ProjectOperationException("舞台点击处理器不能绑定到对象脚本。");
        if (string.Equals(registration.Event, ProjectEventKinds.BackdropSwitch, StringComparison.Ordinal)
            && owner.Kind != ProjectScriptKind.Backdrop)
            throw new ProjectOperationException("背景切换处理器必须绑定到背景脚本。");

        var newHandler = new ProjectEventHandler(owner.Id, registration.Event, argument, handlerName);
        var key = ProjectManifestValidator.HandlerKey(owner.Id, registration.Event, argument);

        var existing = manifest.EventHandlers.FirstOrDefault(handler => string.Equals(
            ProjectManifestValidator.HandlerKey(handler.ScriptId, handler.Event, handler.Argument),
            key,
            StringComparison.Ordinal));

        if (existing is not null)
        {
            if (string.Equals(existing.Handler, handlerName, StringComparison.Ordinal))
                return manifest;

            throw new ProjectOperationException(
                $"该脚本已为事件 {registration.Event} 注册处理器 {existing.Handler}；请先移除再重新绑定。");
        }

        return manifest with { EventHandlers = [.. manifest.EventHandlers, newHandler] };
    }

    private ProjectWorkspace PersistHandlers(ProjectWorkspace project, ProjectManifest manifest)
    {
        manifest = manifest with { PendingScriptDeletions = WithPersistedPendingDeletions(project).Manifest.PendingScriptDeletions };
        ProjectManifestValidator.Validate(manifest, path => store.ScriptExists(project.RootPath, path));
        store.WriteManifest(project.RootPath, manifest);
        return project.WithManifest(manifest);
    }

    private static ProjectScriptEntry ResolveScript(ProjectWorkspace project, string scriptId)
    {
        if (string.IsNullOrWhiteSpace(scriptId))
            throw new ProjectOperationException("缺少脚本 ID。");

        if (string.Equals(project.Manifest.MainScriptId, scriptId, StringComparison.Ordinal))
            return new ProjectScriptEntry(scriptId, project.Manifest.MainScript, ProjectScriptKind.User);

        return project.FindScript(scriptId)
            ?? throw new ProjectOperationException($"脚本索引中没有该脚本：{scriptId}");
    }

    /// <summary>Registers <c>.py</c> files that exist on disk but are missing from the index.</summary>
    private ProjectManifest? AdoptUndeclaredScripts(string root, ProjectManifest manifest)
    {
        var undeclared = ProjectManifestValidator.FindUndeclaredScripts(
            manifest,
            store.ListScriptFiles(root));

        if (undeclared.Count == 0)
            return null;

        var scripts = manifest.Scripts.ToList();
        foreach (var path in undeclared)
        {
            var kind = ProjectLayout.KindOfScript(path)
                ?? throw new ProjectFormatException($"脚本位于受管理的脚本目录之外：{path}");

            var objectId = kind == ProjectScriptKind.User ? null : NewId("object");
            scripts.Add(new ProjectScriptEntry(NewId("script"), path, kind, objectId));
        }

        return manifest with { Scripts = scripts };
    }

    // Callers can still hold the old workspace after a failed rename.
    private ProjectWorkspace WithPersistedPendingDeletions(ProjectWorkspace project) =>
        project.WithManifest(project.Manifest with
        {
            PendingScriptDeletions = project.Manifest.PendingScriptDeletions
                .Concat(store.ReadManifest(project.RootPath).PendingScriptDeletions)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        });

    private ProjectManifest RetryPendingDeletions(string root, ProjectManifest manifest)
    {
        var remaining = manifest.PendingScriptDeletions.Where(path => !TryDeleteScript(root, path)).ToList();
        return remaining.Count == manifest.PendingScriptDeletions.Count
            ? manifest : manifest with { PendingScriptDeletions = remaining };
    }

    /// <summary>True if a deferred cleanup file still exists on disk.</summary>
    public bool HasPendingScriptCleanup(ProjectWorkspace project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return project.Manifest.PendingScriptDeletions.Any(path => store.ScriptExists(project.RootPath, path));
    }

    private bool TryDeleteScript(string root, string path)
    {
        try
        {
            store.DeleteScript(root, path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The persisted manifest retains cleanup intent until a later Open succeeds.
            return false;
        }
    }

    private static bool HasPath(ProjectManifest manifest, string relativePath) =>
        manifest.Scripts.Select(script => script.Path).Concat(manifest.PendingScriptDeletions).Any(path => string.Equals(
            ProjectManifestValidator.NormalizePath(path),
            relativePath,
            StringComparison.OrdinalIgnoreCase));

    /// <summary>Normalizes a user supplied name to a safe file stem, or null when unusable.</summary>
    public static string? SanitizeScriptName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var trimmed = name.Trim();
        if (trimmed.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^3];

        foreach (var character in trimmed)
            if (!char.IsLetterOrDigit(character) && character is not ('_' or '-'))
                return null;

        return trimmed.Length == 0 ? null : trimmed;
    }

    /// <summary>Turns a file stem into a Python class name.</summary>
    public static string ToClassName(string fileStem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileStem);
        var builder = new StringBuilder(fileStem.Length);
        var capitalize = true;
        foreach (var character in fileStem)
        {
            if (character is '_' or '-')
            {
                capitalize = true;
                continue;
            }

            builder.Append(capitalize ? char.ToUpperInvariant(character) : character);
            capitalize = false;
        }

        var name = builder.ToString();
        return name.Length == 0 ? "Script" : char.IsDigit(name[0]) ? "Script" + name : name;
    }

    private static bool IsIdentifier(string value) =>
        value.Length > 0
        && (char.IsLetter(value[0]) || value[0] == '_')
        && value.All(character => char.IsLetterOrDigit(character) || character == '_');

    private static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";
}