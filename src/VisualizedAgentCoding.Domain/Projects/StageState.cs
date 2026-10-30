namespace VisualizedAgentCoding.Domain.Projects;

/// <summary>Editable initial scene; execution state must never be stored in this model.</summary>
public sealed record StageState
{
    public int Version { get; init; } = 1;
    public string? CurrentBackdropId { get; init; }
    public List<StageObjectState> Objects { get; init; } = [];
}

public sealed record StageObjectState(string ObjectId, double X = 0, double Y = 0,
    double Size = 100, double Direction = 90, bool Visible = true, int Layer = 0, string? ImagePath = null, bool NormalizeImage = false);

public static class StageRules
{
    public const double Width = 480, Height = 360;
    public static double FitImageScale(double width, double height) => Math.Min(1, Math.Min(Width / width, Height / height));
    public static bool IsImagePath(string? path) => path is null ||
        path.StartsWith("assets/images/", StringComparison.Ordinal) && !path.Contains('\\') && !path.Contains(':')
        && path.Split('/').Length == 3 && !string.IsNullOrWhiteSpace(path.Split('/')[2])
        && path.Split('/')[2] is not ("." or "..");

    public static void Validate(StageState state)
    {
        if (state.Version != 1 || state.Objects is null) throw new ProjectFormatException("舞台文件版本或对象列表无效。");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in state.Objects)
            if (item is null || string.IsNullOrWhiteSpace(item.ObjectId) || !ids.Add(item.ObjectId)
                || !double.IsFinite(item.X) || Math.Abs(item.X) > 10000 || !double.IsFinite(item.Y) || Math.Abs(item.Y) > 10000
                || !double.IsFinite(item.Size) || item.Size is < 1 or > 1000
                || !double.IsFinite(item.Direction) || item.Direction is < -180 or > 180
                || item.Layer is < -10000 or > 10000 || !IsImagePath(item.ImagePath))
                throw new ProjectFormatException("舞台对象身份、变换或图片路径无效。");
    }
}
