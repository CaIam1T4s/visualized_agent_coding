using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using VisualizedAgentCoding.Application.Runtime;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Avalonia.Media.Imaging;
using happy_agent_coding.Services;
using VisualizedAgentCoding.Application.Ports;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Domain.Projects;
using VisualizedAgentCoding.Infrastructure.Projects;

namespace happy_agent_coding.ViewModels;

public partial class MainViewModel
{
    private readonly ProjectStageService stageService = new(new JsonStageStore(), new ProjectImageStore());
    private StageStorageSnapshot stageSnapshot = new(new(), null);
    private bool stageDirty;
    private bool restoringStage;
    private string? currentBackdropId;

    private string? runtimeBackdropId;
    private long stageSequence;
    private bool hasRuntimePreview;
    public bool HasRuntimeStagePreview => hasRuntimePreview;
    public bool CanEditStage => !IsPythonActive && !hasRuntimePreview;
    public bool CanEditInitialStage => HasOpenProject && !IsPythonActive && !hasRuntimePreview;
    public bool CanReturnToInitialStage => hasRuntimePreview && !IsPythonActive;

    [RelayCommand(CanExecute = nameof(CanReturnToInitialStage))]
    private void ReturnToInitialStage() => ClearRuntimeStage();

    partial void OnIsPythonActiveChanged(bool value) => RefreshStageInspectorState();

    private void RefreshStageInspectorState()
    {
        foreach (var item in StageSprites.Concat(StageBackdrops)) item.IsStageReadOnly = IsPythonActive;
        OnPropertyChanged(nameof(CanEditInitialStage));
        OnPropertyChanged(nameof(CanEditStage));
        OnPropertyChanged(nameof(CanReturnToInitialStage));
        ReturnToInitialStageCommand.NotifyCanExecuteChanged();
    }
    public StageObjectItem? DisplayBackdrop => hasRuntimePreview
        ? StageBackdrops.FirstOrDefault(b => b.ObjectId == runtimeBackdropId) : CurrentBackdrop;

    private void BeginStagePropertyEdit()
    {
        if (!IsPythonActive && hasRuntimePreview) ClearRuntimeStage();
    }

    private void ClearRuntimeStage()
    {
        hasRuntimePreview = false; runtimeBackdropId = null; stageSequence = 0;
        foreach (var item in StageSprites.Concat(StageBackdrops)) item.ApplyRuntimeState(null);
        OnPropertyChanged(nameof(DisplayBackdrop));
        OnPropertyChanged(nameof(HasRuntimeStagePreview));
        RefreshStageInspectorState();
    }

    private void ApplyRuntimeStage(RuntimeStageFrame frame)
    {
        if (lastRun is null || frame.SessionId != lastRun.SessionId || frame.Sequence <= stageSequence
            || runWorkspace != runtimeWorkspace || frame.Objects is null) return;
        var items = StageSprites.Concat(StageBackdrops).ToDictionary(o => o.ObjectId);
        if (frame.Objects.Length != items.Count || frame.Objects.Any(o => o is null || string.IsNullOrEmpty(o.ObjectId) || !items.ContainsKey(o.ObjectId))
            || frame.Objects.Select(o => o.ObjectId).Distinct().Count() != items.Count
            || frame.CurrentBackdropId is { } backdrop && !StageBackdrops.Any(b => b.ObjectId == backdrop)) return;
        try { StageRules.Validate(new StageState { Objects = frame.Objects.ToList() }); }
        catch (ProjectFormatException) { return; }
        stageSequence = frame.Sequence; hasRuntimePreview = true; runtimeBackdropId = frame.CurrentBackdropId;
        foreach (var state in frame.Objects) items[state.ObjectId].ApplyRuntimeState(state);
        OnPropertyChanged(nameof(DisplayBackdrop));
        OnPropertyChanged(nameof(HasRuntimeStagePreview));
        RefreshStageInspectorState();
    }

    private void ResetStage(StageStorageSnapshot snapshot)
    {
        foreach (var item in StageSprites.Concat(StageBackdrops)) { item.PropertyChanged -= OnStageItemChanged; item.Dispose(); }
        hasRuntimePreview = false; runtimeBackdropId = null; stageSequence = 0;
        StageSprites.Clear(); StageBackdrops.Clear();
        OnPropertyChanged(nameof(DisplayBackdrop));
        OnPropertyChanged(nameof(HasRuntimeStagePreview));
        RefreshStageInspectorState();
        stageSnapshot = snapshot; currentBackdropId = snapshot.State.CurrentBackdropId; stageDirty = false;
    }

