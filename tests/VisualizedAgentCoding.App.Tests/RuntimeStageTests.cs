using System.Collections.Concurrent;
using System.Reflection;
using happy_agent_coding.ViewModels;
using happy_agent_coding.Services;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Application.Runtime;
using VisualizedAgentCoding.Domain.Projects;
using VisualizedAgentCoding.Infrastructure.Projects;
using VisualizedAgentCoding.Infrastructure.Python;

namespace VisualizedAgentCoding.App.Tests;

public sealed class RuntimeStageTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "vac-stage-run-" + Guid.NewGuid().ToString("N"));
    private PythonRunRequest Request(string body, string? backdrop = null) => new(
        Guid.NewGuid().ToString("N"), PythonEnvironment.DefaultExecutable, root, "scripts/main.py",
        [new("scripts/main.py", ManagedProjectEntry.Source), new("scripts/objects/cat.py", body),
            .. backdrop is null ? Array.Empty<PythonRunSource>() : [new PythonRunSource("scripts/backdrops/night.py", backdrop)]],
        [new("cat", "cat-id", "scripts/objects/cat.py", "object"),
            .. backdrop is null ? Array.Empty<PythonRunScript>() : [new PythonRunScript("night", "night-id", "scripts/backdrops/night.py", "backdrop")]],
        [], InitialStage: new StageState { Objects = [new("cat-id", 100, 50, 150, -30, true, 8)] });
    private async Task<(int Exit, PythonRunEvent[] Events)> Run(PythonRunRequest request)
    {
        Directory.CreateDirectory(root);
        await using var runtime = new PythonRuntimeProcess(Path.Combine(AppContext.BaseDirectory, "python-runtime"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var events = new ConcurrentQueue<PythonRunEvent>();
        return (await runtime.RunAsync(request, events.Enqueue, timeout.Token), events.ToArray());
    }

    [Fact]
    public async Task DisablingClicksBeforeRunReachesTheRuntimeInsteadOfBeingReenabled()
    {
        var store = new JsonProjectStore();
        var project = new ProjectService(store).Create(Path.Combine(root, "project"));
        new ProjectService(store).CreateScript(project, ProjectScriptKind.Object, "actor");
        await using var bridge = new PythonProcessBridge();
        await using var vm = new MainViewModel(new JsonSettingsStore(Path.Combine(root, "settings.json")),
            new InMemoryRecentProjectsStore(), new ProjectSession(), bridge);
        await vm.OpenProject(project.RootPath);
        var sprite = Assert.Single(vm.StageSprites);
        Assert.True(sprite.InspectorClickEnabled);
        sprite.InspectorClickEnabled = false;
        Assert.False(sprite.ClickEnabled);
        var run = vm.RunCommand.ExecuteAsync(null);
        await TestMainViewModelFactory.WaitForInteractiveAsync(vm, run);
        Assert.False(Assert.Single(vm.StageSprites).ClickEnabledRuntime);
        await vm.StopPythonAsync();
        await run;
    }

    [Fact]
    public async Task StoppedRunKeepsPreviewWhileSelectingAnObjectOpensItsScript()
    {
        var store = new JsonProjectStore();
        var project = new ProjectService(store).Create(Path.Combine(root, "project"));
        new ProjectService(store).CreateScript(project, ProjectScriptKind.Object, "alpha");
        new ProjectService(store).CreateScript(project, ProjectScriptKind.Object, "beta");
        await using var bridge = new PythonProcessBridge();
        await using var vm = new MainViewModel(new JsonSettingsStore(Path.Combine(root, "settings.json")),
            new InMemoryRecentProjectsStore(), new ProjectSession(), bridge);
        await vm.OpenProject(project.RootPath);
        var beta = vm.StageSprites.Single(s => s.DisplayName == "beta");
        var run = vm.RunCommand.ExecuteAsync(null);
        await TestMainViewModelFactory.WaitForInteractiveAsync(vm, run);
        await TestMainViewModelFactory.PumpUntilAsync(vm, () => vm.HasRuntimeStagePreview, run);
        await vm.StopPythonAsync();
        await run;
        Assert.True(vm.HasRuntimeStagePreview);
        Assert.False(vm.IsPythonActive);
        await vm.SelectStageObjectCommand.ExecuteAsync(beta);
        Assert.Equal(beta.ScriptId, vm.OpenScript?.Id);
        Assert.True(vm.HasRuntimeStagePreview);
    }

    [Fact]
    public async Task ExplicitZeroFalseAndConstructorOverrideLayoutAndInitializationOverridesAll()
    {
        var request = Request("""
from vac_runtime import ElementBase
class Cat(ElementBase):
    x = 0
    visible = False
    def __init__(self):
        super().__init__(size=100)
    def initialize(self):
        assert self.x == 0 and self.visible is False
        self.x = 20
    def _vac_on_start(self):
        assert (self.x, self.y, self.size, self.visible, self.layer) == (20, 50, 100, False, 8)
        self.x = 40
        self.direction = 90
""");
        var (exit, events) = await Run(request);
        Assert.Equal(0, exit);
        var frames = events.Where(e => e.Kind == "stage").Select(e => e.Stage!).ToArray();
        Assert.Equal(20, frames[0].Objects.Single().X);
        Assert.Equal(40, frames[^1].Objects.Single().X);
        Assert.False(frames[^1].Objects.Single().Visible);
        Assert.All(frames, f => Assert.Equal(request.SessionId, f.SessionId));
        Assert.True(frames.Zip(frames.Skip(1)).All(pair => pair.First.Sequence < pair.Second.Sequence));
        Assert.Equal(100, request.InitialStage!.Objects[0].X);
        var again = await Run(request with { SessionId = Guid.NewGuid().ToString("N") });
        Assert.Equal(20, again.Events.First(e => e.Kind == "stage").Stage!.Objects.Single().X);
    }

    [Fact]
    public async Task BroadcastAndBackgroundActivationPublishFinalStateEvenWhenHandlerFails()
    {
        var request = Request("""
from vac_runtime import ElementBase, broadcast
class Cat(ElementBase):
    def _vac_on_start(self):
        self.y = -20
        broadcast('go')
""", """
from vac_runtime import BackdropBase
class Night(BackdropBase):
    def receive(self):
        self.activate()
        self.visible = False
        raise ValueError('handler failed')
""") with { Handlers = [new("night", ProjectEventKinds.Broadcast, "go", "receive")] };
        request = request with {
            Sources = [.. request.Sources, new("scripts/backdrops/day.py", "from vac_runtime import BackdropBase\nclass Day(BackdropBase): pass")],
            Scripts = [.. request.Scripts, new("day", "day-id", "scripts/backdrops/day.py", "backdrop")],
            InitialStage = request.InitialStage! with { CurrentBackdropId = "day-id" }
        };
        var (exit, events) = await Run(request);
        Assert.Equal("day-id", events.First(e => e.Kind == "stage").Stage!.CurrentBackdropId);
        Assert.Equal(1, exit);
        Assert.Contains(events, e => e.Kind == "exception" && e.Exception!.Message == "handler failed");
        var last = events.Last(e => e.Kind == "stage").Stage!;
        Assert.Equal("night-id", last.CurrentBackdropId);
        Assert.Equal(-20, last.Objects.Single(o => o.ObjectId == "cat-id").Y);
        Assert.False(last.Objects.Single(o => o.ObjectId == "night-id").Visible);
    }

    [Fact]
    public async Task AnimationIsSampledWhileRunningAndCancellationRetainsLastFrameAndAllowsRestart()
    {
        Directory.CreateDirectory(root);
        await using var runtime = new PythonRuntimeProcess(Path.Combine(AppContext.BaseDirectory, "python-runtime"));
        var request = Request("""
from vac_runtime import ElementBase
import time
class Cat(ElementBase):
    def _vac_on_start(self):
        while True:
            self.x = (self.x + 1) % 200
            time.sleep(0.02)
""");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new ConcurrentQueue<PythonRunEvent>();
        var run = runtime.RunAsync(request, e => {
            events.Enqueue(e);
            if (e.Stage is { Sequence: >= 3 }) changed.TrySetResult();
        }, stop.Token);
        await changed.Task.WaitAsync(timeout.Token);
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.True(events.Count(e => e.Kind == "stage") >= 3);
        var next = Request("from vac_runtime import ElementBase\nclass Cat(ElementBase): pass");
        Assert.Equal(0, await runtime.RunAsync(next, events.Enqueue, timeout.Token));
        Assert.Equal(100, events.Last(e => e.Stage?.SessionId == next.SessionId).Stage!.Objects.Single().X);
    }

    [Fact]
    public async Task ClonePublishesAnIndependentRoleWithCopiedState()
    {
        var (exit, events) = await Run(Request("""
from vac_runtime import ElementBase
class Cat(ElementBase):
    def _vac_on_start(self):
        self.goto(10, 20)
        self.direction = 45
        copy = self.clone()
        copy.goto(100, 50)
        self.x = 30
"""));
        Assert.Equal(0, exit);
        var last = events.Last(e => e.Kind == "stage").Stage!;
        Assert.Equal(2, last.Objects.Length);
        var original = last.Objects.Single(o => o.ObjectId == "cat-id");
        var copy = last.Objects.Single(o => o.ObjectId == "clone:cat-id:1");
        Assert.Equal(30, original.X);
        Assert.Equal((100d, 50d, 45d), (copy.X, copy.Y, copy.Direction));
    }

    [Fact]
    public async Task TouchingEdgeUsesTheProvidedImageSize()
    {
        var request = Request("""
from vac_runtime import ElementBase
class Cat(ElementBase):
    def _vac_on_start(self):
        self.direction = 90
        self.goto(220, 0)
        print('near', self.touching_edge())
        self.goto(225, 0)
        print('edge', self.touching_edge())
""") with { StageImageSizes = new Dictionary<string, RuntimeImageSize> { ["cat-id"] = new(20, 20) } };
        var (exit, events) = await Run(request);
        Assert.Equal(0, exit);
        var output = string.Concat(events.Where(e => e.Kind == "stdout").Select(e => e.Text));
        Assert.Contains("near False", output);
        Assert.Contains("edge True", output);
    }

    [Fact]
    public async Task TouchingUsesTheActiveCostumeImageSize()
    {
        var request = new PythonRunRequest(
            Guid.NewGuid().ToString("N"), PythonEnvironment.DefaultExecutable, root, "scripts/main.py",
            [new("scripts/main.py", ManagedProjectEntry.Source),
             new("scripts/objects/cat.py", """
from vac_runtime import ElementBase
class Cat(ElementBase):
    def _vac_on_start(self):
        print('small', self.touching('dog'))
        self.switch_costume('大')
        print('large', self.touching('dog'))
"""),
             new("scripts/objects/dog.py", """
from vac_runtime import ElementBase
class Dog(ElementBase):
    pass
""")],
            [new("cat", "cat-id", "scripts/objects/cat.py", "object"),
             new("dog", "dog-id", "scripts/objects/dog.py", "object")],
            [], InitialStage: new StageState
            {
                Objects =
                [
                    new("cat-id", 0, 0, 100, 90, true, 0, "assets/images/small.png", false, true,
                        [new("small", "小", "assets/images/small.png", false),
                         new("large", "大", "assets/images/large.png", false)], "small"),
                    new("dog-id", 60, 0, 100, 90, true, 0)
                ]
            })
        {
            StageImageSizes = new Dictionary<string, RuntimeImageSize>
            {
                ["cat-id"] = new(10, 10), ["dog-id"] = new(48, 48)
            },
            StageCostumeImageSizes = new Dictionary<string, RuntimeImageSize>
            {
                ["small"] = new(10, 10), ["large"] = new(100, 100)
            }
        };
        var (exit, events) = await Run(request);
        Assert.Equal(0, exit);
        var output = string.Concat(events.Where(e => e.Kind == "stdout").Select(e => e.Text));
        Assert.Contains("small False", output);
        Assert.Contains("large True", output);
    }

    [Fact]
    public async Task CloneStartHandlerRunsOnlyForTheNewClone()
    {
        var request = Request("""
from vac_runtime import ElementBase
class Cat(ElementBase):
    def _vac_on_start(self):
        self.clone()
    def on_clone_start(self):
        self.x = 200
        print('clone-start', self.is_clone)
""") with { Handlers = [new("cat", ProjectEventKinds.CloneStart, null, "on_clone_start")] };
        var (exit, events) = await Run(request);
        Assert.Equal(0, exit);
        var last = events.Last(e => e.Kind == "stage").Stage!;
        Assert.Equal(100, last.Objects.Single(o => o.ObjectId == "cat-id").X);
        Assert.Equal(200, last.Objects.Single(o => o.ObjectId == "clone:cat-id:1").X);
        Assert.Contains("clone-start True", string.Concat(events.Where(e => e.Kind == "stdout").Select(e => e.Text)));
    }

    [Fact]
    public async Task ProjectVariablesAndListsAreSharedByRuntimeScripts()
    {
        var request = Request("""
from vac_runtime import ElementBase, add_to_variable, get_variable, list_append, list_length, list_values
class Cat(ElementBase):
    def _vac_on_start(self):
        add_to_variable('score', 2)
        list_append('items', 'gem')
        print('data', get_variable('score'), list_length('items'))
        print('items', ','.join(list_values('items')))
""") with { ProjectData = new RuntimeProjectData(
            [new RuntimeVariableDefinition("score", "number", "1")],
            [new RuntimeListDefinition("items", ["coin"])]) };
        var (exit, events) = await Run(request);
        Assert.Equal(0, exit);
        var output = string.Concat(events.Where(e => e.Kind == "stdout").Select(e => e.Text));
        Assert.Contains("data 3.0 2", output);
        Assert.Contains("items coin,gem", output);
    }

    [Fact]
    public async Task GlideBlocksItsOwnHandlerUntilTheTargetIsReached()
    {
        var request = Request("""
from vac_runtime import ElementBase
class Cat(ElementBase):
    def _vac_on_start(self):
        self.goto(0, 0)
        self.glide_to(100, 0, 0.1)
        print('after', self.x)
""") with { KeepAlive = true };
        Directory.CreateDirectory(root);
        await using var runtime = new PythonRuntimeProcess(Path.Combine(AppContext.BaseDirectory, "python-runtime"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var events = new ConcurrentQueue<PythonRunEvent>();
        var run = runtime.RunAsync(request, events.Enqueue, cancellation.Token);
        string Output() => string.Concat(events.Where(e => e.Kind == "stdout").Select(e => e.Text));
        while (!Output().Contains("after 100"))
            await Task.Delay(20, timeout.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task GlideBlocksItsHandlerWhileCloneStartIsQueued()
    {
        var request = Request("""
from vac_runtime import ElementBase, wait
class Cat(ElementBase):
    def _vac_on_start(self):
        self.goto(0, 0)
        copy = self.clone()
        copy.glide_by(0, 100, 0.05)
        print('copied', copy.is_clone)
    def on_clone_start(self):
        self.goto(0, 0)
        wait(0.1)
        self.delete_clone()
""") with { KeepAlive = true, Handlers = [new("cat", ProjectEventKinds.CloneStart, null, "on_clone_start")] };
        Directory.CreateDirectory(root);
        await using var runtime = new PythonRuntimeProcess(Path.Combine(AppContext.BaseDirectory, "python-runtime"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var events = new ConcurrentQueue<PythonRunEvent>();
        var run = runtime.RunAsync(request, events.Enqueue, cancellation.Token);
        string Output() => string.Concat(events.Where(e => e.Kind == "stdout").Select(e => e.Text));
        while (!Output().Contains("copied True"))
            await Task.Delay(20, timeout.Token);
        while (!events.Any(e => e.Stage is { } frame && frame.Objects.Length == 1
                               && frame.Objects[0].ObjectId == "cat-id"))
            await Task.Delay(20, timeout.Token);
        Assert.DoesNotContain(events, e => e.Kind == "exception");
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task CloneUsesItsOwnClickHandlerAndIsCloneFlag()
    {
        var request = Request("""
from vac_runtime import ElementBase
class Cat(ElementBase):
    def _vac_on_start(self):
        self.clone()
    def on_click(self):
        print('clicked', self.is_clone)
""") with { KeepAlive = true, Handlers = [new("cat", ProjectEventKinds.SpriteClick, null, "on_click")] };
        Directory.CreateDirectory(root);
        await using var runtime = new PythonRuntimeProcess(Path.Combine(AppContext.BaseDirectory, "python-runtime"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var events = new ConcurrentQueue<PythonRunEvent>();
        var run = runtime.RunAsync(request, events.Enqueue, cancellation.Token);
        while (!events.Any(e => e.Kind == "interactive-ready")) await Task.Delay(20, timeout.Token);
        var cloneId = events.SelectMany(e => e.Stage?.Objects ?? [])
            .Select(o => o.ObjectId).First(id => id.StartsWith("clone:cat-id:", StringComparison.Ordinal));
        await runtime.SendSpriteClickAsync(request.SessionId, cloneId, timeout.Token);
        await runtime.SendSpriteClickAsync(request.SessionId, "cat-id", timeout.Token);
        string Output() => string.Concat(events.Where(e => e.Kind == "stdout").Select(e => e.Text));
        while (!Output().Contains("clicked True") || !Output().Contains("clicked False"))
            await Task.Delay(20, timeout.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task CloneCanDeleteItselfFromItsClickHandler()
    {
        var request = Request("""
from vac_runtime import ElementBase
class Cat(ElementBase):
    def _vac_on_start(self):
        self.clone()
    def on_click(self):
        if self.is_clone:
            self.delete_clone()
            print('deleted')
""") with { KeepAlive = true, Handlers = [new("cat", ProjectEventKinds.SpriteClick, null, "on_click")] };
        Directory.CreateDirectory(root);
        await using var runtime = new PythonRuntimeProcess(Path.Combine(AppContext.BaseDirectory, "python-runtime"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var events = new ConcurrentQueue<PythonRunEvent>();
        var run = runtime.RunAsync(request, events.Enqueue, cancellation.Token);
        while (!events.Any(e => e.Kind == "interactive-ready")) await Task.Delay(20, timeout.Token);
        var cloneId = events.SelectMany(e => e.Stage?.Objects ?? [])
            .Select(o => o.ObjectId).First(id => id.StartsWith("clone:cat-id:", StringComparison.Ordinal));
        await runtime.SendSpriteClickAsync(request.SessionId, cloneId, timeout.Token);
        while (!events.Any(e => e.Kind == "stdout" && e.Text.Contains("deleted")))
            await Task.Delay(20, timeout.Token);
        while (!events.Any(e => e.Stage is { } frame && frame.Objects.Length == 1
                               && frame.Objects[0].ObjectId == "cat-id"))
            await Task.Delay(20, timeout.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public async Task RuntimeCloneIsRenderedSeparatelyAndRemovedWhenTheFrameDropsIt()
    {
        var service = new ProjectService(new JsonProjectStore());
        var project = service.Create(root);
        service.CreateScript(project, ProjectScriptKind.Object, "cat");
        await using var vm = TestMainViewModelFactory.Create(new InMemorySettingsStore());
        await vm.OpenProject(root);
        var baseItem = Assert.Single(vm.StageSprites);
        var cloneId = $"clone:{baseItem.ObjectId}:1";
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MainViewModel).GetField("lastRun", flags)!.SetValue(vm, Request("") with { SessionId = "clone-preview" });
        typeof(MainViewModel).GetField("runWorkspace", flags)!.SetValue(vm,
            typeof(MainViewModel).GetField("runtimeWorkspace", flags)!.GetValue(vm));
        vm.IsPythonActive = true;
        void Apply(RuntimeStageFrame frame) => typeof(MainViewModel).GetMethod("ApplyRuntimeStage", flags)!.Invoke(vm, [frame]);
        Apply(new("clone-preview", 1, [new(baseItem.ObjectId, 40), new(cloneId, 140)]));
        Assert.Single(vm.StageSprites);
        Assert.Equal(2, vm.StageRenderSprites.Count);
        var clone = Assert.Single(vm.StageRuntimeClones);
        Assert.True(clone.IsRuntimeClone);
        Assert.Equal(140, clone.RenderX);
        Assert.DoesNotContain(clone, vm.StageSprites);
        Apply(new("clone-preview", 2, [new(baseItem.ObjectId, 40)]));
        Assert.Empty(vm.StageRuntimeClones);
        Assert.Single(vm.StageRenderSprites);
    }

    [Fact]
    public async Task StageTracksNonDaemonWorkerUntilItFinishes()
    {
        var (exit, events) = await Run(Request("""
from vac_runtime import ElementBase
import threading
import time
class Cat(ElementBase):
    def _vac_on_start(self):
        def work():
            time.sleep(0.1)
            self.x = 321
        threading.Thread(target=work).start()
"""));
        Assert.Equal(0, exit);
        Assert.Equal(321, events.Last(e => e.Kind == "stage").Stage!.Objects.Single().X);
    }

    [Theory]
    [InlineData("self.x = float('nan')")]
    [InlineData("self.layer = 0.5")]
    [InlineData("self.visible = 0")]
    public async Task InvalidValuesFailWithoutPublishingAnInvalidState(string statement)
    {
        var (exit, events) = await Run(Request("from vac_runtime import ElementBase\nclass Cat(ElementBase):\n    def _vac_on_start(self):\n        " + statement));
        Assert.Equal(1, exit);
        Assert.Contains(events, e => e.Exception?.Type == "ValueError");
        Assert.All(events.Where(e => e.Stage is not null), e => StageRules.Validate(new() { Objects = e.Stage!.Objects.ToList() }));
    }

    [Fact]
    public async Task PreviewRejectsStaleFramesAndSavePersistsOnlyInitialLayout()
    {
        var service = new ProjectService(new JsonProjectStore());
        var project = service.Create(root);
        var created = service.CreateScript(project, ProjectScriptKind.Object, "cat");
        await using var vm = TestMainViewModelFactory.Create(new InMemorySettingsStore());
        await vm.OpenProject(root);
        var item = Assert.Single(vm.StageSprites);
        item.X = 70;
        await vm.SaveCommand.ExecuteAsync(null);
        var path = Path.Combine(root, "scenes", "stage.json");
        var original = File.ReadAllText(path);
        var request = Request("") with { SessionId = "current" };
        vm.IsPythonActive = true;
        typeof(MainViewModel).GetField("lastRun", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, request);
        typeof(MainViewModel).GetField("runWorkspace", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm,
            typeof(MainViewModel).GetField("runtimeWorkspace", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(vm));
        void Apply(RuntimeStageFrame frame) => typeof(MainViewModel).GetMethod("ApplyRuntimeStage", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(vm, [frame]);
        Apply(new("old", 50, [new(item.ObjectId, 999)]));
        Assert.Equal(70, item.RenderX);
        Apply(new("current", 2, [new(item.ObjectId, 150, 30, 200, -90, false, 10)]));
        Assert.Equal(150, item.RenderX);
        Assert.Equal((150d, 30d, 200d, -90d, 10, false),
            (item.InspectorX, item.InspectorY, item.InspectorSize, item.InspectorDirection, item.InspectorLayer, item.InspectorVisible));
        Assert.False(item.CanEditProperties);
        item.InspectorX = 999;
        Assert.Equal(70, item.X);
        Assert.Equal(70, item.Snapshot().X);
        Assert.Equal(10, item.RenderLayer);
        Assert.Equal(0, item.StageOpacity);
        Apply(new("current", 1, [new(item.ObjectId, 999)]));
        Apply(new("current", 3, [new("unknown", 999)]));
        Assert.Equal(150, item.RenderX);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal(original, File.ReadAllText(path));
        vm.IsPythonActive = false;
        Assert.True(item.CanEditProperties);
        Assert.True(vm.ReturnToInitialStageCommand.CanExecute(null));
        vm.ReturnToInitialStageCommand.Execute(null);
        Assert.False(vm.HasRuntimeStagePreview);
        Assert.True(item.CanEditProperties);
        Assert.False(vm.ReturnToInitialStageCommand.CanExecute(null));
        Assert.Equal((70d, 0d, 100d, 90d, 0, true),
            (item.InspectorX, item.InspectorY, item.InspectorSize, item.InspectorDirection, item.InspectorLayer, item.InspectorVisible));
        Assert.Equal(original, File.ReadAllText(path));
        item.InspectorY = 40;
        Assert.Equal(70, item.RenderX);
        Assert.Equal(40, item.RenderY);
        await vm.CloseProject();
        await vm.OpenProject(root);
        Assert.Equal(70, Assert.Single(vm.StageSprites).RenderX);
    }
    [Fact]
    public async Task InspectorCanBrowseObjectsAndBackgroundsWithoutChangingPreviewOrInitialBackdrop()
    {
        var service = new ProjectService(new JsonProjectStore());
        var project = service.Create(root);
        project = service.CreateScript(project, ProjectScriptKind.Object, "cat").Project;
        project = service.CreateScript(project, ProjectScriptKind.Backdrop, "day").Project;
        project = service.CreateScript(project, ProjectScriptKind.Backdrop, "night").Project;
        await using var vm = TestMainViewModelFactory.Create(new InMemorySettingsStore());
        await vm.OpenProject(root);
        vm.StageSprites[0].X = 27; // Unsaved initial layout must survive returning from preview.
        var day = vm.StageBackdrops[0];
        var night = vm.StageBackdrops[1];
        var initial = vm.CurrentBackdrop!.ObjectId;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MainViewModel).GetField("lastRun", flags)!.SetValue(vm, Request("") with { SessionId = "inspect" });
        typeof(MainViewModel).GetField("runWorkspace", flags)!.SetValue(vm,
            typeof(MainViewModel).GetField("runtimeWorkspace", flags)!.GetValue(vm));
        vm.IsPythonActive = true;
        Assert.All(vm.StageSprites.Concat(vm.StageBackdrops), i => Assert.False(i.CanEditProperties));
        var states = vm.StageSprites.Concat(vm.StageBackdrops).Select(i => new StageObjectState(i.ObjectId, 80, -30, Visible: false)).ToArray();
        typeof(MainViewModel).GetMethod("ApplyRuntimeStage", flags)!.Invoke(vm, [new RuntimeStageFrame("inspect", 1, states, night.ObjectId)]);
        Assert.False(vm.ReturnToInitialStageCommand.CanExecute(null));
        await vm.SelectStageObjectCommand.ExecuteAsync(day);
        Assert.Equal(80, vm.SelectedBackdrop!.InspectorX);
        Assert.False(vm.SelectedBackdrop.InspectorVisible);
        Assert.Equal(night, vm.DisplayBackdrop);
        Assert.Equal(initial, vm.CurrentBackdrop!.ObjectId);
        Assert.Null(vm.OpenScript);
        vm.IsPythonActive = false;
        Assert.True(day.CanEditProperties);
        await vm.SelectStageObjectCommand.ExecuteAsync(night);
        await vm.SelectStageObjectCommand.ExecuteAsync(vm.StageSprites[0]);
        Assert.Equal(80, vm.SelectedSprite!.InspectorX);
        Assert.Equal(night, vm.DisplayBackdrop);
        Assert.Equal(initial, vm.CurrentBackdrop!.ObjectId);
        vm.ReturnToInitialStageCommand.Execute(null);
        Assert.Equal(initial, vm.DisplayBackdrop!.ObjectId);
        Assert.All(vm.StageSprites.Concat(vm.StageBackdrops), i => {
            Assert.True(i.CanEditProperties); Assert.Equal(i.IsSprite ? 27 : 0, i.InspectorX); Assert.True(i.InspectorVisible);
        });
        Assert.True(vm.CanEditStage);
    }

    [Theory]
    [InlineData(-30)]
    [InlineData(70)]
    public async Task StoppedInspectorEditLeavesPreviewAndSavesOnlyTheExplicitProperty(double requestedX)
    {
        var service = new ProjectService(new JsonProjectStore());
        service.CreateScript(service.Create(root), ProjectScriptKind.Object, "cat");
        await using var vm = TestMainViewModelFactory.Create(new InMemorySettingsStore());
        await vm.OpenProject(root);
        var item = Assert.Single(vm.StageSprites);
        item.X = 70; item.Y = 10; item.Size = 100; item.Direction = 90; item.Layer = 2;
        await vm.SaveCommand.ExecuteAsync(null);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MainViewModel).GetField("lastRun", flags)!.SetValue(vm, Request("") with { SessionId = "edit" });
        typeof(MainViewModel).GetField("runWorkspace", flags)!.SetValue(vm,
            typeof(MainViewModel).GetField("runtimeWorkspace", flags)!.GetValue(vm));
        vm.IsPythonActive = true;
        typeof(MainViewModel).GetMethod("ApplyRuntimeStage", flags)!.Invoke(vm,
            [new RuntimeStageFrame("edit", 1, [new(item.ObjectId, 150, 80, 200, -45, false, 8)])]);
        item.InspectorX = requestedX;
        Assert.Equal(70, item.X);
        vm.IsPythonActive = false;
        Assert.True(item.CanEditProperties);
        Assert.True(vm.HasRuntimeStagePreview);
        item.InspectorX = item.RenderX; // Binding echo must not promote the preview into layout.
        Assert.True(vm.HasRuntimeStagePreview);
        item.InspectorX = requestedX;
        Assert.False(vm.HasRuntimeStagePreview);
        Assert.Equal((requestedX, 10d, 100d, 90d, 2, true),
            (item.X, item.Y, item.Size, item.Direction, item.Layer, item.IsVisible));
        Assert.Equal(requestedX, item.RenderX);
        await vm.SaveCommand.ExecuteAsync(null);
        await vm.CloseProject(); await vm.OpenProject(root);
        var restored = Assert.Single(vm.StageSprites);
        Assert.Equal((requestedX, 10d, 100d, 90d, 2, true),
            (restored.X, restored.Y, restored.Size, restored.Direction, restored.Layer, restored.IsVisible));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
