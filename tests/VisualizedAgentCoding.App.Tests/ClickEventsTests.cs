using System.Collections.Concurrent;
using AvaloniaEdit.Document;
using happy_agent_coding.Services;
using VisualizedAgentCoding.Application.Editor;
using VisualizedAgentCoding.Application.Editor.ObjectScripts;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Application.Runtime;
using VisualizedAgentCoding.Domain.Projects;
using VisualizedAgentCoding.Infrastructure.Projects;
using VisualizedAgentCoding.Infrastructure.Python;

namespace VisualizedAgentCoding.App.Tests;

public sealed class ClickEventsTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "vac-click-" + Guid.NewGuid().ToString("N"));
    private readonly JsonProjectStore store = new();
    private readonly PythonProcessBridge bridge = new();
    private ProjectWorkspace project = null!;
    private static string RuntimePath => Path.Combine(AppContext.BaseDirectory, "python-runtime");
    public Task InitializeAsync() { project = new ProjectService(store).Create(root); return Task.CompletedTask; }
    public async Task DisposeAsync() { await bridge.DisposeAsync(); if (Directory.Exists(root)) Directory.Delete(root, true); }
    private async Task<ObjectScriptDocument> Actor(string name)
    {
        var created = new ProjectService(store).CreateScript(project, ProjectScriptKind.Object, name);
        project = created.Project;
        var doc = await Inspect(created.Script.Id, store.ReadScript(root, created.Script.Path));
        var imported = await new ScriptImportService(bridge).InspectAsync(project, created.Script, 0, doc.FullSource, doc,
            [new("vac_runtime", Name: "broadcast", Source: "runtime")]);
        return await Inspect(doc.ScriptId, imported.Source);
    }
    private async Task<ObjectScriptDocument> Backdrop(string name)
    {
        var created = new ProjectService(store).CreateScript(project, ProjectScriptKind.Backdrop, name);
        project = created.Project;
        return await Inspect(created.Script.Id, store.ReadScript(root, created.Script.Path));
    }
    private Task<ObjectScriptDocument> Inspect(string id, string source) => new ObjectScriptDocumentService(bridge).InspectAsync(project, id, 0, source);
    private static ObjectScriptDocument Body(ObjectScriptDocument doc, string id, string text)
    {
        var projection = new MultiRegionProjection(doc);
        projection.UpdateRegions(new Dictionary<string, string> { [id] = text });
        return projection.Document;
    }
    private void Save(ObjectScriptDocument doc) => store.WriteScript(root, project.FindScript(doc.ScriptId)!.Path, doc.FullSource);
    private async Task<PythonRunRequest> Request() => (await new ProjectRunService(store, bridge).PrepareAsync(project, PythonEnvironment.DefaultExecutable, default))
        with { KeepAlive = true, InitialStage = new() };
    private static async Task WaitUntil(Func<bool> condition, CancellationToken token)
    { while (!condition()) await Task.Delay(20, token); }
    private static string Output(ConcurrentQueue<PythonRunEvent> events) => string.Concat(events.Where(e => e.Kind == "stdout").Select(e => e.Text)).Replace("\r", "");
    private static async Task Stop(Task<int> run, CancellationTokenSource cancellation)
    { cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run); }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task RegionRoundTripsAndUndoRestoresHandlerWithoutTouchingOtherCode(string nl)
    {
        var doc = await Actor("cat");
        doc = await Inspect(doc.ScriptId, doc.FullSource.Replace("\r\n", "\n").Replace("\n", nl));
        var added = ObjectScriptFeatures.AddSpriteClick(doc);
        var id = added.Regions!.Last().Id;
        added = Body(added, id, "self.x += 10" + nl);
        Assert.Throws<ArgumentException>(() => ObjectScriptFeatures.AddSpriteClick(added));
        Assert.Contains(MessageEvents.Title(ProjectEventKinds.SpriteClick), new MultiRegionProjection(added).ViewSource);
        Assert.Equal(ObjectScriptStatus.Recognized, (await Inspect(doc.ScriptId, added.FullSource)).Status);
        Save(added);
        Assert.Single((await Request()).Handlers, h => h.Event == ProjectEventKinds.SpriteClick);
        var deleted = ObjectScriptFeatures.DeleteSpriteClick(added, id);
        Assert.Contains("broadcast", deleted.FullSource);
        Assert.DoesNotContain("self.x += 10", deleted.FullSource);
        Assert.Equal(ObjectScriptStatus.Recognized, (await Inspect(doc.ScriptId, deleted.FullSource)).Status);
        var text = new TextDocument(new MultiRegionProjection(added).ViewSource);
        StructuralEditorState? restored = null;
        StructuralEditorHistory.Apply(text, new(added, 0, 0, 0), new(deleted, 0, 0, 0), () => { }, value => restored = value);
        text.UndoStack.Undo(); Assert.Equal(added.FullSource, restored!.Document.FullSource);
        text.UndoStack.Redo(); Assert.Equal(deleted.FullSource, restored!.Document.FullSource);
        Save(deleted);
        Assert.DoesNotContain((await Request()).Handlers, h => h.Event == ProjectEventKinds.SpriteClick);
    }

    [Fact]
    public async Task PersistentClicksUseMainThreadAndSynchronousBroadcastAndIgnoreDisabledTargets()
    {
        var doc = ObjectScriptFeatures.AddInitialize(await Actor("cat"));
        doc = Body(doc, "initialize", "self.owner = __import__('threading').get_ident()\n");
        doc = ObjectScriptFeatures.AddMessage(doc, "echo");
        doc = Body(doc, doc.Regions!.Last().Id, "print('message', self.x)\n");
        doc = ObjectScriptFeatures.AddSpriteClick(doc);
        Save(Body(doc, doc.Regions!.Last().Id, "assert self.owner == __import__('threading').get_ident()\nself.x += 10\nbroadcast('echo')\nprint('click', self.x)\nif self.x >= 20:\n    self.click_enabled = False\n"));
        var barrier = ObjectScriptFeatures.AddSpriteClick(await Actor("barrier"));
        Save(Body(barrier, barrier.Regions!.Last().Id, "print('barrier')\n"));
        var request = await Request();
        var catId = project.FindScript(doc.ScriptId)!.ObjectId!;
        var barrierId = project.FindScript(barrier.ScriptId)!.ObjectId!;
        await using var runtime = new PythonRuntimeProcess(RuntimePath);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var events = new ConcurrentQueue<PythonRunEvent>();
        var run = runtime.RunAsync(request, events.Enqueue, cancellation.Token);
        await WaitUntil(() => events.Any(e => e.Kind == "interactive-ready"), timeout.Token);
        Assert.False(run.IsCompleted);
        await runtime.SendSpriteClickAsync(request.SessionId, catId, timeout.Token);
        await WaitUntil(() => events.Any(e => e.Stage?.Objects.Any(o => o.ObjectId == catId && o.X == 10) == true), timeout.Token);
        await runtime.SendSpriteClickAsync(request.SessionId, catId, timeout.Token);
        await WaitUntil(() => events.Any(e => e.Stage?.Objects.Any(o => o.ObjectId == catId && !o.ClickEnabled) == true), timeout.Token);
        await runtime.SendSpriteClickAsync(request.SessionId, catId, timeout.Token);
        await runtime.SendSpriteClickAsync(request.SessionId, barrierId, timeout.Token);
        await WaitUntil(() => Output(events).Contains("barrier"), timeout.Token);
        Assert.Contains("message 10\nclick 10", Output(events));
        Assert.DoesNotContain("click 30", Output(events));
        await Assert.ThrowsAsync<IOException>(() => runtime.SendSpriteClickAsync("stale", catId));
        await Assert.ThrowsAsync<IOException>(() => runtime.SendSpriteClickAsync(request.SessionId, "unknown"));
        await Stop(run, cancellation);
        await Assert.ThrowsAsync<IOException>(() => runtime.SendSpriteClickAsync(request.SessionId, catId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopInterruptsInputHandlerDiscardsPendingClicksAndRestartUsesNewSession(bool busyLoop)
    {
        var doc = ObjectScriptFeatures.AddSpriteClick(await Actor("cat"));
        Save(Body(doc, doc.Regions!.Last().Id, "print('entered')\ninput()\nself.x += 1\nprint('done')\n"));
        var request = await Request(); var objectId = project.FindScript(doc.ScriptId)!.ObjectId!;
        await using var runtime = new PythonRuntimeProcess(RuntimePath);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var events = new ConcurrentQueue<PythonRunEvent>();
        var first = busyLoop ? request with { Sources = request.Sources.Select(source => source with {
            Source = source.Source.Replace("        input()", "        while True:\n            pass") }).ToArray() } : request;
        var run = runtime.RunAsync(first, events.Enqueue, stop.Token);
        await WaitUntil(() => events.Any(e => e.Kind == "interactive-ready"), timeout.Token);
        await runtime.SendSpriteClickAsync(request.SessionId, objectId);
        await WaitUntil(() => Output(events).Contains("entered"), timeout.Token);
        await runtime.SendSpriteClickAsync(request.SessionId, objectId);
        await runtime.SendSpriteClickAsync(request.SessionId, objectId);
        await Stop(run, stop);
        Assert.DoesNotContain("done", Output(events));
        var next = request with { SessionId = Guid.NewGuid().ToString("N") };
        events.Clear();
        using var nextStop = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var nextRun = runtime.RunAsync(next, events.Enqueue, nextStop.Token);
        await WaitUntil(() => events.Any(e => e.Kind == "interactive-ready"), timeout.Token);
        await Assert.ThrowsAsync<IOException>(() => runtime.SendSpriteClickAsync(request.SessionId, objectId));
        Assert.DoesNotContain("entered", Output(events));
        await runtime.SendSpriteClickAsync(next.SessionId, objectId);
        await WaitUntil(() => Output(events).Contains("entered"), timeout.Token);
        await runtime.SendInputAsync("continue");
        await WaitUntil(() => events.Any(e => e.Stage?.Objects.Single().X == 1), timeout.Token);
        Assert.Contains("done", Output(events));
        await Stop(nextRun, nextStop);
    }

    [Fact]
    public async Task ClickFailureTerminatesSessionAndMapsToClickUserRegion()
    {
        var doc = ObjectScriptFeatures.AddSpriteClick(await Actor("cat"));
        Save(Body(doc, doc.Regions!.Last().Id, "self.x = 80\nraise ValueError('click failed')\n"));
        var request = await Request();
        await using var runtime = new PythonRuntimeProcess(RuntimePath);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var events = new ConcurrentQueue<PythonRunEvent>();
        var run = runtime.RunAsync(request, events.Enqueue, timeout.Token);
        await WaitUntil(() => events.Any(e => e.Kind == "interactive-ready"), timeout.Token);
        await runtime.SendSpriteClickAsync(request.SessionId, project.FindScript(doc.ScriptId)!.ObjectId!);
        Assert.Equal(1, await run);
        var error = Assert.Single(events, e => e.Kind == "exception");
        var report = new RuntimeErrorPresentationService().Present(error, request.PresentationSources!);
        Assert.Contains(MessageEvents.Title(ProjectEventKinds.SpriteClick), report.UserText);
        Assert.Equal(80, events.Last(e => e.Kind == "stage").Stage!.Objects.Single().X);
    }

    [Fact]
    public async Task BackdropClickReachesOnlyTheActiveBackdropAndHonorsClickEnabled()
    {
        var day = new ProjectService(store).CreateScript(project, ProjectScriptKind.Backdrop, "day");
        project = day.Project;
        var dayDoc = ObjectScriptFeatures.AddStageClick(await Inspect(day.Script.Id, store.ReadScript(root, day.Script.Path)));
        Save(Body(dayDoc, dayDoc.Regions!.Last().Id, "self.x += 1\nprint('day', self.x)\n"));
        var night = new ProjectService(store).CreateScript(project, ProjectScriptKind.Backdrop, "night");
        project = night.Project;
        var nightDoc = ObjectScriptFeatures.AddStageClick(await Inspect(night.Script.Id, store.ReadScript(root, night.Script.Path)));
        Save(Body(nightDoc, nightDoc.Regions!.Last().Id, "print('night')\n"));
        var dayId = project.FindScript(dayDoc.ScriptId)!.ObjectId!;
        var nightId = project.FindScript(nightDoc.ScriptId)!.ObjectId!;
        var initial = new StageState { CurrentBackdropId = dayId, Objects = [new(dayId), new(nightId)] };
        var request = (await Request()) with { InitialStage = initial };
        Assert.Contains(request.Handlers, h => h.Event == ProjectEventKinds.StageClick);
        await using var runtime = new PythonRuntimeProcess(RuntimePath);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var events = new ConcurrentQueue<PythonRunEvent>();
        var run = runtime.RunAsync(request, events.Enqueue, timeout.Token);
        await WaitUntil(() => events.Any(e => e.Kind == "interactive-ready"), timeout.Token);
        await runtime.SendStageClickAsync(request.SessionId, timeout.Token);
        await WaitUntil(() => Output(events).Contains("day"), timeout.Token);
        Assert.DoesNotContain("night", Output(events));
        await Stop(run, timeout);
    }

    [Fact]
    public async Task KeyEventsDispatchToEveryBoundObjectOnTheMainThread()
    {
        var cat = ObjectScriptFeatures.AddKey(await Actor("cat"), "a");
        Save(Body(cat, cat.Regions!.Last().Id, "print('cat-key')\n"));
        var dog = ObjectScriptFeatures.AddKey(await Actor("dog"), "a");
        Save(Body(dog, dog.Regions!.Last().Id, "print('dog-key')\n"));
        var bird = ObjectScriptFeatures.AddKey(await Actor("bird"), "any");
        Save(Body(bird, bird.Regions!.Last().Id, "print('any-key')\n"));
        var request = await Request();
        Assert.Equal(3, request.Handlers.Count(h => h.Event == ProjectEventKinds.Key));
        await using var runtime = new PythonRuntimeProcess(RuntimePath);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var events = new ConcurrentQueue<PythonRunEvent>();
        var run = runtime.RunAsync(request, events.Enqueue, timeout.Token);
        await WaitUntil(() => events.Any(e => e.Kind == "interactive-ready"), timeout.Token);
        await runtime.SendKeyAsync(request.SessionId, "a", timeout.Token);
        await WaitUntil(() => Output(events).Contains("any-key"), timeout.Token);
        var text = Output(events);
        Assert.Contains("cat-key", text);
        Assert.Contains("dog-key", text);
        await Stop(run, timeout);
    }

    [Fact]
    public async Task BackdropSwitchEventRunsForEveryBoundObject()
    {
        var day = await Backdrop("day");
        var night = await Backdrop("night");
        var nightId = project.FindScript(night.ScriptId)!.ObjectId!;
        Save(Body(night, "start", "self.activate()\n"));
        day = ObjectScriptFeatures.AddBackdropSwitch(day, nightId);
        Save(Body(day, day.Regions!.Last().Id, "print('day-switch')\n"));
        var cat = ObjectScriptFeatures.AddBackdropSwitch(await Actor("cat"), nightId);
        Assert.Contains("当背景切换到 night", new MultiRegionProjection(cat).ViewSource);
        Save(Body(cat, cat.Regions!.Last().Id, "print('cat-switch')\n"));
        var request = (await Request()) with
        {
            InitialStage = new StageState
            {
                CurrentBackdropId = project.FindScript(day.ScriptId)!.ObjectId!,
                Objects = [new(project.FindScript(day.ScriptId)!.ObjectId!), new(nightId)],
            },
        };
        Assert.Contains(request.Handlers, h => h.Event == ProjectEventKinds.BackdropSwitch && h.Argument == nightId);
        await using var runtime = new PythonRuntimeProcess(RuntimePath);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var events = new ConcurrentQueue<PythonRunEvent>();
        var run = runtime.RunAsync(request, events.Enqueue, timeout.Token);
        await WaitUntil(() => Output(events).Contains("day-switch") && Output(events).Contains("cat-switch"), timeout.Token);
        Assert.Contains("day-switch", Output(events));
        Assert.Contains("cat-switch", Output(events));
        await Stop(run, timeout);
    }

    [Fact]
    public async Task SwitchBackdropApiWorksFromAnyObject()
    {
        var day = await Backdrop("day");
        var night = await Backdrop("night");
        var nightId = project.FindScript(night.ScriptId)!.ObjectId!;
        var cat = ObjectScriptFeatures.AddBackdropSwitch(await Actor("cat"), nightId);
        Save(Body(cat, cat.Regions!.Last().Id, "print('cat-switch')\n"));
        var driver = await Actor("driver");
        var imported = await new ScriptImportService(bridge).InspectAsync(project,
            project.FindScript(driver.ScriptId)!, 0, driver.FullSource, driver,
            [new("vac_runtime", Name: "broadcast", Source: "runtime"),
             new("vac_runtime", Name: "switch_backdrop", Source: "runtime")]);
        driver = await Inspect(driver.ScriptId, imported.Source);
        Save(Body(driver, "start", "switch_backdrop('night')\n"));
        var request = (await Request()) with
        {
            InitialStage = new StageState
            {
                CurrentBackdropId = project.FindScript(day.ScriptId)!.ObjectId!,
                Objects = [new(project.FindScript(day.ScriptId)!.ObjectId!), new(nightId)],
            },
        };
        await using var runtime = new PythonRuntimeProcess(RuntimePath);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var events = new ConcurrentQueue<PythonRunEvent>();
        var run = runtime.RunAsync(request, events.Enqueue, timeout.Token);
        await WaitUntil(() => Output(events).Contains("cat-switch"), timeout.Token);
        Assert.Contains("cat-switch", Output(events));
        await Stop(run, timeout);
    }

    [Fact]
    public async Task PointerCommandsDriveDirectionAndMoveToPointer()
    {
        var cat = ObjectScriptFeatures.AddKey(await Actor("cat"), "a");
        cat = Body(cat, "start", "self.goto(0, 0)\nself.direction = 0\n");
        Save(Body(cat, cat.Regions!.Last().Id,
            "self.point_towards_pointer()\nprint('direction', self.direction)\nself.move_to_pointer()\nprint('moved', self.x, self.y)\n"));
        var request = await Request();
        await using var runtime = new PythonRuntimeProcess(RuntimePath);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var events = new ConcurrentQueue<PythonRunEvent>();
        var run = runtime.RunAsync(request, events.Enqueue, timeout.Token);
        await WaitUntil(() => events.Any(e => e.Kind == "interactive-ready"), timeout.Token);
        await runtime.SendPointerAsync(request.SessionId, 100, 0, timeout.Token);
        await runtime.SendKeyAsync(request.SessionId, "a", timeout.Token);
        await WaitUntil(() => Output(events).Contains("moved"), timeout.Token);
        Assert.Contains("direction 90", Output(events));
        Assert.Contains("moved 100", Output(events));
        await Stop(run, timeout);
    }

    [Fact]
    public void TimerIdentitiesValidateAndDisplay()
    {
        Assert.Equal("timer:1.5", TimerEvents.FormatIdentity(new(1.5, false)));
        Assert.Equal("timer:every:2", TimerEvents.FormatIdentity(new(2, true)));
        Assert.Equal(new TimerSpec(1.5, false), TimerEvents.ParseIdentity("timer:1.5")!.Value);
        Assert.Equal(new TimerSpec(2, true), TimerEvents.ParseIdentity("timer:every:2")!.Value);
        Assert.Equal("1.5", TimerEvents.ValidateArgument(" 1.5 ", false));
        Assert.Equal("every:2", TimerEvents.ValidateArgument("2", true));
        Assert.Throws<ArgumentException>(() => TimerEvents.ValidateArgument("0", false));
        Assert.Throws<ArgumentException>(() => TimerEvents.ValidateArgument("every:2", false));
        Assert.Throws<ArgumentException>(() => TimerEvents.ValidateArgument("", false));
        Assert.Null(TimerEvents.ParseIdentity("key:a"));
    }

    [Fact]
    public async Task TimerEventRunsOnTheMainThreadAndExposesTheRunClock()
    {
        var doc = await Actor("cat");
        var imported = await new ScriptImportService(bridge).InspectAsync(project,
            project.FindScript(doc.ScriptId)!, 0, doc.FullSource, doc,
            [new("vac_runtime", Name: "broadcast", Source: "runtime"),
             new("vac_runtime", Name: "timer", Source: "runtime")]);
        doc = await Inspect(doc.ScriptId, imported.Source);
        doc = ObjectScriptFeatures.AddTimer(doc, "0.2", false);
        Save(Body(doc, doc.Regions!.Last().Id,
            "print('timer', round(timer(), 2), round(self.timer, 2))\nself.x = 10\n"));
        var request = await Request();
        Assert.Contains(request.Handlers, h => h.Event == ProjectEventKinds.Timer);
        await using var runtime = new PythonRuntimeProcess(RuntimePath);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var events = new ConcurrentQueue<PythonRunEvent>();
        var run = runtime.RunAsync(request, events.Enqueue, timeout.Token);
        await WaitUntil(() => Output(events).Contains("timer"), timeout.Token);
        Assert.Contains("timer", Output(events));
        await Stop(run, timeout);
    }

    [Fact]
    public void KeyNamesRejectUnknownAndUppercaseForms()
    {
        Assert.Equal("a", KeyEvents.ValidateName("a"));
        Assert.Equal("space", KeyEvents.ValidateName("space"));
        Assert.Throws<ArgumentException>(() => KeyEvents.ValidateName("A"));
        Assert.Throws<ArgumentException>(() => KeyEvents.ValidateName("ctrl+a"));
        Assert.Throws<ArgumentException>(() => KeyEvents.ValidateName(""));
        Assert.Null(KeyEvents.Name("message:x"));
        Assert.Equal("up", KeyEvents.Name("key:up"));
    }
}