    private void RestoreStageItem(StageObjectItem item)
    {
        var state = stageSnapshot.State.Objects.FirstOrDefault(o => o.ObjectId == item.ObjectId);
        if (state is not null) item.Restore(state);
        if (state?.ImagePath is { } image)
        {
            Bitmap? bitmap = null;
            try
            {
                var bytes = StageImageLoader.Read(Path.Combine(activeProject!.RootPath, image.Replace('/', Path.DirectorySeparatorChar)));
                using var input = new MemoryStream(bytes);
                bitmap = new Bitmap(input);
                item.SetImage(image, bitmap, $"已设置图片（{bitmap.PixelSize.Width} × {bitmap.PixelSize.Height}）");
                bitmap = null;
            }
            catch (Exception) { item.SetImage(image, null, "图片缺失或无法读取，请重新导入。"); }
            finally { bitmap?.Dispose(); }
        }
        item.IsStageReadOnly = IsPythonActive;
        item.InitialEditRequested += BeginStagePropertyEdit;
        item.PropertyChanged += OnStageItemChanged;
    }

    private void OnStageItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!restoringStage && e.PropertyName is nameof(StageObjectItem.X) or nameof(StageObjectItem.Y) or nameof(StageObjectItem.Size)
            or nameof(StageObjectItem.Direction) or nameof(StageObjectItem.IsVisible) or nameof(StageObjectItem.Layer) or nameof(StageObjectItem.ImagePath))
        {
            if (hasRuntimePreview && !IsPythonActive) ClearRuntimeStage();
            stageDirty = true;
        }
    }

    private void SaveStage()
    {
        if (activeProject is null) return;
        if (stageDirty)
        {
            var state = new StageState { CurrentBackdropId = CurrentBackdrop?.ObjectId,
                Objects = StageSprites.Concat(StageBackdrops).Select(o => o.Snapshot()).ToList() };
            stageSnapshot = stageService.Save(activeProject, state, stageSnapshot.Revision);
            stageDirty = false;
        }
        CleanStageImages();
    }

    private void CleanStageImages()
    {
        if (activeProject is null || IsPythonActive) return;
        var retained = StageSprites.Concat(StageBackdrops).Select(o => o.ImagePath)
            .Concat(stageSnapshot.State.Objects.Select(o => o.ImagePath));
        if (stageService.CleanUnusedImages(activeProject, retained, stageSnapshot.Revision, PythonCode) is { } message)
        { ProjectResourceMessage = message; OnPropertyChanged(nameof(ProjectResourceMessage)); }
    }

    public void ReloadStage()
    {
        if (activeProject is null || IsPythonActive) return;
        try
        {
            var snapshot = stageService.Load(activeProject);
            ResetStage(snapshot);
            RefreshStageObjects();
            ProjectResourceMessage = "已重新加载舞台布局。";
            CleanStageImages();
        }
        catch (Exception ex) { ProjectResourceMessage = "舞台未重新加载：" + ex.Message; }
        OnPropertyChanged(nameof(ProjectResourceMessage));
    }

    public void ReportStageImageError(string message)
    { ProjectResourceMessage = "图片未导入：" + message; OnPropertyChanged(nameof(ProjectResourceMessage)); }
    private void DisposeStageImages()
    {
        foreach (var item in StageSprites.Concat(StageBackdrops)) { item.PropertyChanged -= OnStageItemChanged; item.Dispose(); }
    }

    public Task ImportStageImageAsync(StageObjectItem item, string sourcePath)
    {
        if (activeProject is null || IsPythonActive || !StageSprites.Concat(StageBackdrops).Contains(item)) return Task.CompletedTask;
        Bitmap? bitmap = null;
        try
        {
            var bytes = StageImageLoader.Read(sourcePath);
            using var input = new MemoryStream(bytes);
            bitmap = new Bitmap(input);
            var path = stageService.ImportImage(activeProject, item.ObjectId, sourcePath, bytes);
            BeginStagePropertyEdit();
            item.Size = 100;
            item.SetImage(path, bitmap, Path.GetFileName(sourcePath), normalize: true);
            bitmap = null;
            stageDirty = true;
            ProjectResourceMessage = "图片已导入；保存项目后恢复此布局。";
            CleanStageImages();
        }
        catch (Exception ex) { ProjectResourceMessage = "图片未导入：" + ex.Message; }
        finally { bitmap?.Dispose(); OnPropertyChanged(nameof(ProjectResourceMessage)); }
        return Task.CompletedTask;
    }
}
