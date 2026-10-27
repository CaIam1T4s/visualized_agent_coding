using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualizedAgentCoding.Domain.Projects;

namespace happy_agent_coding.ViewModels;

/// <summary>
/// A stage sprite or backdrop shown in the object manager. Identity comes from the
/// indexed script; transform values stay in the session until a scene file exists.
/// </summary>
public partial class StageObjectItem : ObservableObject
{
    public StageObjectItem(
        string scriptId,
        string objectId,
        ProjectScriptKind kind,
        string displayName,
        string relativePath)
    {
        ScriptId = scriptId;
        ObjectId = objectId;
        Kind = kind;
        RelativePath = relativePath;
        DisplayName = displayName;
    }

    public string ScriptId { get; private set; }

    public string ObjectId { get; }

    public ProjectScriptKind Kind { get; }

    public string RelativePath { get; private set; } = string.Empty;

    public string KindLabel => Kind == ProjectScriptKind.Backdrop ? "背景" : "角色";

    public string Initial =>
        string.IsNullOrWhiteSpace(DisplayName) ? "?" : DisplayName.Trim()[..1];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Initial))]
    private string displayName = string.Empty;

    public const double StageWidth = 480;

    public const double StageHeight = 360;

    public const double BaseSpriteSize = 48;

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanvasLeft))]
    private double x;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanvasTop))]
    private double y;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpriteWidth))]
    [NotifyPropertyChangedFor(nameof(SpriteHeight))]
    [NotifyPropertyChangedFor(nameof(CanvasLeft))]
    [NotifyPropertyChangedFor(nameof(CanvasTop))]
    private double size = 100;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Rotation))]
    private double direction = 90;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHidden))]
    [NotifyPropertyChangedFor(nameof(StageOpacity))]
    private bool isVisible = true;

    public bool IsHidden => !IsVisible;

    /// <summary>Placeholder costume size on the 480×360 canvas.</summary>
    public double SpriteWidth => Math.Max(8, BaseSpriteSize * Size / 100.0);

    public double SpriteHeight => SpriteWidth;

    /// <summary>Canvas X of the sprite's top-left. Stage origin is the canvas center, X right.</summary>
    public double CanvasLeft => StageWidth / 2.0 + X - SpriteWidth / 2.0;

    /// <summary>Canvas Y of the sprite's top-left. Stage Y is up, canvas Y is down.</summary>
    public double CanvasTop => StageHeight / 2.0 - Y - SpriteHeight / 2.0;

    /// <summary>Avalonia clockwise degrees. Stage direction 90° (right) maps to 0°.</summary>
    public double Rotation => Direction - 90.0;

    public double StageOpacity => IsVisible ? 1.0 : 0.4;

    /// <summary>Updates stage X/Y from a canvas top-left, keeping the sprite centered on that box.</summary>
    public void SetPositionFromCanvasTopLeft(double canvasLeft, double canvasTop)
    {
        X = Math.Round(canvasLeft + SpriteWidth / 2.0 - StageWidth / 2.0, 1);
        Y = Math.Round(StageHeight / 2.0 - canvasTop - SpriteHeight / 2.0, 1);
    }

    public void SyncFromScript(string scriptId, string displayName, string relativePath, bool isSelected)
    {
        ScriptId = scriptId;
        RelativePath = relativePath;
        DisplayName = displayName;
        IsSelected = isSelected;
    }

    public ProjectScriptItem ToScriptItem() =>
        new(ScriptId, DisplayName, RelativePath, Kind, IsSelected);

    [RelayCommand]
    private void ShowObject() => IsVisible = true;

    [RelayCommand]
    private void HideObject() => IsVisible = false;
}
