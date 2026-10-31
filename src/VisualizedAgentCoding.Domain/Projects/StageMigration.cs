using System;
using System.Collections.Generic;
using System.Linq;

namespace VisualizedAgentCoding.Domain.Projects;

/// <summary>Upgrades legacy scene files in memory without rewriting them on open.</summary>
public static class StageMigration
{
    public static StageState Upgrade(StageState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Version != 1) return state;

        var objects = new List<StageObjectState>(state.Objects?.Count ?? 0);
        foreach (var item in state.Objects ?? [])
        {
            var costumes = item.Costumes?.ToList() ?? [];
            if (costumes.Count == 0 && item.ImagePath is not null)
                costumes.Add(new("costume-" + Guid.NewGuid().ToString("N"), "造型 1", item.ImagePath, item.NormalizeImage));
            var initial = costumes.FirstOrDefault(costume => costume.Id == item.InitialCostumeId) ?? costumes.FirstOrDefault();
            objects.Add(item with
            {
                Costumes = costumes,
                InitialCostumeId = initial?.Id,
                ImagePath = initial?.ImagePath,
                NormalizeImage = initial?.NormalizeImage ?? false,
            });
        }
        return state with { Version = StageState.CurrentVersion, Objects = objects };
    }
}
