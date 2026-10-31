using System.Text.RegularExpressions;

namespace VisualizedAgentCoding.Domain.Projects;

/// <summary>Persistent region identity scoped to a script, independent of display labels.</summary>
public sealed record ObjectScriptRegionDefinition(string Id, string Kind, string? EventName = null)
{
    public static void Validate(IReadOnlyList<ObjectScriptRegionDefinition> regions)
    {
        if (regions.Count < 2 || regions[0].Kind != "members"
            || regions.Count(r => r.Kind == "members") != 1 || regions.Count(r => r.Kind == "start") != 1
            || regions.Count(r => r.Kind == "initialize") > 1
            || regions.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() != regions.Count)
            throw new ArgumentException("Regions require unique IDs, members first, one start and at most one initialize.");
        var events = new HashSet<string>(StringComparer.Ordinal);
        foreach (var region in regions)
        {
            if (MessageEvents.Name(region.EventName) is { } message) MessageEvents.ValidateName(message);
            if (KeyEvents.Name(region.EventName) is { } key) KeyEvents.ValidateName(key);
            if (region.Id is null || !Regex.IsMatch(region.Id, "\\A[a-z][a-z0-9_]{0,63}\\z")
                || region.Kind is not ("members" or "initialize" or "start" or "event")
                || (region.Kind == "event" ? string.IsNullOrWhiteSpace(region.EventName)
                    || region.EventName.Length > 256 || !events.Add(region.EventName) : region.EventName is not null))
                throw new ArgumentException("Invalid region ID, kind or event identity.");
        }
    }

    public string? HandlerName => Kind switch
    {
        "members" => null,
        "initialize" => "initialize",
        "start" => "_vac_on_start",
        "event" => "_vac_event_" + Id,
        _ => throw new ArgumentException("Unknown region kind."),
    };
}
