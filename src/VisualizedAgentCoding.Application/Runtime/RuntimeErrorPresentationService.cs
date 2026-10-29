using System.Collections.ObjectModel;
using System.Text;
using VisualizedAgentCoding.Application.Python;

namespace VisualizedAgentCoding.Application.Runtime;

public sealed record RuntimePresentedFrame(PythonRunFrame Original, RuntimeSourceSnapshot? Snapshot,
    string DisplayText, string Code, int? ViewLine = null, int? ViewColumn = null,
    string Navigation = "none", string? Diagnostic = null)
{
    public bool CanNavigate => Snapshot is not null && Navigation is "code" or "imports" && Original.Line > 0;
}

public sealed record RuntimeErrorReport(string Summary, ReadOnlyCollection<RuntimePresentedFrame> Frames,
    string UserText, string Details, int PrimaryFrameIndex = -1, string Explanation = "");

public sealed class RuntimePreparationException(RuntimeErrorReport report) : Exception(report.Summary)
{
    public RuntimeErrorReport Report { get; } = report;
}

/// <summary>Only this layer translates execution coordinates into user-facing errors.</summary>
public sealed class RuntimeErrorPresentationService
{
    public RuntimeErrorReport Present(PythonRunEvent message, IReadOnlyList<RuntimeSourceSnapshot> sources)
    {
        var frames = new List<RuntimePresentedFrame>();
        var output = new StringBuilder();
        var explanation = new StringBuilder();
        var error = message.Exception ?? new PythonExceptionData("运行错误", "异常信息不完整，请查看诊断详情。", message.Frames ?? []);
        var remaining = 128;
        Walk(error, "", 0);
        if (frames.Count == 0)
        {
            const string unavailable = "未找到可定位的用户代码；可展开诊断详情查看完整来源。";
            output.AppendLine(unavailable); explanation.AppendLine(unavailable);
        }
        var summary = Header(error);
        return Build(summary, frames, output.ToString(), message.RawTraceback ?? message.Text) with { Explanation = explanation.ToString().TrimEnd() };

        void Walk(PythonExceptionData item, string prefix, int depth)
        {
            if (depth > 12 || remaining-- <= 0)
            { output.AppendLine("部分异常信息已省略，详见诊断详情。"); explanation.AppendLine("部分异常信息已省略，详见诊断详情。"); return; }
            if (item.Cause is { } cause)
            { Walk(cause, prefix, depth + 1); output.AppendLine("以上异常直接导致以下异常："); explanation.AppendLine("以上异常直接导致以下异常："); }
            else if (!item.SuppressContext && item.Context is { } context)
            { Walk(context, prefix, depth + 1); output.AppendLine("处理以上异常时又发生了异常："); explanation.AppendLine("处理以上异常时又发生了异常："); }
            output.Append(prefix).AppendLine(Header(item));
            if (depth > 0 || item.Cause is not null || item.Context is not null || item.Children is { Length: > 0 })
                explanation.Append(prefix).AppendLine(Header(item));
            foreach (var frame in item.Frames)
            {
                var source = FindSource(frame.Path, sources);
                if (source is null) continue; // external and product frames remain in raw details
                var mapped = source.Map(frame.Line, frame.Column ?? 1);
                var title = source.Label;
                if (mapped is null)
                {
                    var hidden = source.HiddenRegionTitle(frame.Line);
                    title += " → " + (hidden ?? "位置无法映射");
                    frames.Add(new(frame, source, prefix + title, ""));
                }
                else
                {
                    if (mapped.RegionTitle.Length > 0) title += " → " + mapped.RegionTitle;
                    // Generated handler names never enter user labels. Ordinary functions remain useful.
                    if (mapped.RegionTitle is "" or "类定义" && frame.Function is not ("<module>" or "<diagnostic>"))
                        title += " → " + frame.Function;
                    title += mapped.Navigation == "imports" ? "（点击查看导入摘要）" : $"，第 {mapped.ViewLine} 行"
                        + (frame.Column is null ? "" : $"，第 {mapped.ViewColumn} 列");
                    frames.Add(new(frame, source, prefix + title, mapped.Code, mapped.ViewLine, mapped.ViewColumn, mapped.Navigation));
                }
                var last = frames[^1];
                output.Append("  ").AppendLine(last.DisplayText);
                if (last.Code.Length > 0) output.Append("    ").AppendLine(last.Code);
            }
            foreach (var note in item.Notes ?? []) { output.Append("  ").AppendLine(note); explanation.AppendLine(note); }
            for (var i = 0; i < (item.Children?.Length ?? 0); i++) Walk(item.Children![i], prefix + $"[{i + 1}] ", depth + 1);
            if (item.Truncated) { output.AppendLine("部分异常信息已省略，详见诊断详情。"); explanation.AppendLine("部分异常信息已省略，详见诊断详情。"); }
        }
    }

