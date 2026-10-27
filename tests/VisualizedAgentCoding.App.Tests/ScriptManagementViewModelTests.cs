using happy_agent_coding.Services;
using happy_agent_coding.ViewModels;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Domain.Projects;
using VisualizedAgentCoding.Infrastructure.Projects;

namespace VisualizedAgentCoding.App.Tests;

public sealed class ScriptManagementViewModelTests : IDisposable
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
        vm.OpenProject(root);
    }

    [Fact]
    public void RenameSavesUnsavedCodeAndKeepsTheSameDocumentOpen()
    {
        var item = Assert.Single(vm.UserScripts);
        vm.OpenScriptFromExplorerCommand.Execute(item);
        vm.PythonCode = "value = 123\n";
        vm.StartRenameScriptCommand.Execute(item);
        var rename = Assert.IsType<ScriptRenameState>(vm.ScriptRename);
        Assert.Equal("hero", rename.Name);
        Assert.Same(rename, Assert.Single(vm.UserScripts).RenameState);
        rename.Name = "helpers";
        rename.ConfirmCommand.Execute(null);
        Assert.Null(vm.ScriptRename);
        Assert.Equal(item.Id, vm.OpenScript!.Id);
        Assert.Equal("helpers.py", vm.FileName);
        Assert.Equal("value = 123\n", vm.PythonCode);
        Assert.Equal(vm.PythonCode, File.ReadAllText(Path.Combine(root, "scripts/user/helpers.py")));
        Assert.False(File.Exists(Path.Combine(root, "scripts/user/hero.py")));
        Assert.True(vm.IsEditorVisible);
    }

    [Fact]
    public void InvalidNameKeepsInlineEditorAndCanBeCorrected()
    {
        vm.StartRenameScriptCommand.Execute(Assert.Single(vm.UserScripts));
        var rename = vm.ScriptRename!;
        rename.Name = "bad name";
        rename.ConfirmCommand.Execute(null);
        Assert.Same(rename, vm.ScriptRename);
        Assert.True(rename.HasError);
        rename.Name = "valid";
        rename.ConfirmCommand.Execute(null);
        Assert.Null(vm.ScriptRename);
        Assert.Equal("valid", Assert.Single(vm.UserScripts).DisplayName);
    }

    [Fact]
    public void RenamePreservesSearchAndMayRemoveTheItemFromResults()
    {
        vm.ResourceSearchQuery = "hero";
        vm.StartRenameScriptCommand.Execute(Assert.Single(vm.UserScripts));
        vm.ScriptRename!.Name = "helpers";
        vm.ScriptRename.ConfirmCommand.Execute(null);
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
    public void DeleteRequiresConfirmationAndCancelKeepsUnsavedBuffer()
    {
        var item = Assert.Single(vm.UserScripts);
        vm.OpenScriptFromExplorerCommand.Execute(item);
        vm.PythonCode = "unsaved";
        vm.ConfirmDeleteScriptCommand.Execute(null);
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
    public void DeletingCurrentDocumentClearsEditorAndDoesNotRecreateItOnClose()
    {
        var item = Assert.Single(vm.UserScripts);
        vm.OpenScriptFromExplorerCommand.Execute(item);
        vm.PythonCode = "discard this buffer";
        Delete(item);
        Assert.Null(vm.OpenScript);
        Assert.Empty(vm.PythonCode);
        Assert.Empty(vm.FileName);
        Assert.False(vm.IsEditorVisible);
        Assert.Empty(vm.UserScripts);
        vm.CloseProject();
        Assert.False(File.Exists(Path.Combine(root, item.RelativePath)));
    }

    [Fact]
    public void DeletingOtherDocumentPreservesAndSavesCurrentBuffer()
    {
        var item = Assert.Single(vm.UserScripts);
        vm.OpenScriptFromExplorerCommand.Execute(item);
        vm.PythonCode = "keep me";
        Delete(Assert.Single(vm.ObjectScripts));
        Assert.Equal(item.Id, vm.OpenScript!.Id);
        Assert.Equal("keep me", vm.PythonCode);
        Assert.Equal("keep me", File.ReadAllText(Path.Combine(root, item.RelativePath)));
    }

    [Theory]
    [InlineData(ProjectScriptKind.Object)]
    [InlineData(ProjectScriptKind.Backdrop)]
    public void DeletingStageObjectDeletesOnlyItsLinkedScriptAndHandlers(ProjectScriptKind kind)
    {
        var item = vm.ScriptCategories.Single(c => c.Kind == kind).Scripts.Single(s => s.DisplayName == "hero");
        service.RegisterEventHandler(service.Open(root), item.Id,
            new EventHandlerRegistration(ProjectEventKinds.Start, null, "on_start"));
        vm.OpenProject(root);
        var selected = kind == ProjectScriptKind.Object ? vm.StageSprites.Single() : vm.StageBackdrops.First();
        vm.SelectStageObjectCommand.Execute(selected);
        vm.RequestDeleteScriptCommand.Execute(selected.ToScriptItem());
        Assert.Contains(kind == ProjectScriptKind.Object ? "角色" : "背景", vm.DeleteConfirmationMessage);
        vm.ConfirmDeleteScriptCommand.Execute(null);
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
    public void SaveFailureAbortsRenameAndPreservesBuffer()
    {
        var item = Assert.Single(vm.UserScripts);
        vm.OpenScriptFromExplorerCommand.Execute(item);
        vm.PythonCode = "unsaved";
        session.FailSave = true;
        vm.StartRenameScriptCommand.Execute(item);
        vm.ScriptRename!.Name = "new_name";
        vm.ScriptRename.ConfirmCommand.Execute(null);
        Assert.True(vm.ScriptRename.HasError);
        Assert.Equal("hero.py", vm.FileName);
        Assert.Equal("unsaved", vm.PythonCode);
        Assert.True(File.Exists(Path.Combine(root, item.RelativePath)));
    }

    [Fact]
    public void DeleteFailureKeepsDialogDocumentAndStageObjectForRetry()
    {
        var item = Assert.Single(vm.ObjectScripts);
        vm.OpenScriptFromExplorerCommand.Execute(item);
        vm.PythonCode = "unsaved";
        session.FailDelete = true;
        Delete(item);
        Assert.True(vm.IsDeleteConfirmationOpen);
        Assert.NotNull(vm.DeleteError);
        Assert.Equal(item.Id, vm.OpenScript!.Id);
        Assert.Equal("unsaved", vm.PythonCode);
        Assert.Single(vm.StageSprites);
        session.FailDelete = false;
        vm.ConfirmDeleteScriptCommand.Execute(null);
        Assert.False(vm.IsDeleteConfirmationOpen);
        Assert.Empty(vm.StageSprites);
    }

    [Fact]
    public void DeferredCleanupIsExplainedToTheUser()
    {
        session.PendingCleanup = true;
        Delete(Assert.Single(vm.UserScripts));
        Assert.Contains("重试清理", vm.ProjectResourceMessage);
    }

    [Fact]
    public void ProjectCloseClearsPendingActions()
    {
        vm.RequestDeleteScriptCommand.Execute(Assert.Single(vm.UserScripts));
        vm.CloseProject();
        Assert.False(vm.IsDeleteConfirmationOpen);
        vm.ConfirmDeleteScriptCommand.Execute(null);
        Assert.True(File.Exists(Path.Combine(root, "scripts/user/hero.py")));
        vm.OpenProject(root);
        vm.StartRenameScriptCommand.Execute(Assert.Single(vm.UserScripts));
        vm.CloseProject();
        Assert.Null(vm.ScriptRename);
    }

    private void Delete(ProjectScriptItem item)
    {
        vm.RequestDeleteScriptCommand.Execute(item);
        vm.ConfirmDeleteScriptCommand.Execute(null);
    }

    public void Dispose() => Directory.Delete(root, true);

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
        public ProjectWorkspace RenameScript(ProjectWorkspace p, string id, string n) => inner.RenameScript(p, id, n);
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
