using System;
using System.Collections.Generic;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Domain.Projects;
using VisualizedAgentCoding.Infrastructure.Projects;

namespace happy_agent_coding.Services;

/// <summary>Composition root for project use cases used by the Avalonia layer.</summary>
public interface IProjectSession
{
    ProjectWorkspace Create(string rootPath, string? name = null);

    ProjectWorkspace Open(string rootPath);

    bool IsProject(string? rootPath);

    string LoadMainScript(ProjectWorkspace project);

    void SaveMainScript(ProjectWorkspace project, string content);

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