    public RuntimeErrorReport PresentDiagnostics(RuntimeSourceSnapshot source, PythonDiagnostic[] diagnostics,
        string summary, string details)
    {
        var frames = new List<RuntimePresentedFrame>();
        var text = new StringBuilder(summary).AppendLine();
        foreach (var diagnostic in diagnostics)
        {
            var (line, column) = source.ToFullPosition(diagnostic.Line, diagnostic.Column);
            if (diagnostic.Navigation == "imports" && source.ImportFullLine is { } importLine) { line = importLine; column = 1; }
            var mapped = source.Map(line, column);
            var label = source.Label + (mapped?.Navigation == "imports" ? " → 受管理导入（点击查看导入摘要）"
                : diagnostic.Line > 0 ? $"，第 {diagnostic.Line} 行" : " → 位置无法映射");
            var navigation = mapped?.Navigation ?? "none";
            frames.Add(new(new(source.AbsolutePath, line, "<diagnostic>", column), source, label,
                mapped?.Code ?? "", diagnostic.Line > 0 ? diagnostic.Line : null, diagnostic.Column, navigation, diagnostic.Message));
            text.Append("  ").Append(label).Append("：").AppendLine(diagnostic.Message);
            if (mapped?.Code is { Length: > 0 } code) text.Append("    ").AppendLine(code);
        }
        return Build(summary, frames, text.ToString(), details);
    }

    public static RuntimeErrorReport EnvironmentFailure(string details) => new("运行环境错误，启动或通信失败。",
        Array.AsReadOnly(Array.Empty<RuntimePresentedFrame>()), "运行环境错误，启动或通信失败。请检查“设置 → 通用”中的 Python 环境，或查看诊断详情。", details,
        Explanation: "请检查“设置 → 通用”中的 Python 环境，或查看诊断详情。");

    public static RuntimeErrorReport PreparationFailure(string message, string details) => new("运行准备失败",
        Array.AsReadOnly(Array.Empty<RuntimePresentedFrame>()), "运行准备失败：" + message, details, Explanation: message);

    private static RuntimeErrorReport Build(string summary, List<RuntimePresentedFrame> frames, string text, string details) =>
        new(summary, frames.AsReadOnly(), text.TrimEnd(), details, frames.FindLastIndex(f => f.CanNavigate));

    private static string Header(PythonExceptionData error) => error.Type + "：" +
        (error.Type == "ZeroDivisionError" && error.Message == "division by zero" ? "除数不能为零" : error.Message);

    private static RuntimeSourceSnapshot? FindSource(string path, IReadOnlyList<RuntimeSourceSnapshot> sources)
    {
        try
        {
            if (!Path.IsPathFullyQualified(path)) return null;
            var normalized = Path.GetFullPath(path);
            return sources.FirstOrDefault(s => string.Equals(s.AbsolutePath, normalized,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
        }
        catch (ArgumentException) { return null; }
    }
}
