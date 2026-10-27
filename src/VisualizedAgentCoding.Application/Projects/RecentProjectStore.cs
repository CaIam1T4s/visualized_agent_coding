using System;
using System.Collections.Generic;

namespace VisualizedAgentCoding.Application.Projects;

public sealed record RecentProjectEntry(string Name, string Path, DateTimeOffset OpenedAt);

public interface IRecentProjectsStore
{
    IReadOnlyList<RecentProjectEntry> Load();

    void Save(IEnumerable<RecentProjectEntry> projects);
}
