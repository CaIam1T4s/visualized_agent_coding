using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VisualizedAgentCoding.Domain.Projects;

namespace VisualizedAgentCoding.Application.Projects;

public sealed class RecentProjectsService
{
    private readonly IRecentProjectsStore store;
    private readonly IProjectValidityChecker projectValidityChecker;

    public RecentProjectsService(IRecentProjectsStore store, IProjectValidityChecker projectValidityChecker)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(projectValidityChecker);
        this.store = store;
        this.projectValidityChecker = projectValidityChecker;
    }

    public IReadOnlyList<RecentProjectEntry> LoadValidProjects()
    {
        IReadOnlyList<RecentProjectEntry> loaded;
        try
        {
            loaded = store.Load();
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }

        var valid = Normalize(loaded.Where(item =>
            item is not null && projectValidityChecker.IsProject(item.Path)));
        if (!loaded.SequenceEqual(valid))
            TrySave(valid);
        return valid;
    }

    public IReadOnlyList<RecentProjectEntry> RecordOpened(
        ProjectWorkspace project,
        DateTimeOffset? openedAt = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        var entry = new RecentProjectEntry(project.Name, project.RootPath, openedAt ?? DateTimeOffset.Now);
        IReadOnlyList<RecentProjectEntry> existing;
        try
        {
            existing = store.Load();
        }
        catch (IOException)
        {
            existing = [];
        }
        catch (UnauthorizedAccessException)
        {
            existing = [];
        }

        var updated = existing
            .Where(item => item is not null &&
                !string.Equals(item.Path, project.RootPath, StringComparison.OrdinalIgnoreCase))
            .Append(entry);
        var valid = Normalize(updated.Where(item => projectValidityChecker.IsProject(item.Path)));
        TrySave(valid);
        return valid;
    }

    private static RecentProjectEntry[] Normalize(IEnumerable<RecentProjectEntry> projects) => projects
        .Where(item => item is not null && !string.IsNullOrWhiteSpace(item.Path))
        .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
        .Select(group => group.OrderByDescending(item => item.OpenedAt).First())
        .OrderByDescending(item => item.OpenedAt)
        .ToArray();

    private void TrySave(IEnumerable<RecentProjectEntry> projects)
    {
        try
        {
            store.Save(projects);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
