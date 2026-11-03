using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using VisualizedAgentCoding.Application.Editor.ObjectScripts;
using VisualizedAgentCoding.Domain.Projects;

namespace happy_agent_coding.ViewModels;

/// <summary>One event/function/class canvas shown by the class definition page.</summary>
public sealed class BlueprintCanvasEntry
{
    public required string RegionId { get; init; }
    public required string Title { get; init; }
    public required string Kind { get; init; }
    public string? EventName { get; init; }
    public bool IsStart => Kind == "start";
    public bool IsMembers => Kind == "members";
}

/// <summary>
/// Navigation for one object script: class members, methods and events. Each entry
/// opens its own canvas; the page itself is not an execution chain.
/// </summary>
public sealed partial class BlueprintClassPageModel : ObservableObject
{
    public ObservableCollection<BlueprintCanvasEntry> Canvases { get; } = [];

    [ObservableProperty] private string objectName = string.Empty;
    [ObservableProperty] private string className = string.Empty;
    [ObservableProperty] private BlueprintCanvasEntry? selectedCanvas;

    public void SetRegions(string objectName, string className, IEnumerable<ObjectScriptRegion> regions,
        IReadOnlyDictionary<string, string>? backdropNames, string? selectedRegionId)
    {
        ObjectName = objectName;
        ClassName = className;
        Canvases.Clear();
        foreach (var region in regions)
        {
            Canvases.Add(new BlueprintCanvasEntry
            {
                RegionId = region.Id,
                Kind = region.Kind,
                EventName = region.EventName,
                Title = Title(region, backdropNames),
            });
        }
        var selected = Canvases.FirstOrDefault(item => item.RegionId == selectedRegionId)
            ?? Canvases.FirstOrDefault(item => item.IsStart)
            ?? Canvases.FirstOrDefault();
        SelectedCanvas = selected;
    }

    public static string Title(ObjectScriptRegion region, IReadOnlyDictionary<string, string>? backdropNames) =>
        region.Kind switch
        {
            "members" => "类定义",
            "start" => "程序启动时运行",
            "initialize" => "初始化",
            "event" => MessageEvents.Title(region.EventName ?? region.Id, backdropNames),
            _ => region.Id,
        };
}
