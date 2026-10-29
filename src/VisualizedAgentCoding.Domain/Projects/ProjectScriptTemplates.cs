namespace VisualizedAgentCoding.Domain.Projects;

/// <summary>Minimal valid source for a newly created project script.</summary>
public static class ProjectScriptTemplates
{
    public static string CreateWithImports(ProjectScriptKind kind, string className,
        IReadOnlyList<ObjectScriptRegionDefinition>? regions = null)
    {
        var source = CreateMultiRegion(kind, className, regions).Replace("# vac:template-version=2", "# vac:template-version=3");
        var position = source.IndexOf('\n', source.IndexOf('\n') + 1) + 1;
        return source.Insert(position, "# vac:imports-begin\n# vac:imports-end\n");
    }
    public const string UserPlaceholderStatement = "pass  # 请删除此行并编写代码";
    /// <summary>Version 2 source used for newly created object and backdrop scripts.</summary>
    public static string CreateMultiRegion(ProjectScriptKind kind, string className,
        IReadOnlyList<ObjectScriptRegionDefinition>? regions = null)
    {
        if (kind is not (ProjectScriptKind.Object or ProjectScriptKind.Backdrop))
            throw new ArgumentException("Only object and backdrop scripts use multi-region templates.", nameof(kind));
        if (string.IsNullOrEmpty(className) || !System.Text.RegularExpressions.Regex.IsMatch(className, "\\A[_\\p{L}][_\\p{L}\\p{Nd}\\p{Mn}\\p{Mc}]*\\z"))
            throw new ArgumentException("Invalid class name.", nameof(className));
        regions ??= [new("members", "members"), new("start", "start")];
        ObjectScriptRegionDefinition.Validate(regions);
        var baseName = kind == ProjectScriptKind.Object ? "ElementBase" : "BackdropBase";
        var module = kind == ProjectScriptKind.Object ? "element_base" : "backdrop_base";
        var source = new System.Text.StringBuilder($"# vac:template-version=2\nfrom runtime.{module} import {baseName}\n\n\nclass {className}({baseName}):\n    pass\n");
        foreach (var region in regions)
        {
            var metadata = new Dictionary<string, string> { ["id"] = region.Id, ["kind"] = region.Kind };
            if (region.EventName is not null) metadata["eventName"] = region.EventName;
            source.Append('\n');
            if (region.HandlerName is { } handler) source.Append($"    def {handler}(self):\n");
            var indent = region.Kind == "members" ? "    " : "        ";
            source.Append(indent).Append("# vac:region-begin ").Append(System.Text.Json.JsonSerializer.Serialize(metadata)).Append('\n');
            if (region.Kind != "members") source.Append(indent).Append(UserPlaceholderStatement).Append('\n');
            source.Append(indent).Append("# vac:region-end ").Append(region.Id).Append('\n');
        }
        return source.ToString();
    }

    /// <summary>Legacy v1 object templates and ordinary user scripts; retained for compatibility fixtures.</summary>
    public static string Create(ProjectScriptKind kind, string className) => kind switch
    {
        ProjectScriptKind.User => CreateUserScript(className),
        ProjectScriptKind.Object => CreateObjectScript(className),
        ProjectScriptKind.Backdrop => CreateBackdropScript(className),
        _ => throw new System.ArgumentOutOfRangeException(nameof(kind), kind, "Unknown script kind."),
    };

    private static string CreateUserScript(string className) =>
        $"\"\"\"{className}\"\"\"\n";

    private static string CreateObjectScript(string className) =>
        "# vac:template-version=1\n" +
        $"from runtime.element_base import ElementBase\n\n\nclass {className}(ElementBase):\n" +
        "    pass\n\n    # vac:user-code-begin\n    # vac:user-code-end\n";

    private static string CreateBackdropScript(string className) =>
        "# vac:template-version=1\n" +
        $"from runtime.backdrop_base import BackdropBase\n\n\nclass {className}(BackdropBase):\n" +
        "    pass\n\n    # vac:user-code-begin\n    # vac:user-code-end\n";
}
