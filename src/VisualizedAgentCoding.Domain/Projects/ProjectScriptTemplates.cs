namespace VisualizedAgentCoding.Domain.Projects;

/// <summary>Minimal valid source for a newly created project script.</summary>
public static class ProjectScriptTemplates
{
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
        $"from runtime.element_base import ElementBase\n\n\nclass {className}(ElementBase):\n" +
        $"    \"\"\"User code for the {className} sprite.\"\"\"\n";

    private static string CreateBackdropScript(string className) =>
        $"from runtime.backdrop_base import BackdropBase\n\n\nclass {className}(BackdropBase):\n" +
        $"    \"\"\"User code for the {className} backdrop.\"\"\"\n";
}
