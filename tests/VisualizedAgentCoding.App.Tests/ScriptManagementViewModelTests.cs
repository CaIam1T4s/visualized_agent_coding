using happy_agent_coding.Services;
using happy_agent_coding.ViewModels;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Domain.Projects;
using VisualizedAgentCoding.Infrastructure.Projects;
using VisualizedAgentCoding.Infrastructure.Python;
using VisualizedAgentCoding.Application.Ports;
using VisualizedAgentCoding.Application.Python;

namespace VisualizedAgentCoding.App.Tests;

public sealed class ScriptManagementViewModelTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"script-ui-{Guid.NewGuid():N}");
    private readonly ProjectService service = new(new JsonProjectStore());
    private readonly TestSession session = new();
    private readonly MainViewModel vm;

    public ScriptManagementViewModelTests()
    {
        var project = service.Create(root, "Demo");
        foreach (var kind in new[] { ProjectScriptKind.User, ProjectScriptKind.Object, ProjectScriptKind.Backdrop })
            project = service.CreateScript(project, kind, "hero").Project;
        service.CreateScript(project, ProjectScriptKind.Backdrop, "night");
        vm = new MainViewModel(new Settings(), new InMemoryRecentProjectsStore(), session);
    }

    public Task InitializeAsync() => vm.OpenProject(root);

    [Fact]
    public async Task DeleteAnalysisBlocksConfirmationAndIgnoresCancelledResult()
    {
        var bridge = new PendingReferenceBridge();
        await using var model = new MainViewModel(new Settings(), new InMemoryRecentProjectsStore(), session, bridge);
        await model.OpenProject(root);
        var item = Assert.Single(model.UserScripts);
        model.RequestDeleteScriptCommand.Execute(item);
        await bridge.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(model.CanConfirmDelete);
        await model.ConfirmDeleteScriptCommand.ExecuteAsync(null);
        Assert.True(File.Exists(Path.Combine(root, item.RelativePath)));
        model.CancelDeleteScriptCommand.Execute(null);
        bridge.Complete.SetResult(new(bridge.Request!.Uri, bridge.Request.Version, [], [], false));
        Assert.False(model.IsDeleteConfirmationOpen);
        Assert.False(model.CanConfirmDelete);
    }

    [Fact]
    public void UnavailableAnalysisShowsUnknownImpactAndRequiresExplicitDelete()
    {
        vm.RequestDeleteScriptCommand.Execute(Assert.Single(vm.UserScripts));
        Assert.True(vm.CanConfirmDelete);
        Assert.Contains("无法完成", vm.DeleteReferenceStatus);
        Assert.Equal("仍然删除", vm.DeleteButtonText);
        Assert.NotEmpty(vm.UserScripts);
    }

    private sealed class PendingReferenceBridge : IPythonBridge
    {
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<PythonReferencesResult> Complete = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public PythonModuleDependenciesRequest? Request;
        public Task<PythonReferencesResult> FindReferencesAsync(PythonReferencesRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PythonReferencesResult> FindModuleDependenciesAsync(PythonModuleDependenciesRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            Started.SetResult();
            return Complete.Task;
        }
        public Task<PythonHelperInfo> InitializeAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PythonParseResult> ParseAsync(PythonParseRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task RenamingAnotherScriptUpdatesOpenUnsavedImporter()
    {
        service.CreateScript(service.Open(root), ProjectScriptKind.User, "caller");
        await using var model = await CreateRenameModelAsync();
        var target = model.UserScripts.Single(item => item.DisplayName == "hero");
        await model.OpenScriptFromExplorerCommand.ExecuteAsync(model.UserScripts.Single(item => item.DisplayName == "caller"));
        model.PythonCode = "import hero\nhero.value\n";
        model.StartRenameScriptCommand.Execute(target);
        model.ScriptRename!.Name = "helpers";
        await model.ScriptRename.ConfirmCommand.ExecuteAsync(null);
        Assert.True(model.CanApplyRename);
        Assert.Single(model.RenameChanges);
        model.ApplyRenameCommand.Execute(null);
        Assert.Equal("caller.py", model.FileName);
        Assert.Equal("import helpers\nhelpers.value\n", model.PythonCode);
        Assert.Equal(model.PythonCode, File.ReadAllText(Path.Combine(root, "scripts/user/caller.py")));
    }

    [Fact]
    public async Task RenamePreviewCanBeCancelledWithoutSavingOrChangingFiles()
    {
        await using var model = await CreateRenameModelAsync();
        var item = Assert.Single(model.UserScripts);
        await model.OpenScriptFromExplorerCommand.ExecuteAsync(item);
        model.PythonCode = "value = 42\n";
        var disk = File.ReadAllText(Path.Combine(root, item.RelativePath));
        model.StartRenameScriptCommand.Execute(item);
        model.ScriptRename!.Name = "helpers";
        await model.ScriptRename.ConfirmCommand.ExecuteAsync(null);
        Assert.True(model.IsRenamePreviewOpen);
        Assert.True(model.CanApplyRename);
        Assert.Equal(disk, File.ReadAllText(Path.Combine(root, item.RelativePath)));
        model.CancelRenameScriptCommand.Execute(null);
        Assert.False(model.IsRenamePreviewOpen);
        Assert.Equal("value = 42\n", model.PythonCode);
        Assert.False(File.Exists(Path.Combine(root, "scripts/user/helpers.py")));
    }

    [Fact]
    public async Task ChangedEditorBufferInvalidatesRenamePreview()
    {
        await using var model = await CreateRenameModelAsync();
        var item = Assert.Single(model.UserScripts);
        await model.OpenScriptFromExplorerCommand.ExecuteAsync(item);
        model.StartRenameScriptCommand.Execute(item);
        model.ScriptRename!.Name = "helpers";
        await model.ScriptRename.ConfirmCommand.ExecuteAsync(null);
        model.PythonCode = "newer = 42\n";
        model.ApplyRenameCommand.Execute(null);
        Assert.False(model.CanApplyRename);
        Assert.NotNull(model.RenamePreviewError);
        Assert.True(File.Exists(Path.Combine(root, item.RelativePath)));
        Assert.Equal("newer = 42\n", model.PythonCode);
    }

    [Fact]
    public async Task RenameSavesUnsavedCodeAndKeepsTheSameDocumentOpen()
    {
        await using var vm = await CreateRenameModelAsync();
        var item = Assert.Single(vm.UserScripts);
        await vm.OpenScriptFromExplorerCommand.ExecuteAsync(item);
        vm.PythonCode = "value = 123\n";
        vm.StartRenameScriptCommand.Execute(item);
        var rename = Assert.IsType<ScriptRenameState>(vm.ScriptRename);
        Assert.Equal("hero", rename.Name);
        Assert.Same(rename, Assert.Single(vm.UserScripts).RenameState);
        rename.Name = "helpers";
        await rename.ConfirmCommand.ExecuteAsync(null);
        if (vm.CanApplyRename) vm.ApplyRenameCommand.Execute(null);
        Assert.Null(vm.ScriptRename);
        Assert.Equal(item.Id, vm.OpenScript!.Id);
        Assert.Equal("helpers.py", vm.FileName);
        Assert.Equal("value = 123\n", vm.PythonCode);
        Assert.Equal(vm.PythonCode, File.ReadAllText(Path.Combine(root, "scripts/user/helpers.py")));
        Assert.False(File.Exists(Path.Combine(root, "scripts/user/hero.py")));
        Assert.True(vm.IsEditorVisible);
    }

    [Fact]
    public async Task InvalidNameKeepsInlineEditorAndCanBeCorrected()
    {
        await using var vm = await CreateRenameModelAsync();
        vm.StartRenameScriptCommand.Execute(Assert.Single(vm.UserScripts));
        var rename = vm.ScriptRename!;
        rename.Name = "bad name";
        await rename.ConfirmCommand.ExecuteAsync(null);
        if (vm.CanApplyRename) vm.ApplyRenameCommand.Execute(null);
        Assert.Same(rename, vm.ScriptRename);
        Assert.True(rename.HasError);
        vm.BackToRenameCommand.Execute(null);
        rename.Name = "valid";
        await rename.ConfirmCommand.ExecuteAsync(null);
        if (vm.CanApplyRename) vm.ApplyRenameCommand.Execute(null);
        Assert.Null(vm.ScriptRename);
        Assert.Equal("valid", Assert.Single(vm.UserScripts).DisplayName);
    }

    [Fact]
    public async Task RenamePreservesSearchAndMayRemoveTheItemFromResults()
    {
        await using var vm = await CreateRenameModelAsync();
        vm.ResourceSearchQuery = "hero";
        vm.StartRenameScriptCommand.Execute(Assert.Single(vm.UserScripts));
        vm.ScriptRename!.Name = "helpers";
        await vm.ScriptRename.ConfirmCommand.ExecuteAsync(null);
        if (vm.CanApplyRename) vm.ApplyRenameCommand.Execute(null);
        Assert.Equal("hero", vm.ResourceSearchQuery);
        Assert.Empty(vm.UserScripts);
        Assert.Contains("helpers.py", vm.ProjectResourceMessage);
    }

    [Fact]
    public void CancelRenameDoesNotChangeFiles()
    {
        vm.StartRenameScriptCommand.Execute(Assert.Single(vm.UserScripts));
        vm.ScriptRename!.Name = "other";
        vm.ScriptRename.CancelCommand.Execute(null);
        Assert.Null(vm.ScriptRename);
        Assert.Equal("hero", Assert.Single(vm.UserScripts).DisplayName);
        Assert.True(File.Exists(Path.Combine(root, "scripts/user/hero.py")));
    }

    [Fact]
    public void ObjectsAndBackgroundsRefuseRename()
    {
        foreach (var item in vm.ObjectScripts.Concat(vm.BackdropScripts))
        {
            vm.StartRenameScriptCommand.Execute(item);
            Assert.Null(vm.ScriptRename);
        }
    }

    [Fact]
    public async Task DeleteRequiresConfirmationAndCancelKeepsUnsavedBuffer()
    {
        var item = Assert.Single(vm.UserScripts);
        await vm.OpenScriptFromExplorerCommand.ExecuteAsync(item);
        vm.PythonCode = "unsaved";
        await vm.ConfirmDeleteScriptCommand.ExecuteAsync(null);
        Assert.True(File.Exists(Path.Combine(root, item.RelativePath)));
        vm.RequestDeleteScriptCommand.Execute(item);
        Assert.True(vm.IsDeleteConfirmationOpen);
        Assert.Equal(item.RelativePath, vm.DeleteConfirmationPath);
        Assert.Equal("删除 hero.py：", vm.DeleteConfirmationTitle);
        vm.CancelDeleteScriptCommand.Execute(null);
        Assert.False(vm.IsDeleteConfirmationOpen);
        Assert.Equal("unsaved", vm.PythonCode);
        Assert.Equal(item.Id, vm.OpenScript!.Id);
    }

    [Fact]
    public async Task DeletingCurrentDocumentClearsEditorAndDoesNotRecreateItOnClose()
    {
        var item = Assert.Single(vm.UserScripts);
        await vm.OpenScriptFromExplorerCommand.ExecuteAsync(item);
        vm.PythonCode = "discard this buffer";
        await DeleteAsync(item);
        Assert.Null(vm.OpenScript);
        Assert.Empty(vm.PythonCode);
        Assert.Empty(vm.FileName);
        Assert.False(vm.IsEditorVisible);
        Assert.Empty(vm.UserScripts);
        await vm.CloseProject();
        Assert.False(File.Exists(Path.Combine(root, item.RelativePath)));
    }

    [Fact]
    public async Task DeletingOtherDocumentPreservesAndSavesCurrentBuffer()
    {
        var item = Assert.Single(vm.UserScripts);
        await vm.OpenScriptFromExplorerCommand.ExecuteAsync(item);
        vm.PythonCode = "keep me";
        await DeleteAsync(Assert.Single(vm.ObjectScripts));
        Assert.Equal(item.Id, vm.OpenScript!.Id);
        Assert.Equal("keep me", vm.PythonCode);
        Assert.Equal("keep me", File.ReadAllText(Path.Combine(root, item.RelativePath)));
    }

    [Theory]
    [InlineData(ProjectScriptKind.Object)]
    [InlineData(ProjectScriptKind.Backdrop)]
    public async Task DeletingStageObjectDeletesOnlyItsLinkedScriptAndHandlers(ProjectScriptKind kind)
    {
        var item = vm.ScriptCategories.Single(c => c.Kind == kind).Scripts.Single(s => s.DisplayName == "hero");
        service.RegisterEventHandler(service.Open(root), item.Id,
            new EventHandlerRegistration(ProjectEventKinds.Start, null, "on_start"));
        await vm.OpenProject(root);
        var selected = kind == ProjectScriptKind.Object ? vm.StageSprites.Single() : vm.StageBackdrops.First();
        await vm.SelectStageObjectCommand.ExecuteAsync(selected);
        vm.RequestDeleteScriptCommand.Execute(selected.ToScriptItem());
        Assert.Contains(kind == ProjectScriptKind.Object ? "角色" : "背景", vm.DeleteConfirmationMessage);
        await vm.ConfirmDeleteScriptCommand.ExecuteAsync(null);
        Assert.False(File.Exists(Path.Combine(root, item.RelativePath)));
        Assert.DoesNotContain(service.Open(root).Manifest.Scripts, s => s.Id == item.Id);
        Assert.Empty(service.Open(root).Manifest.EventHandlers);
        Assert.True(File.Exists(Path.Combine(root, "scripts/user/hero.py")));
        if (kind == ProjectScriptKind.Object)
        {
            Assert.Empty(vm.StageSprites);
            Assert.Null(vm.SelectedSprite);
            Assert.True(File.Exists(Path.Combine(root, "scripts/backdrops/hero.py")));
        }
        else
        {
            Assert.Single(vm.StageBackdrops);
            Assert.Equal("night", vm.CurrentBackdrop!.DisplayName);
            Assert.Null(vm.SelectedBackdrop);
            Assert.True(File.Exists(Path.Combine(root, "scripts/objects/hero.py")));
        }
    }

    [Fact]
    public async Task UnavailableHelperAbortsRenameAndPreservesBuffer()
    {
        var item = Assert.Single(vm.UserScripts);
        await vm.OpenScriptFromExplorerCommand.ExecuteAsync(item);
        vm.PythonCode = "unsaved";
        session.FailSave = true;
        vm.StartRenameScriptCommand.Execute(item);
        vm.ScriptRename!.Name = "new_name";
        await vm.ScriptRename.ConfirmCommand.ExecuteAsync(null);
        if (vm.CanApplyRename) vm.ApplyRenameCommand.Execute(null);
        Assert.True(vm.ScriptRename.HasError);
        Assert.Equal("hero.py", vm.FileName);
        Assert.Equal("unsaved", vm.PythonCode);
        Assert.True(File.Exists(Path.Combine(root, item.RelativePath)));
    }

    [Fact]
    public async Task DeleteFailureKeepsDialogDocumentAndStageObjectForRetry()
    {
        var item = Assert.Single(vm.ObjectScripts);
        await vm.OpenScriptFromExplorerCommand.ExecuteAsync(item);
        vm.PythonCode = "unsaved";
        session.FailDelete = true;
        await DeleteAsync(item);
        Assert.True(vm.IsDeleteConfirmationOpen);
        Assert.NotNull(vm.DeleteError);
        Assert.Equal(item.Id, vm.OpenScript!.Id);
        Assert.Equal("unsaved", vm.PythonCode);
        Assert.Single(vm.StageSprites);
        session.FailDelete = false;
        await vm.ConfirmDeleteScriptCommand.ExecuteAsync(null);
        Assert.False(vm.IsDeleteConfirmationOpen);
        Assert.Empty(vm.StageSprites);
    }

    [Fact]
    public async Task DeferredCleanupIsExplainedToTheUser()
    {
        session.PendingCleanup = true;
        await DeleteAsync(Assert.Single(vm.UserScripts));
        Assert.Contains("重试清理", vm.ProjectResourceMessage);
    }

    [Fact]
    public async Task ProjectCloseClearsPendingActions()
    {
        vm.RequestDeleteScriptCommand.Execute(Assert.Single(vm.UserScripts));
        await vm.CloseProject();
        Assert.False(vm.IsDeleteConfirmationOpen);
        await vm.ConfirmDeleteScriptCommand.ExecuteAsync(null);
        Assert.True(File.Exists(Path.Combine(root, "scripts/user/hero.py")));
        await vm.OpenProject(root);
        vm.StartRenameScriptCommand.Execute(Assert.Single(vm.UserScripts));
        await vm.CloseProject();
        Assert.Null(vm.ScriptRename);
    }

    [Fact]
    public async Task ExitAutoSavePersistsTheUnsavedEditorBuffer()
    {
        var item = Assert.Single(vm.UserScripts);
        await vm.OpenScriptFromExplorerCommand.ExecuteAsync(item);
        vm.PythonCode = "value = 999\n";
        Assert.True(await vm.PrepareForExitAsync());
        Assert.Equal(vm.PythonCode, File.ReadAllText(Path.Combine(root, item.RelativePath)));
        Assert.Equal(item.Id, vm.OpenScript!.Id);
    }

    [Fact]
    public async Task ExitAutoSaveFailureKeepsDocumentOpenAndSupportsRetry()
    {
        await vm.OpenScriptFromExplorerCommand.ExecuteAsync(Assert.Single(vm.UserScripts));
        vm.PythonCode = "unsaved";
        session.FailSave = true;
        Assert.False(await vm.PrepareForExitAsync());
        Assert.Equal("unsaved", vm.PythonCode);
        Assert.NotNull(vm.OpenScript);
        session.FailSave = false;
        Assert.True(await vm.PrepareForExitAsync());
    }

    [Fact]
    public async Task ExitWithNoOpenDocumentDoesNotSave()
    {
        session.FailSave = true;
        Assert.True(await vm.PrepareForExitAsync());
    }

    private async Task<MainViewModel> CreateRenameModelAsync()
    {
        var model = new MainViewModel(new Settings(), new InMemoryRecentProjectsStore(), session, new PythonProcessBridge());
        await model.OpenProject(root);
        return model;
    }

    private async Task DeleteAsync(ProjectScriptItem item)
    {
        vm.RequestDeleteScriptCommand.Execute(item);
        await vm.ConfirmDeleteScriptCommand.ExecuteAsync(null);
    }

    public async Task DisposeAsync()
    {
        await vm.DisposeAsync();
        Directory.Delete(root, true);
    }

    private sealed class Settings : ISettingsStore
    {
        public AppSettings Load() => new();
        public void Save(AppSettings value) { }
    }

    private sealed class TestSession : IProjectSession
    {
        private readonly ProjectSession inner = new();
        public bool FailSave { get; set; }
        public bool FailDelete { get; set; }
        public bool PendingCleanup { get; set; }
        public ProjectWorkspace Create(string p, string? n = null) => inner.Create(p, n);
        public ProjectWorkspace Open(string p) => inner.Open(p);
        public bool IsProject(string? p) => inner.IsProject(p);
        public string LoadMainScript(ProjectWorkspace p) => inner.LoadMainScript(p);
        public void SaveMainScript(ProjectWorkspace p, string c) => inner.SaveMainScript(p, c);
        public string LoadScript(ProjectWorkspace p, ProjectScriptEntry s) => inner.LoadScript(p, s);
        public void SaveScript(ProjectWorkspace p, ProjectScriptEntry s, string c)
        {
            if (FailSave) throw new IOException("save failed");
            inner.SaveScript(p, s, c);
        }
        public ProjectScriptCreation CreateScript(ProjectWorkspace p, ProjectScriptKind k, string n) => inner.CreateScript(p, k, n);
        public ProjectWorkspace DeleteScript(ProjectWorkspace p, string id)
        {
            if (FailDelete) throw new IOException("delete failed");
            return inner.DeleteScript(p, id);
        }
        public bool HasPendingScriptCleanup(ProjectWorkspace p) => PendingCleanup || inner.HasPendingScriptCleanup(p);
        public ProjectWorkspace RegisterEventHandler(ProjectWorkspace p, string id, EventHandlerRegistration r) => inner.RegisterEventHandler(p, id, r);
        public ProjectWorkspace UnregisterEventHandler(ProjectWorkspace p, string id, string e, string? a = null) => inner.UnregisterEventHandler(p, id, e, a);
        public IReadOnlyList<ProjectEventHandler> MatchHandlers(ProjectWorkspace p, string e, string? a) => inner.MatchHandlers(p, e, a);
    }
}
