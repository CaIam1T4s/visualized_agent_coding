using System;
using System.Collections.Generic;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Domain.Projects;
using VisualizedAgentCoding.Infrastructure.Projects;

namespace happy_agent_coding.Services;

/// <summary>Composition root for project use cases used by the Avalonia layer.</summary>
public interface IProjectSession : IProjectValidityChecker
{
    ProjectWorkspace Create(string rootPath, string? name = null);

    ProjectWorkspace Open(string rootPath);

    string LoadMainScript(ProjectWorkspace project);

    void SaveMainScript(ProjectWorkspace project, string content);

    string LoadScript(ProjectWorkspace project, ProjectScriptEntry script);

    void SaveScript(ProjectWorkspace project, ProjectScriptEntry script, string content);

    ProjectWorkspace RenameScript(ProjectWorkspace project, string scriptId, string newName);

    ProjectWorkspace DeleteScript(ProjectWorkspace project, string scriptId);

    bool HasPendingScriptCleanup(ProjectWorkspace project);

    ProjectScriptCreation CreateScript(ProjectWorkspace project, ProjectScriptKind kind, string scriptName);

    ProjectWorkspace RegisterEventHandler(ProjectWorkspace project, string scriptId, EventHandlerRegistration registration);

    ProjectWorkspace UnregisterEventHandler(ProjectWorkspace project, string scriptId, string eventKind, string? argument = null);

    IReadOnlyList<ProjectEventHandler> MatchHandlers(ProjectWorkspace project, string eventKind, string? argument);
}

public sealed class ProjectSession : IProjectSession
{
    private readonly ProjectService projectService;

    public ProjectSession() : this(new ProjectService(new JsonProjectStore()))
    {
    }

    public ProjectSession(ProjectService projectService)
    {
        ArgumentNullException.ThrowIfNull(projectService);
        this.projectService = projectService;
    }

    public ProjectWorkspace Create(string rootPath, string? name = null) =>
        projectService.Create(rootPath, name);

    public ProjectWorkspace Open(string rootPath) => projectService.Open(rootPath);

    public bool IsProject(string? rootPath) => projectService.IsProject(rootPath);

    public string LoadMainScript(ProjectWorkspace project) => projectService.LoadMainScript(project);

    public void SaveMainScript(ProjectWorkspace project, string content) =>
        projectService.SaveMainScript(project, content);

    public string LoadScript(ProjectWorkspace project, ProjectScriptEntry script) =>
        projectService.LoadScript(project, script);

    public void SaveScript(ProjectWorkspace project, ProjectScriptEntry script, string content) =>
        projectService.SaveScript(project, script, content);

    public ProjectWorkspace RenameScript(ProjectWorkspace project, string scriptId, string newName) =>
        projectService.RenameScript(project, scriptId, newName);

    public ProjectWorkspace DeleteScript(ProjectWorkspace project, string scriptId) =>
        projectService.DeleteScript(project, scriptId);

    public bool HasPendingScriptCleanup(ProjectWorkspace project) => projectService.HasPendingScriptCleanup(project);

    public ProjectScriptCreation CreateScript(ProjectWorkspace project, ProjectScriptKind kind, string scriptName) =>
        projectService.CreateScript(project, kind, scriptName);

    public ProjectWorkspace RegisterEventHandler(
        ProjectWorkspace project,
        string scriptId,
        EventHandlerRegistration registration) =>
        projectService.RegisterEventHandler(project, scriptId, registration);

    public ProjectWorkspace UnregisterEventHandler(
        ProjectWorkspace project,
        string scriptId,
        string eventKind,
        string? argument = null) =>
        projectService.UnregisterEventHandler(project, scriptId, eventKind, argument);

    public IReadOnlyList<ProjectEventHandler> MatchHandlers(
        ProjectWorkspace project,
        string eventKind,
        string? argument) =>
        projectService.MatchHandlers(project, eventKind, argument);
}
