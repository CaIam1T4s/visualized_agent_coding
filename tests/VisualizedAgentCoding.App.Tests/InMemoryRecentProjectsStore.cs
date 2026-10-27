using System.Collections.Generic;
using System.Linq;
using VisualizedAgentCoding.Application.Projects;

namespace VisualizedAgentCoding.App.Tests;

internal sealed class InMemoryRecentProjectsStore : IRecentProjectsStore
{
    public InMemoryRecentProjectsStore(IEnumerable<RecentProjectEntry>? projects = null)
    {
        Projects = projects?.ToArray() ?? [];
    }

    public IReadOnlyList<RecentProjectEntry> Projects { get; private set; }

    public IReadOnlyList<RecentProjectEntry> Load() => Projects;

    public void Save(IEnumerable<RecentProjectEntry> projects) => Projects = projects.ToArray();
}
