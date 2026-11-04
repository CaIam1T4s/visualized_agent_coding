using VisualizedAgentCoding.Domain.Blueprint;

namespace VisualizedAgentCoding.Blueprint.Core;

/// <summary>One visual row of a node. Input and output stay separate properties so the
/// renderer and layout engine agree on which ports share a row.</summary>
public sealed record BlueprintNodeRow(NodePort? Input, NodePort? Output)
{
    public string Label => Input?.Label ?? Output?.Label ?? Input?.Id ?? Output?.Id ?? string.Empty;
    public string? Value => Input?.DefaultText;
    public string Type => Input?.Type ?? Output?.Type
        ?? (Input?.IsExec == true || Output?.IsExec == true ? "exec" : "any");
}

/// <summary>Shared row construction. Only condition/case inputs may share a row with a
/// branch output; every other value input gets its own row.</summary>
public static class BlueprintNodeRows
{
    public static IReadOnlyList<BlueprintNodeRow> Build(BlueprintNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Kind == NodeKind.Comment) return [];

        var ports = node.Ports.Where(port => port.Id is not ("exec-in" or "exec-out")).ToList();
        var rows = new List<BlueprintNodeRow>();
        for (var index = 0; index < ports.Count; index++)
        {
            var port = ports[index];
            if (port.IsInput && index + 1 < ports.Count && !ports[index + 1].IsInput
                && CanShareRow(port, ports[index + 1]))
            {
                rows.Add(new BlueprintNodeRow(port, ports[index + 1]));
                index++;
            }
            else
            {
                rows.Add(new BlueprintNodeRow(port.IsInput ? port : null, port.IsInput ? null : port));
            }
        }
        return rows;
    }

    public static int Count(BlueprintNode node) => Build(node).Count;

    public static bool CanUseInlineValue(BlueprintNodeRow row) =>
        !string.IsNullOrWhiteSpace(row.Value)
        && (row.Input?.Type == "string" || IsBasicLiteral(row.Value));

    public static bool IsBasicLiteral(string? text)
    {
        var value = text?.Trim();
        if (string.IsNullOrEmpty(value)) return false;
        if (value is "True" or "False" or "None") return true;
        if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"')
            || (value[0] == '\'' && value[^1] == '\''))) return true;
        return double.TryParse(value, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out _);
    }

    private static bool CanShareRow(NodePort input, NodePort output) =>
        input.IsValue && output.IsExec
        && (input.Id.StartsWith("condition-", StringComparison.Ordinal)
            || input.Id.StartsWith("case-", StringComparison.Ordinal));
}
