using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisualizedAgentCoding.Domain.Projects;
using Avalonia.Media.Imaging;
using Avalonia.Media;

namespace happy_agent_coding.ViewModels;

/// <summary>
/// A stage sprite or backdrop shown in the object manager. Identity comes from the
/// indexed script; transforms describe the editable initial scene, never Python runtime state.
/// </summary>
public partial class StageObjectItem : ObservableObject, IDisposable
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

    private StageObjectState? runtimeState;
    public double RenderX => runtimeState?.X ?? X;
    public double RenderY => runtimeState?.Y ?? Y;
    public double RenderSize => runtimeState?.Size ?? Size;
    public double RenderDirection => runtimeState?.Direction ?? Direction;
    public bool RenderVisible => runtimeState?.Visible ?? IsVisible;
    public int RenderLayer => runtimeState?.Layer ?? Layer;
    public void ApplyRuntimeState(StageObjectState? state)
    {
        runtimeState = state;
        if (state is not null) IsStageReadOnly = true;
        foreach (var name in new[] { nameof(RenderX), nameof(RenderY), nameof(RenderSize), nameof(RenderDirection),
            nameof(RenderVisible), nameof(RenderLayer), nameof(SpriteWidth), nameof(SpriteHeight), nameof(CanvasLeft),
            nameof(CanvasTop), nameof(Rotation), nameof(StageOpacity), nameof(BackdropImageWidth), nameof(BackdropImageHeight), nameof(InspectorX), nameof(InspectorY),
            nameof(InspectorSize), nameof(InspectorDirection), nameof(InspectorLayer), nameof(InspectorVisible), nameof(InspectorHidden), nameof(InspectorVisibilityText) })
            OnPropertyChanged(name);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditProperties))]
    private bool isStageReadOnly;
    public bool CanEditProperties => !IsStageReadOnly;
    public bool InspectorVisible => RenderVisible;
    public bool InspectorHidden => !RenderVisible;
    public string InspectorVisibilityText => RenderVisible ? "当前显示：可见" : "当前显示：隐藏";
    public event Action? InitialEditRequested;
    private bool PrepareInitialEdit()
    {
        if (!CanEditProperties) return false;
        InitialEditRequested?.Invoke();
        return true;
    }
    public double InspectorX { get => RenderX; set { if (value != RenderX && PrepareInitialEdit()) X = value; } }
    public double InspectorY { get => RenderY; set { if (value != RenderY && PrepareInitialEdit()) Y = value; } }
    public double InspectorSize { get => RenderSize; set { if (value != RenderSize && PrepareInitialEdit()) Size = value; } }
    public double InspectorDirection { get => RenderDirection; set { if (value != RenderDirection && PrepareInitialEdit()) Direction = value; } }
    public int InspectorLayer { get => RenderLayer; set { if (value != RenderLayer && PrepareInitialEdit()) Layer = value; } }

    public string ScriptId { get; private set; }

    public string ObjectId { get; }

    public ProjectScriptKind Kind { get; }

    public string RelativePath { get; private set; } = string.Empty;

    public string KindLabel => Kind == ProjectScriptKind.Backdrop ? "背景" : "角色";
    public bool IsSprite => Kind == ProjectScriptKind.Object;
    public string? ImagePath { get; private set; }
    private bool normalizeImage;
    public double ImageScale => normalizeImage && Picture is { } image ? StageRules.FitImageScale(image.PixelSize.Width, image.PixelSize.Height) : 1;
    public double BackdropImageWidth => normalizeImage ? SpriteWidth : StageWidth;
    public double BackdropImageHeight => normalizeImage ? SpriteHeight : StageHeight;
    public Stretch BackdropStretch => normalizeImage ? Stretch.Uniform : Stretch.UniformToFill;
    [ObservableProperty] private string imageStatus = "尚未设置图片";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImage), nameof(SpriteWidth), nameof(SpriteHeight), nameof(CanvasLeft), nameof(CanvasTop), nameof(BackdropImageWidth), nameof(BackdropImageHeight), nameof(BackdropStretch))]
    private Bitmap? picture;
    public bool HasImage => Picture is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RenderLayer), nameof(InspectorLayer))]
    private int layer;
    partial void OnLayerChanged(int value) { if (value is < -10000 or > 10000) Layer = Math.Clamp(value, -10000, 10000); }
    partial void OnXChanged(double value) { var safe = double.IsFinite(value) ? Math.Clamp(value, -10000, 10000) : 0; if (value != safe) X = safe; }
    partial void OnYChanged(double value) { var safe = double.IsFinite(value) ? Math.Clamp(value, -10000, 10000) : 0; if (value != safe) Y = safe; }
    partial void OnSizeChanged(double value) { var safe = double.IsFinite(value) ? Math.Clamp(value, 1, 1000) : 100; if (value != safe) Size = safe; }
    partial void OnDirectionChanged(double value)
    {
        var safe = double.IsFinite(value) ? Math.Clamp(value, -180, 180) : 90;
        if (value != safe) Direction = safe;
    }

    public void Restore(StageObjectState state)
    { X = state.X; Y = state.Y; Size = state.Size; Direction = state.Direction; IsVisible = state.Visible; Layer = state.Layer; ImagePath = state.ImagePath; normalizeImage = state.NormalizeImage; }
    public StageObjectState Snapshot() => new(ObjectId, X, Y, Size, Direction, IsVisible, Layer, ImagePath, normalizeImage);
    public void SetImage(string? path, Bitmap? bitmap, string status, bool? normalize = null)
    {
        var old = Picture;
        if (normalize is { } value) normalizeImage = value;
        ImagePath = path;
        Picture = bitmap;
        ImageStatus = status;
        OnPropertyChanged(nameof(ImagePath));
        if (!ReferenceEquals(old, bitmap)) old?.Dispose();
    }
    [RelayCommand] private void BringForward() => InspectorLayer = Math.Clamp(RenderLayer + 1, -10000, 10000);
    [RelayCommand] private void SendBackward() => InspectorLayer = Math.Clamp(RenderLayer - 1, -10000, 10000);
    public void Dispose() { InitialEditRequested = null; Picture?.Dispose(); }

    public string Initial =>
        string.IsNullOrWhiteSpace(DisplayName) ? "?" : DisplayName.Trim()[..1];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Initial))]
    private string displayName = string.Empty;

    public const double StageWidth = StageRules.Width;

    public const double StageHeight = StageRules.Height;

    public const double BaseSpriteSize = 48;

    [ObservableProperty]
    private bool isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanvasLeft))]
    [NotifyPropertyChangedFor(nameof(RenderX), nameof(InspectorX))]
    private double x;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanvasTop))]
    [NotifyPropertyChangedFor(nameof(RenderY), nameof(InspectorY))]
    private double y;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SpriteWidth))]
    [NotifyPropertyChangedFor(nameof(SpriteHeight))]
    [NotifyPropertyChangedFor(nameof(BackdropImageWidth), nameof(BackdropImageHeight))]
    [NotifyPropertyChangedFor(nameof(CanvasLeft))]
    [NotifyPropertyChangedFor(nameof(CanvasTop))]
    [NotifyPropertyChangedFor(nameof(RenderSize), nameof(InspectorSize))]
    private double size = 100;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Rotation))]
    [NotifyPropertyChangedFor(nameof(RenderDirection), nameof(InspectorDirection))]
    private double direction = 90;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHidden))]
    [NotifyPropertyChangedFor(nameof(StageOpacity))]
    [NotifyPropertyChangedFor(nameof(RenderVisible), nameof(InspectorVisible), nameof(InspectorHidden), nameof(InspectorVisibilityText))]
    private bool isVisible = true;

    public bool IsHidden => !IsVisible;

    /// <summary>Placeholder costume size on the 480×360 canvas.</summary>
    public double SpriteWidth => (Picture?.PixelSize.Width ?? BaseSpriteSize) * ImageScale * RenderSize / 100.0;

    public double SpriteHeight => (Picture?.PixelSize.Height ?? BaseSpriteSize) * ImageScale * RenderSize / 100.0;

    /// <summary>Canvas X of the sprite's top-left. Stage origin is the canvas center, X right.</summary>
    public double CanvasLeft => StageWidth / 2.0 + RenderX - SpriteWidth / 2.0;

    /// <summary>Canvas Y of the sprite's top-left. Stage Y is up, canvas Y is down.</summary>
    public double CanvasTop => StageHeight / 2.0 - RenderY - SpriteHeight / 2.0;

    /// <summary>Avalonia clockwise degrees. Stage direction 90° (right) maps to 0°.</summary>
    public double Rotation => RenderDirection - 90.0;

    public double StageOpacity => RenderVisible ? 1.0 : 0.0;

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
    private void ShowObject() { if (PrepareInitialEdit()) IsVisible = true; }

    [RelayCommand]
    private void HideObject() { if (PrepareInitialEdit()) IsVisible = false; }
}
