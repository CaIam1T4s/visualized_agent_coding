using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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

    private void ResetStage(StageStorageSnapshot snapshot)
    {
        foreach (var item in StageSprites.Concat(StageBackdrops)) { item.PropertyChanged -= OnStageItemChanged; item.Dispose(); }
        StageSprites.Clear(); StageBackdrops.Clear();
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
        item.PropertyChanged += OnStageItemChanged;
    }

    private void OnStageItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!restoringStage && e.PropertyName is nameof(StageObjectItem.X) or nameof(StageObjectItem.Y) or nameof(StageObjectItem.Size)
            or nameof(StageObjectItem.Direction) or nameof(StageObjectItem.IsVisible) or nameof(StageObjectItem.Layer) or nameof(StageObjectItem.ImagePath))
            stageDirty = true;
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
        if (activeProject is null) return;
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
        if (activeProject is null || !StageSprites.Concat(StageBackdrops).Contains(item)) return Task.CompletedTask;
        Bitmap? bitmap = null;
        try
        {
            var bytes = StageImageLoader.Read(sourcePath);
            using var input = new MemoryStream(bytes);
            bitmap = new Bitmap(input);
            var path = stageService.ImportImage(activeProject, item.ObjectId, sourcePath, bytes);
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
