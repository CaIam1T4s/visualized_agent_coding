namespace VisualizedAgentCoding.Domain.Blueprint;

/// <summary>UTF-16 source range owned by a node. Line numbers are 1-based and informational.</summary>
public readonly record struct SourceSpan(int Start, int Length, int StartLine, int EndLine)
{
    public int End => Start + Length;
    public bool IsEmpty => Length <= 0;
    public static readonly SourceSpan None = new(0, 0, 0, 0);
}

/// <summary>How a variable is resolved when the node is projected back to Python.</summary>
public enum NodeScope
{
    Parameter,
    Local,
    LoopVariable,
    InstanceAttribute,
    ClassAttribute,
    ProjectVariable,
    ModuleGlobal,
}

/// <summary>A scope-qualified variable reference. Display text is derived, never used as identity.</summary>
public sealed record VariableRef(
    NodeScope Scope,
    string Name,
    string? DeclaringType = null,
    string? OwnerMethod = null)
{
    public string ScopeLabel => Scope switch
    {
        NodeScope.Parameter => "参数",
        NodeScope.Local => "局部",
        NodeScope.LoopVariable => "循环",
        NodeScope.InstanceAttribute => "实例",
        NodeScope.ClassAttribute => "类",
        NodeScope.ProjectVariable => "项目",
        NodeScope.ModuleGlobal => "模块",
        _ => "变量",
    };

    public string ScopeTitle => Scope switch
    {
        NodeScope.Parameter => "参数",
        NodeScope.Local => "局部变量",
        NodeScope.LoopVariable => "循环变量",
        NodeScope.InstanceAttribute => "实例属性",
        NodeScope.ClassAttribute => "类属性",
        NodeScope.ProjectVariable => "项目变量",
        NodeScope.ModuleGlobal => "模块变量",
        _ => "变量",
    };

    public string? ValueText { get; init; }

    public string HoverText => $"{ScopeTitle}：{Name} = {ValueText ?? "/"}";

    public string OwnerLabel => Scope switch
    {
        NodeScope.Parameter or NodeScope.Local or NodeScope.LoopVariable => OwnerMethod,
        NodeScope.InstanceAttribute or NodeScope.ClassAttribute => DeclaringType,
        NodeScope.ModuleGlobal => DeclaringType,
        _ => null,
    } ?? string.Empty;

    public string DisplayName => OwnerLabel.Length == 0
        ? $"{Name}  {ScopeLabel}"
        : $"{Name}  {ScopeLabel}@{OwnerLabel}";

    public string GeneratedCode => Scope switch
    {
        NodeScope.InstanceAttribute => $"self.{Name}",
        NodeScope.ClassAttribute => $"{DeclaringType ?? "self"}.{Name}",
        NodeScope.ProjectVariable => $"get_variable(\"{Name}\")",
        _ => Name,
    };
}

public enum PortKind
{
    Exec,
    Value,
}

/// <summary>Coarse node category used by the renderer and palette.</summary>
public enum NodeKind
{
    Start,
    Statement,
    ControlFlow,
    Expression,
    Data,
    Function,
    Class,
    Comment,
    Opaque,
}

public enum NodeRelation
{
    Next,
    Value,
    Exec,
    Annotation,
}

/// <summary>A typed port on a node. Ports are rendered on the node edge, never as free text.</summary>
public sealed record NodePort(
    string Id,
    PortKind Kind,
    NodeRelation Relation,
    string? Type = null,
    bool IsInput = true,
    bool IsOptional = false,
    string? Label = null,
    string? DefaultText = null)
{
    public bool IsExec => Kind == PortKind.Exec;
    public bool IsValue => Kind == PortKind.Value;
}

/// <summary>A node in a projected Python document. Node identity is view-local.</summary>
public sealed class BlueprintNode
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public NodeKind Kind { get; set; } = NodeKind.Statement;
    public string DisplayName { get; set; } = string.Empty;
    public SourceSpan Source { get; set; } = SourceSpan.None;
    public List<NodePort> Ports { get; } = [];
    public Dictionary<string, string> Arguments { get; } = [];
    public List<BlueprintNode> Children { get; } = [];
    public List<BlueprintNode> ElseChildren { get; } = [];
    public BlueprintNode? Parent { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public bool HasPosition { get; set; }
    public bool IsCollapsed { get; set; }
    public string? Text { get; set; }
    public BlueprintNode? AnnotationTarget { get; set; }
    public IReadOnlyList<BlueprintNode> AllChildren => [.. Children, .. ElseChildren];
}

/// <summary>One projected node document for a script region.</summary>
public sealed class BlueprintDocument
{
    public required string ScriptId { get; init; }
    public required string RegionId { get; init; }
    public string? EventTitle { get; set; }
    public string? Error { get; set; }
    public List<BlueprintNode> Roots { get; } = [];
    public int SourceVersion { get; set; }
}
