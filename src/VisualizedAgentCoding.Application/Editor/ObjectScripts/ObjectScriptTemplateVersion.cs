namespace VisualizedAgentCoding.Application.Editor.ObjectScripts;

/// <summary>Cheap header hint for explorer decoration. Migration still requires full lexical validation.</summary>
public static class ObjectScriptTemplateVersion
{
    public const int Current = 2;
    public static int? ReadHeader(string source)
    {
        foreach (var raw in source.TrimStart('\uFEFF').Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            const string prefix = "# vac:template-version=";
            if (line.StartsWith(prefix, StringComparison.Ordinal))
                return int.TryParse(line[prefix.Length..], out var version) && version > 0 ? version : null;
            if (!line.StartsWith('#')) break;
        }
        return null;
    }
}
