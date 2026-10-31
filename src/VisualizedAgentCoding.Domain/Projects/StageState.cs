using System;
using System.Collections.Generic;
using System.Linq;

namespace VisualizedAgentCoding.Domain.Projects;

/// <summary>Editable initial scene; execution state must never be stored in this model.</summary>
public sealed record StageState
{
    public const int CurrentVersion = 2;
    public int Version { get; init; } = CurrentVersion;
    public string? CurrentBackdropId { get; init; }
    public List<StageObjectState> Objects { get; init; } = [];
}

/// <summary>A named image owned by one role or backdrop.</summary>
public sealed record StageCostumeState(string Id, string Name, string ImagePath, bool NormalizeImage);

public sealed record StageObjectState(string ObjectId, double X = 0, double Y = 0,
    double Size = 100, double Direction = 90, bool Visible = true, int Layer = 0, string? ImagePath = null, bool NormalizeImage = false,
    bool ClickEnabled = true, List<StageCostumeState>? Costumes = null, string? InitialCostumeId = null);

public static class StageCostumeRules
{
    public const int MaximumNameLength = 64;
    public const int MaximumCostumes = 64;

    public static string ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name != name.Trim() || name.Length > MaximumNameLength
            || name.Any(char.IsControl))
            throw new ProjectFormatException($"造型名称须为 1–{MaximumNameLength} 个字符，不能包含控制字符或首尾空白。");
        return name;
    }

    public static string SuggestName(string sourcePath, IEnumerable<string> existing)
    {
        var stem = System.IO.Path.GetFileNameWithoutExtension(sourcePath);
        var cleaned = new string((stem ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (cleaned.Length == 0) cleaned = "造型";
        if (cleaned.Length > MaximumNameLength) cleaned = cleaned[..MaximumNameLength];
        var used = existing.ToHashSet(StringComparer.Ordinal);
        if (!used.Contains(cleaned)) return cleaned;
        for (var index = 2; ; index++)
        {
            var suffix = " " + index;
            var candidate = cleaned.Length + suffix.Length <= MaximumNameLength
                ? cleaned + suffix : cleaned[..(MaximumNameLength - suffix.Length)] + suffix;
            if (!used.Contains(candidate)) return candidate;
        }
    }

    public static void Validate(IReadOnlyList<StageCostumeState>? costumes, string? initialCostumeId,
        string? imagePath, bool normalizeImage)
    {
        costumes ??= [];
        if (costumes.Count > MaximumCostumes) throw new ProjectFormatException($"每个对象最多 {MaximumCostumes} 个造型。");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var costume in costumes)
        {
            if (costume is null || string.IsNullOrWhiteSpace(costume.Id) || !ids.Add(costume.Id)
                || !names.Add(ValidateName(costume.Name)) || !StageRules.IsImagePath(costume.ImagePath)
                || costume.ImagePath is null)
                throw new ProjectFormatException("舞台造型身份、名称或图片路径无效。");
        }
        var initial = costumes.FirstOrDefault(costume => costume.Id == initialCostumeId);
        if (costumes.Count == 0)
        {
            if (initialCostumeId is not null || imagePath is not null || normalizeImage)
                throw new ProjectFormatException("舞台对象没有造型，但保留了图片或初始造型引用。");
            return;
        }
        if (initial is null) throw new ProjectFormatException("舞台对象的初始造型不存在。");
        if (imagePath != initial.ImagePath || normalizeImage != initial.NormalizeImage)
            throw new ProjectFormatException("舞台对象的图片字段与初始造型不一致。");
    }
}

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
        if (state.Version != StageState.CurrentVersion || state.Objects is null)
            throw new ProjectFormatException("舞台文件版本或对象列表无效。");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in state.Objects)
        {
            ValidateBase(item, ids);
            StageCostumeRules.Validate(item.Costumes, item.InitialCostumeId, item.ImagePath, item.NormalizeImage);
        }
    }

    /// <summary>Runtime frames carry only the active image, not the full editable costume list.</summary>
    public static void ValidateRuntime(StageState state)
    {
        if (state.Objects is null) throw new ProjectFormatException("舞台对象列表无效。");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in state.Objects) ValidateBase(item, ids);
    }

    public static void ValidateRuntimeObject(StageObjectState? item)
    {
        if (item is null) throw new ProjectFormatException("舞台对象无效。");
        ValidateBase(item, new HashSet<string>(StringComparer.Ordinal));
    }

    private static void ValidateBase(StageObjectState? item, HashSet<string> ids)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.ObjectId) || !ids.Add(item.ObjectId)
            || !double.IsFinite(item.X) || Math.Abs(item.X) > 100000 || !double.IsFinite(item.Y) || Math.Abs(item.Y) > 100000
            || !double.IsFinite(item.Size) || item.Size is < 1 or > 1000
            || !double.IsFinite(item.Direction) || item.Direction is < -180 or > 180
            || item.Layer is < -10000 or > 10000 || !IsImagePath(item.ImagePath))
            throw new ProjectFormatException("舞台对象身份、变换或图片路径无效。");
    }
}
