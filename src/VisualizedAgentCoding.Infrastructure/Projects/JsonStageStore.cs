using System.Text.Json;
using VisualizedAgentCoding.Application.Ports;
using VisualizedAgentCoding.Domain.Projects;

namespace VisualizedAgentCoding.Infrastructure.Projects;

public sealed class JsonStageStore : IStageStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
        WriteIndented = true, RespectNullableAnnotations = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
    };
    public StageStorageSnapshot Load(string root)
    {
        var path = Path.Combine(root, "scenes", "stage.json");
        if (!File.Exists(path)) return new(new(), null);
        var source = File.ReadAllText(path);
        try
        {
            var state = JsonSerializer.Deserialize<StageState>(source, Options) ?? throw new ProjectFormatException("舞台文件为空。");
            state = StageMigration.Upgrade(state);
            StageRules.Validate(state);
            return new(state, source);
        }
        catch (JsonException ex) { throw new ProjectFormatException("舞台文件格式无效，未覆盖原文件：" + ex.Message); }
    }
    public StageStorageSnapshot Save(string root, StageState state, string? expectedRevision)
    {
        StageRules.Validate(state);
        var path = Path.Combine(root, "scenes", "stage.json");
        var current = File.Exists(path) ? File.ReadAllText(path) : null;
        if (current != expectedRevision) throw new ProjectOperationException("舞台文件已被外部修改，未覆盖。可在对象属性中选择“重新加载舞台”，确认后读取磁盘布局。");
        var text = JsonSerializer.Serialize(state, Options);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try { File.WriteAllText(temporary, text); File.Move(temporary, path, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return new(state, text);
    }
}
