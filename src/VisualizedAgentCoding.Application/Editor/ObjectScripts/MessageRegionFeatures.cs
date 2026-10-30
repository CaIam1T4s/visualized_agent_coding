using System.Text.Json;
using VisualizedAgentCoding.Domain.Projects;

namespace VisualizedAgentCoding.Application.Editor.ObjectScripts;

public static partial class ObjectScriptFeatures
{
    public static ObjectScriptDocument AddMessage(ObjectScriptDocument document, string name)
    {
        ValidateMessage(document, name);
        var id = "message_" + Guid.NewGuid().ToString("N");
        var nl = document.FullSource.Contains("\r\n") ? "\r\n" : "\n";
        var identity = MessageEvents.Prefix + name;
        var prefix = document.FullSource + (document.FullSource.EndsWith('\n') ? "" : nl) + nl
            + "    def _vac_event_" + id + "(self):" + nl
            + "        # vac:region-begin " + Metadata(id, identity) + nl;
        var body = "        " + ProjectScriptTemplates.UserPlaceholderStatement + nl;
        return document with { FullSource = prefix + body + "        # vac:region-end " + id + nl,
            Regions = [.. document.Regions!, new(id, "event", new(prefix.Length, body.Length), 8, identity)] };
    }

    public static ObjectScriptDocument RenameMessage(ObjectScriptDocument document, string id, string name)
    {
        ValidateMessage(document, name, id);
        var region = MessageRegion(document, id);
        if (MessageEvents.Name(region.EventName) == name) return document;
        var start = PreviousLine(document.FullSource, region.Span.Offset);
        var nl = document.FullSource.Contains("\r\n") ? "\r\n" : "\n";
        var identity = MessageEvents.Prefix + name;
        var text = "        # vac:region-begin " + Metadata(id, identity) + nl;
        var updated = ReplaceShell(document, start, region.Span.Offset - start, text);
        return updated with { Regions = updated.Regions!.Select(r => r.Id == id ? r with { EventName = identity } : r).ToArray() };
    }

    public static ObjectScriptDocument DeleteMessage(ObjectScriptDocument document, string id)
    {
        var region = MessageRegion(document, id);
        // Search only the verified shell between regions, never user body text.
        var index = Array.IndexOf(document.Regions!, region);
        var previous = index > 0 ? document.Regions![index - 1].Span : null;
        var shellStart = previous is null ? 0 : previous.Offset + previous.Length;
        var markerStart = PreviousLine(document.FullSource, region.Span.Offset);
        var shell = document.FullSource.Substring(shellStart, markerStart - shellStart);
        var handlers = System.Text.RegularExpressions.Regex.Matches(shell,
            @"(?m)^    def _vac_event_" + System.Text.RegularExpressions.Regex.Escape(id) + @"\(self\):\r?$");
        if (handlers.Count != 1) throw new ArgumentException("消息区域包装无法安全识别，未删除任何代码。");
        var start = shellStart + handlers[0].Index;
        var marker = region.Span.Offset + region.Span.Length;
        var end = document.FullSource.IndexOf('\n', marker);
        end = end < 0 ? document.FullSource.Length : end + 1;
        var without = document with { Regions = document.Regions!.Where(r => r.Id != id).ToArray() };
        return ReplaceShell(without, start, end - start, "");
    }

    private static ObjectScriptDocument ReplaceShell(ObjectScriptDocument document, int offset, int length, string replacement)
    {
        var delta = replacement.Length - length;
        return document with { FullSource = document.FullSource.Remove(offset, length).Insert(offset, replacement),
            Regions = document.Regions!.Select(r => r.Span.Offset >= offset + length
                ? r with { Span = r.Span with { Offset = r.Span.Offset + delta } } : r).ToArray() };
    }

    private static int PreviousLine(string source, int offset) => offset <= 1 ? 0 : source.LastIndexOf('\n', offset - 2) + 1;
    private static string Metadata(string id, string identity) => JsonSerializer.Serialize(new { id, kind = "event", eventName = identity });

    private static ObjectScriptRegion MessageRegion(ObjectScriptDocument document, string id)
    {
        RequireMessageDocument(document);
        return document.Regions!.FirstOrDefault(r => r.Id == id && r.Kind == "event" && MessageEvents.Name(r.EventName) is not null)
            ?? throw new ArgumentException("请选择消息事件区域。");
    }

    private static void RequireMessageDocument(ObjectScriptDocument document)
    {
        if (!document.HasRegions || document.TemplateVersion != 3)
            throw new ArgumentException("请先升级为 v3 对象脚本模板，再管理消息事件。");
    }

    public static void ValidateMessage(ObjectScriptDocument document, string name, string? excludingId = null)
    {
        RequireMessageDocument(document);
        MessageEvents.ValidateName(name);
        if (document.Regions!.Any(r => r.Id != excludingId && r.Kind == "event" && MessageEvents.Name(r.EventName) == name))
            throw new ArgumentException("当前对象已经有同名消息事件。");
    }
}
