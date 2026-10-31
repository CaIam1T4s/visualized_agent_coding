using System.Text.Json;
using VisualizedAgentCoding.Domain.Projects;

namespace VisualizedAgentCoding.Application.Editor.ObjectScripts;

public static partial class ObjectScriptFeatures
{
    public static ObjectScriptDocument AddMessage(ObjectScriptDocument document, string name)
    {
        ValidateMessage(document, name);
        return AddEventRegion(document, MessageEvents.Prefix + name, "message_");
    }

    private static ObjectScriptDocument AddEventRegion(ObjectScriptDocument document, string identity, string prefixId)
    {
        var id = prefixId + Guid.NewGuid().ToString("N");
        var nl = document.FullSource.Contains("\r\n") ? "\r\n" : "\n";
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
        return DeleteEventRegion(document, region);
    }

    private static ObjectScriptDocument DeleteEventRegion(ObjectScriptDocument document, ObjectScriptRegion region)
    {
        var id = region.Id;
        // Search only the verified shell between regions, never user body text.
        var index = Array.IndexOf(document.Regions!, region);
        var previous = index > 0 ? document.Regions![index - 1].Span : null;
        var shellStart = previous is null ? 0 : previous.Offset + previous.Length;
        var markerStart = PreviousLine(document.FullSource, region.Span.Offset);
        var shell = document.FullSource.Substring(shellStart, markerStart - shellStart);
        var handlers = System.Text.RegularExpressions.Regex.Matches(shell,
            @"(?m)^    def _vac_event_" + System.Text.RegularExpressions.Regex.Escape(id) + @"\(self\):\r?$");
        if (handlers.Count != 1) throw new ArgumentException("事件区域包装无法安全识别，未删除任何代码。");
        var start = shellStart + handlers[0].Index;
        var marker = region.Span.Offset + region.Span.Length;
        var end = document.FullSource.IndexOf('\n', marker);
        end = end < 0 ? document.FullSource.Length : end + 1;
        var without = document with { Regions = document.Regions!.Where(r => r.Id != id).ToArray() };
        return ReplaceShell(without, start, end - start, "");
    }

    public static void ValidateSpriteClick(ObjectScriptDocument document)
    {
        RequireMessageDocument(document);
        if (document.Regions!.Any(r => r.Kind == "event" && r.EventName == ProjectEventKinds.SpriteClick))
            throw new ArgumentException("该角色已经有点击事件区域。");
    }
    public static ObjectScriptDocument AddSpriteClick(ObjectScriptDocument document)
    {
        ValidateSpriteClick(document);
        return AddEventRegion(document, ProjectEventKinds.SpriteClick, "click_");
    }
    public static ObjectScriptDocument DeleteSpriteClick(ObjectScriptDocument document, string id)
    {
        RequireMessageDocument(document);
        var region = document.Regions!.SingleOrDefault(r => r.Id == id && r.Kind == "event" && r.EventName == ProjectEventKinds.SpriteClick)
            ?? throw new ArgumentException("请选择角色点击区域。");
        return DeleteEventRegion(document, region);
    }

    public static void ValidateStageClick(ObjectScriptDocument document)
    {
        RequireMessageDocument(document);
        if (document.Regions!.Any(r => r.Kind == "event" && r.EventName == ProjectEventKinds.StageClick))
            throw new ArgumentException("该背景已经有舞台点击事件区域。");
    }
    public static ObjectScriptDocument AddStageClick(ObjectScriptDocument document)
    {
        ValidateStageClick(document);
        return AddEventRegion(document, ProjectEventKinds.StageClick, "stage_click_");
    }
    public static ObjectScriptDocument DeleteStageClick(ObjectScriptDocument document, string id)
    {
        RequireMessageDocument(document);
        var region = document.Regions!.SingleOrDefault(r => r.Id == id && r.Kind == "event" && r.EventName == ProjectEventKinds.StageClick)
            ?? throw new ArgumentException("请选择舞台点击区域。");
        return DeleteEventRegion(document, region);
    }

    public static void ValidateBackdropSwitch(ObjectScriptDocument document, string targetId, string? excludingId = null)
    {
        RequireMessageDocument(document);
        var identity = BackdropSwitchEvents.Identity(targetId);
        if (document.Regions!.Any(r => r.Id != excludingId && r.Kind == "event" && r.EventName == identity))
            throw new ArgumentException("当前对象已经监听了该背景切换。");
    }
    public static ObjectScriptDocument AddBackdropSwitch(ObjectScriptDocument document, string targetId)
    {
        ValidateBackdropSwitch(document, targetId);
        return AddEventRegion(document, BackdropSwitchEvents.Identity(targetId), "backdrop_switch_");
    }
    public static ObjectScriptDocument DeleteBackdropSwitch(ObjectScriptDocument document, string id)
    {
        RequireMessageDocument(document);
        var region = document.Regions!.SingleOrDefault(r => r.Id == id && r.Kind == "event"
            && BackdropSwitchEvents.Target(r.EventName) is not null)
            ?? throw new ArgumentException("请选择背景切换事件区域。");
        return DeleteEventRegion(document, region);
    }

    public static void ValidateTimer(ObjectScriptDocument document, string value, bool repeat, string? excludingId = null)
    {
        RequireMessageDocument(document);
        var identity = TimerIdentity(value, repeat);
        if (document.Regions!.Any(r => r.Id != excludingId && r.Kind == "event" && r.EventName == identity))
            throw new ArgumentException("当前对象已经有相同的计时器事件。");
    }
    public static ObjectScriptDocument AddTimer(ObjectScriptDocument document, string value, bool repeat)
    {
        ValidateTimer(document, value, repeat);
        return AddEventRegion(document, TimerIdentity(value, repeat), repeat ? "timer_every_" : "timer_");
    }

    private static string TimerIdentity(string value, bool repeat) => TimerEvents.FormatIdentity(
        TimerEvents.ParseArgument(TimerEvents.ValidateArgument(value, repeat))!.Value);
    public static ObjectScriptDocument DeleteTimer(ObjectScriptDocument document, string id)
    {
        RequireMessageDocument(document);
        var region = document.Regions!.SingleOrDefault(r => r.Id == id && r.Kind == "event"
            && TimerEvents.ParseIdentity(r.EventName) is not null)
            ?? throw new ArgumentException("请选择计时器事件区域。");
        return DeleteEventRegion(document, region);
    }

    public static void ValidateKey(ObjectScriptDocument document, string name, string? excludingId = null)
    {
        RequireMessageDocument(document);
        KeyEvents.ValidateName(name);
        if (document.Regions!.Any(r => r.Id != excludingId && r.Kind == "event" && KeyEvents.Name(r.EventName) == name))
            throw new ArgumentException("当前对象已经有同名按键事件。");
    }
    public static ObjectScriptDocument AddKey(ObjectScriptDocument document, string name)
    {
        ValidateKey(document, name);
        return AddEventRegion(document, KeyEvents.Prefix + name, "key_");
    }
    public static ObjectScriptDocument DeleteKey(ObjectScriptDocument document, string id)
    {
        RequireMessageDocument(document);
        var region = document.Regions!.SingleOrDefault(r => r.Id == id && r.Kind == "event" && KeyEvents.Name(r.EventName) is not null)
            ?? throw new ArgumentException("请选择按键事件区域。");
        return DeleteEventRegion(document, region);
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
