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

    public string? BackdropObjectIdForScript(string scriptId) => activeProject?.FindScript(scriptId)?.ObjectId;

    public string? BackdropNameForObjectId(string objectId)
    {
        var script = activeProject?.Manifest.Scripts.FirstOrDefault(entry =>
            entry.Kind == ProjectScriptKind.Backdrop && entry.ObjectId == objectId);
        return script is null ? null : Path.GetFileNameWithoutExtension(script.Path);
    }

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

    private void CleanUnusedImagesOnOpen()
    {
        if (activeProject is null || IsPythonActive) return;
        var retained = StageSprites.Concat(StageBackdrops).SelectMany(o => o.ImagePaths)
            .Concat(stageSnapshot.State.Objects.SelectMany(o => (o.Costumes ?? []).Select(c => c.ImagePath).Append(o.ImagePath)));
        if (stageService.CleanUnusedImages(activeProject, retained, stageSnapshot.Revision, PythonCode) is { } message)
        { ProjectResourceMessage = message; OnPropertyChanged(nameof(ProjectResourceMessage)); }
    }

    private void ApplyRuntimeStage(RuntimeStageFrame frame)
    {
        if (lastRun is null || frame.SessionId != lastRun.SessionId || frame.Sequence <= stageSequence
            || runWorkspace != runtimeWorkspace || frame.Objects is null) return;
        var items = StageSprites.Concat(StageBackdrops).ToDictionary(o => o.ObjectId);
        if (frame.Objects.Length != items.Count || frame.Objects.Any(o => o is null || string.IsNullOrEmpty(o.ObjectId) || !items.ContainsKey(o.ObjectId))
            || frame.Objects.Select(o => o.ObjectId).Distinct().Count() != items.Count
            || frame.CurrentBackdropId is { } backdrop && !StageBackdrops.Any(b => b.ObjectId == backdrop)) return;
        try { foreach (var state in frame.Objects) StageRules.ValidateRuntimeObject(state); }
        catch (ProjectFormatException) { return; }
        stageSequence = frame.Sequence; hasRuntimePreview = true; runtimeBackdropId = frame.CurrentBackdropId;
        foreach (var state in frame.Objects)
        {
            var item = items[state.ObjectId];
            var previousImage = item.RuntimeImagePath;
            item.ApplyRuntimeState(state);
            if (state.ImagePath is not null && !string.Equals(previousImage, state.ImagePath, StringComparison.OrdinalIgnoreCase))
                ApplyRuntimeStageImage(item, state);
        }
        OnPropertyChanged(nameof(DisplayBackdrop));
        OnPropertyChanged(nameof(HasRuntimeStagePreview));
        RefreshStageInspectorState();
    }

    private void ApplyRuntimeStageImage(StageObjectItem item, StageObjectState state)
    {
        if (state.ImagePath is null) return;
        if (!item.Costumes.Any(c => string.Equals(c.ImagePath, state.ImagePath, StringComparison.OrdinalIgnoreCase))
            && !string.Equals(item.ImagePath, state.ImagePath, StringComparison.OrdinalIgnoreCase))
        {
            item.SetRuntimeImage(state.ImagePath, null, "运行时造型不属于当前对象。");
            return;
        }
        if (string.Equals(item.ImagePath, state.ImagePath, StringComparison.OrdinalIgnoreCase))
        {
            item.SetRuntimeImage(state.ImagePath, null, "当前使用初始造型图片。");
            return;
        }
        Bitmap? bitmap = null;
        try
        {
            var bytes = StageImageLoader.Read(Path.Combine(activeProject!.RootPath, state.ImagePath.Replace('/', Path.DirectorySeparatorChar)));
            using var input = new MemoryStream(bytes);
            bitmap = new Bitmap(input);
            item.SetRuntimeImage(state.ImagePath, bitmap,
                $"运行时造型（{bitmap.PixelSize.Width} × {bitmap.PixelSize.Height}）");
            bitmap = null;
        }
        catch (Exception)
        {
            item.SetRuntimeImage(state.ImagePath, null, "运行时造型图片缺失或无法读取。");
        }
        finally { bitmap?.Dispose(); }
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
        LoadInitialStageImage(item);
        item.IsStageReadOnly = IsPythonActive;
        item.InitialEditRequested += BeginStagePropertyEdit;
        item.PropertyChanged += OnStageItemChanged;
    }

    private void LoadInitialStageImage(StageObjectItem item)
    {
        if (item.ImagePath is not { } image)
        {
            item.SetInitialImage(null, null, "尚未设置图片");
            return;
        }
        Bitmap? bitmap = null;
        try
        {
            var bytes = StageImageLoader.Read(Path.Combine(activeProject!.RootPath, image.Replace('/', Path.DirectorySeparatorChar)));
            using var input = new MemoryStream(bytes);
            bitmap = new Bitmap(input);
            item.SetInitialImage(image, bitmap, $"已设置图片（{bitmap.PixelSize.Width} × {bitmap.PixelSize.Height}）", item.SelectedCostume?.NormalizeImage);
            bitmap = null;
        }
        catch (Exception) { item.SetInitialImage(image, null, "图片缺失或无法读取，请重新导入。", item.SelectedCostume?.NormalizeImage); }
        finally { bitmap?.Dispose(); }
    }

    private void OnStageItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!restoringStage && e.PropertyName is nameof(StageObjectItem.X) or nameof(StageObjectItem.Y) or nameof(StageObjectItem.Size)
            or nameof(StageObjectItem.Direction) or nameof(StageObjectItem.IsVisible) or nameof(StageObjectItem.Layer)
            or nameof(StageObjectItem.ImagePath) or nameof(StageObjectItem.SelectedCostume) or nameof(StageObjectItem.ClickEnabled))
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
        var retained = StageSprites.Concat(StageBackdrops).SelectMany(o => o.ImagePaths)
            .Concat(stageSnapshot.State.Objects.SelectMany(o => (o.Costumes ?? []).Select(c => c.ImagePath).Append(o.ImagePath)));
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
            if (!item.IsSprite)
            {
                // Backdrops keep their previous replacement semantics; only roles use a costume list.
                foreach (var old in item.Costumes.ToList()) item.RemoveCostume(old);
                item.Size = 100;
            }
            var name = StageCostumeRules.SuggestName(sourcePath, item.Costumes.Select(c => c.Name));
            var costume = new StageCostumeItem("costume-" + Guid.NewGuid().ToString("N"), name, path, true);
            var selectAsDefault = !item.IsSprite || item.SelectedCostume is null;
            if (item.Costumes.Count == 0 && item.IsSprite) item.Size = 100;
            item.AddCostume(costume, select: selectAsDefault);
            if (selectAsDefault)
            {
                item.SetInitialImage(path, bitmap, Path.GetFileName(sourcePath), normalize: true);
                bitmap = null;
            }
            stageDirty = true;
            ProjectResourceMessage = selectAsDefault
                ? "造型已添加并设为默认造型；保存项目后写入舞台文件。"
                : "造型已添加；当前默认造型保持不变。";
            CleanStageImages();
        }
        catch (Exception ex) { ProjectResourceMessage = "造型未添加：" + ex.Message; }
        finally { bitmap?.Dispose(); OnPropertyChanged(nameof(ProjectResourceMessage)); }
        return Task.CompletedTask;
    }

    public bool RenameStageCostume(StageObjectItem item, StageCostumeItem costume, string name)
    {
        if (activeProject is null || IsPythonActive || !item.Costumes.Contains(costume)) return false;
        try
        {
            name = StageCostumeRules.ValidateName(name);
            if (item.Costumes.Any(c => !ReferenceEquals(c, costume) && c.Name == name))
                throw new ProjectOperationException("同一角色内的造型名称不能重复。");
            if (costume.Name == name) return true;
            BeginStagePropertyEdit();
            costume.Name = name;
            item.NotifyCostumes();
            stageDirty = true;
            ProjectResourceMessage = "造型名称已修改；保存项目后写入舞台文件。";
            OnPropertyChanged(nameof(ProjectResourceMessage));
            return true;
        }
        catch (Exception ex)
        {
            ProjectResourceMessage = "造型名称未修改：" + ex.Message;
            OnPropertyChanged(nameof(ProjectResourceMessage));
            return false;
        }
    }

    public void SelectInitialStageCostume(StageObjectItem item, StageCostumeItem costume)
    {
        if (activeProject is null || IsPythonActive || !item.Costumes.Contains(costume)
            || ReferenceEquals(item.SelectedCostume, costume)) return;
        BeginStagePropertyEdit();
        item.SelectInitialCostume(costume);
        LoadInitialStageImage(item);
        stageDirty = true;
        ProjectResourceMessage = "初始造型已修改；保存项目后写入舞台文件。";
        OnPropertyChanged(nameof(ProjectResourceMessage));
    }

    public void RemoveStageCostume(StageObjectItem item, StageCostumeItem costume)
    {
        if (activeProject is null || IsPythonActive || !item.Costumes.Contains(costume)) return;
        BeginStagePropertyEdit();
        var wasInitial = ReferenceEquals(item.SelectedCostume, costume);
        item.RemoveCostume(costume);
        if (wasInitial) LoadInitialStageImage(item);
        stageDirty = true;
        ProjectResourceMessage = "造型已移除；保存项目后清理未引用图片。";
        OnPropertyChanged(nameof(ProjectResourceMessage));
    }
}
