using VisualizedAgentCoding.Domain.Projects;

namespace VisualizedAgentCoding.Application.Editor.ObjectScripts;

/// <summary>Explicit structural edits; ordinary typing cannot change the system shell.</summary>
public static class ObjectScriptFeatures
{
    public static ObjectScriptDocument AddInitialize(ObjectScriptDocument document)
    {
        if (document.TemplateVersion is not (2 or 3) || !document.HasRegions)
            throw new ArgumentException("Confirmed v2 document required.");
        if (document.Regions!.Any(r => r.Kind == "initialize")) return document;
        var id = "initialize";
        for (var i = 2; document.Regions!.Any(r => r.Id == id); i++) id = "initialize_" + i;
        var nl = document.FullSource.Contains("\r\n") ? "\r\n" : "\n";
        var prefix = document.FullSource + (document.FullSource.EndsWith('\n') ? "" : nl) + nl
            + "    def initialize(self):" + nl
            + "        # vac:region-begin {\"id\":\"" + id + "\",\"kind\":\"initialize\"}" + nl;
        var body = "        " + ProjectScriptTemplates.UserPlaceholderStatement + nl;
        return document with { FullSource = prefix + body + "        # vac:region-end " + id + nl,
            Regions = [.. document.Regions!, new(id, "initialize", new(prefix.Length, body.Length), 8)] };
    }
}
