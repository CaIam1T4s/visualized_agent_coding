using happy_agent_coding.Services;
using happy_agent_coding.ViewModels;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Application.Editor.ObjectScripts;
using VisualizedAgentCoding.Application.Ports;
using VisualizedAgentCoding.Application.Python;
using VisualizedAgentCoding.Domain.Projects;
using VisualizedAgentCoding.Infrastructure.Projects;
using VisualizedAgentCoding.Infrastructure.Python;

namespace VisualizedAgentCoding.App.Tests;

public sealed class ObjectScriptSaveViewModelTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "object-save-ui-" + Guid.NewGuid().ToString("N"));
    public ObjectScriptSaveViewModelTests()
    {
        var projects = new ProjectService(new JsonProjectStore());
        var project = projects.CreateScript(projects.Create(root, "Demo"), ProjectScriptKind.Object, "hero").Project;
        projects.CreateScript(project, ProjectScriptKind.Backdrop, "forest");
    }
    private static string Insert(string source, string user) => source.Replace("    # vac:user-code-end", user + "    # vac:user-code-end");
    private string HeroPath => Path.Combine(root, "scripts/objects/hero.py");
    private async Task<MainViewModel> OpenHero()
    {
        var vm = new MainViewModel(new Settings(), new InMemoryRecentProjectsStore(), new ProjectSession(), new PythonProcessBridge());
        await vm.OpenProject(root);
        await vm.OpenScriptFromExplorerCommand.ExecuteAsync(Assert.Single(vm.ObjectScripts));
        return vm;
    }

    [Fact]
    public async Task DedentedUserCodeRemainsEditableAfterReloadAndSave()
    {
        File.WriteAllText(HeroPath, Insert(File.ReadAllText(HeroPath), "sdf\n"));
        await using var vm = await OpenHero();
        var document = await vm.InspectEditorProtectionAsync(CancellationToken.None);
        Assert.True(document!.HasUserRegion);
        Assert.Equal("sdf\n", document.UserSource);
        vm.PythonCode = vm.PythonCode.Replace("sdf\n", "    value = 1\n");
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal("已保存", vm.EditorState);
        Assert.Equal(vm.PythonCode, File.ReadAllText(HeroPath));
        vm.RequestReloadScriptCommand.Execute(null);
        await vm.ConfirmReloadScriptCommand.ExecuteAsync(null);
        document = await vm.InspectEditorProtectionAsync(CancellationToken.None);
        Assert.True(document!.HasUserRegion);
        Assert.Equal("    value = 1\n", document.UserSource);
    }

    [Fact]
    public async Task ProtectionUsesTrustedShellWhileUserCodeIsIncompleteAndRefreshesAfterReload()
    {
        await using var vm = await OpenHero();
        var original = vm.PythonCode;
        vm.PythonCode = Insert(original, "    value = \"\"\"unfinished\n");
        var document = await vm.InspectEditorProtectionAsync(CancellationToken.None);
        Assert.NotNull(document);
        Assert.True(document.HasUserRegion);
        Assert.Equal("    value = \"\"\"unfinished\n", document.UserSource);
        await vm.SaveCommand.ExecuteAsync(null);
        document = await vm.InspectEditorProtectionAsync(CancellationToken.None);
        Assert.True(document!.HasUserRegion);
        vm.PythonCode = vm.PythonCode.Replace("class Hero", "class Changed");
        Assert.Null(await vm.InspectEditorProtectionAsync(CancellationToken.None));
        File.WriteAllText(HeroPath, original);
        var revision = vm.EditorSourceRevision;
        vm.RequestReloadScriptCommand.Execute(null);
        await vm.ConfirmReloadScriptCommand.ExecuteAsync(null);
        Assert.True(vm.EditorSourceRevision > revision);
        document = await vm.InspectEditorProtectionAsync(CancellationToken.None);
        Assert.True(document!.HasUserRegion);
        Assert.Equal(string.Empty, document.UserSource);
    }

    [Fact]
    public async Task MixedIndentationInOpenedFileDoesNotPreventSavingUserContent()
    {
        var original = File.ReadAllText(HeroPath);
        var mixed = Insert(original, "\tvalue = j\n");
        File.WriteAllText(HeroPath, mixed);
        await using var vm = await OpenHero();
        var edited = mixed.Replace("value = j", "value = zz");
        vm.PythonCode = edited;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal("已保存", vm.EditorState);
        Assert.Equal(edited, File.ReadAllText(HeroPath));
        Assert.Contains("\tvalue = zz", vm.PythonCode);
        Assert.True(await vm.PrepareForExitAsync());
    }

    [Fact]
    public async Task ManualSwitchAndExitUseSameRegionMerge()
    {
        await using var vm = await OpenHero();
        var original = vm.PythonCode;
        vm.PythonCode = Insert(original, "    value = 1\n");
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(vm.PythonCode, File.ReadAllText(HeroPath));
        vm.PythonCode = Insert(original, "    value = 2\n");
        await vm.OpenScriptFromExplorerCommand.ExecuteAsync(Assert.Single(vm.BackdropScripts));
        Assert.Contains("value = 2", File.ReadAllText(HeroPath));
        Assert.Equal("forest.py", vm.FileName);
        var backdrop = vm.PythonCode;
        vm.PythonCode = Insert(backdrop, "    value =\n");
        Assert.True(await vm.PrepareForExitAsync());
        Assert.Equal(vm.PythonCode, File.ReadAllText(Path.Combine(root, "scripts/backdrops/forest.py")));
    }

    [Fact]
    public async Task ExternalConflictKeepsBufferBlocksNavigationAndExitUntilExplicitReload()
    {
        await using var vm = await OpenHero();
        var original = vm.PythonCode;
        var local = Insert(original, "    local = 1\n");
        var external = Insert(original, "    external = 2\n");
        vm.PythonCode = local;
        File.WriteAllText(HeroPath, external);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Contains("外部修改", vm.EditorState);
        Assert.Equal(local, vm.PythonCode);
        Assert.Equal(external, File.ReadAllText(HeroPath));
        await vm.OpenScriptFromExplorerCommand.ExecuteAsync(Assert.Single(vm.BackdropScripts));
        Assert.Equal("hero.py", vm.FileName);
        Assert.Equal(local, vm.PythonCode);
        Assert.False(await vm.PrepareForExitAsync());
        vm.RequestReloadScriptCommand.Execute(null);
        vm.CancelReloadScriptCommand.Execute(null);
        Assert.Equal(local, vm.PythonCode);
        vm.RequestReloadScriptCommand.Execute(null);
        await vm.ConfirmReloadScriptCommand.ExecuteAsync(null);
        Assert.Equal(external, vm.PythonCode);
        vm.PythonCode = Insert(original, "    value = 3\n");
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(vm.PythonCode, File.ReadAllText(HeroPath));
    }

    [Fact]
    public async Task SystemEditIsNotSavedAndCanBeCorrectedWithoutLosingBuffer()
    {
        await using var vm = await OpenHero();
        var original = vm.PythonCode;
        vm.PythonCode = original.Replace("class Hero", "class Changed");
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Contains("系统区域", vm.EditorState);
        Assert.Contains("Changed", vm.PythonCode);
        Assert.Equal(original, File.ReadAllText(HeroPath));
        vm.PythonCode = Insert(original, "    value = 42\n");
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(vm.PythonCode, File.ReadAllText(HeroPath));
    }

    [Fact]
    public async Task NoLocalEditDoesNotOverwriteExternalContentWhenNavigating()
    {
        await using var vm = await OpenHero();
        var external = Insert(vm.PythonCode, "    external = 1\n");
        File.WriteAllText(HeroPath, external);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Contains("外部修改", vm.EditorState);
        await vm.OpenScriptFromExplorerCommand.ExecuteAsync(Assert.Single(vm.BackdropScripts));
        Assert.Equal("forest.py", vm.FileName);
        Assert.Equal(external, File.ReadAllText(HeroPath));
    }

    [Fact]
    public async Task EditingDuringAnalysisRejectsStaleSaveAndRetryKeepsNewestText()
    {
        var bridge = new DelayedBridge();
        await using var vm = new MainViewModel(new Settings(), new InMemoryRecentProjectsStore(), new ProjectSession(), bridge);
        await vm.OpenProject(root);
        await vm.OpenScriptFromExplorerCommand.ExecuteAsync(Assert.Single(vm.ObjectScripts));
        var original = vm.PythonCode;
        vm.PythonCode = Insert(original, "    value = 1\n");
        var saving = vm.SaveCommand.ExecuteAsync(null);
        await bridge.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var newest = Insert(original, "    value = 2\n");
        vm.PythonCode = newest;
        bridge.Release.SetResult();
        await saving;
        Assert.Equal(newest, vm.PythonCode);
        Assert.Equal(original, File.ReadAllText(HeroPath));
        Assert.Contains("已变化", vm.EditorState);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(newest, File.ReadAllText(HeroPath));
    }

    private sealed class DelayedBridge : IPythonBridge
    {
        public readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ObjectScriptInspectionResult> InspectObjectScriptAsync(ObjectScriptInspectionRequest request, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            var start = request.Source.IndexOf("# vac:user-code-begin\n", StringComparison.Ordinal) + "# vac:user-code-begin\n".Length;
            var end = request.Source.IndexOf("    # vac:user-code-end", StringComparison.Ordinal);
            return new(request.Uri, request.Version, "recognized", 1, new(start, end - start), []);
        }
        public Task<PythonHelperInfo> InitializeAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PythonParseResult> ParseAsync(PythonParseRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PythonReferencesResult> FindReferencesAsync(PythonReferencesRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    public void Dispose() => Directory.Delete(root, true);
    private sealed class Settings : ISettingsStore
    {
        public AppSettings Load() => new();
        public void Save(AppSettings settings) { }
    }
}
