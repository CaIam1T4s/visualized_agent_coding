using System.Collections.Concurrent;
using AvaloniaEdit.Document;
using happy_agent_coding.Services;
using happy_agent_coding.ViewModels;
using VisualizedAgentCoding.Application.Editor;
using VisualizedAgentCoding.Application.Editor.ObjectScripts;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Application.Runtime;
using VisualizedAgentCoding.Domain.Projects;
using VisualizedAgentCoding.Infrastructure.Projects;
using VisualizedAgentCoding.Infrastructure.Python;

namespace VisualizedAgentCoding.App.Tests;

public sealed class MessageEventsTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "vac-messages-" + Guid.NewGuid().ToString("N"));
    private readonly JsonProjectStore store = new();
    private readonly PythonProcessBridge bridge = new();
    private ProjectWorkspace project = null!;
    public Task InitializeAsync() { project = new ProjectService(store).Create(root); return Task.CompletedTask; }
    public async Task DisposeAsync() { await bridge.DisposeAsync(); if (Directory.Exists(root)) Directory.Delete(root, true); }

    private async Task<ObjectScriptDocument> Actor(string name, ProjectScriptKind kind = ProjectScriptKind.Object)
    {
        var created = new ProjectService(store).CreateScript(project, kind, name);
        project = created.Project;
        var source = store.ReadScript(root, created.Script.Path);
        var doc = await new ObjectScriptDocumentService(bridge).InspectAsync(project, created.Script.Id, 0, source);
        var imports = await new ScriptImportService(bridge).InspectAsync(project, created.Script, 0, source, doc,
            [new("vac_runtime", Name: "broadcast", Source: "runtime")]);
        return await Inspect(doc, imports.Source);
    }
    private Task<ObjectScriptDocument> Inspect(ObjectScriptDocument doc, string source) =>
        new ObjectScriptDocumentService(bridge).InspectAsync(project, doc.ScriptId, doc.Version + 1, source);
    private static ObjectScriptDocument Body(ObjectScriptDocument doc, string id, string code)
    {
        var projection = new MultiRegionProjection(doc);
        projection.UpdateRegions(new Dictionary<string, string> { [id] = code });
        return projection.Document;
    }
    private void Save(ObjectScriptDocument doc) => store.WriteScript(root, project.FindScript(doc.ScriptId)!.Path, doc.FullSource);
    private async Task<(int Exit, PythonRunEvent[] Events, PythonRunRequest Request)> Run()
    {
        var request = await new ProjectRunService(store, bridge).PrepareAsync(project, PythonEnvironment.DefaultExecutable, default);
        var events = new ConcurrentQueue<PythonRunEvent>();
        await using var runtime = new PythonRuntimeProcess(Path.Combine(AppContext.BaseDirectory, "python-runtime"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var exit = await runtime.RunAsync(request, events.Enqueue, timeout.Token);
        return (exit, events.ToArray(), request);
    }
    private static string Output(PythonRunEvent[] events) => string.Concat(events.Where(e => e.Kind == "stdout").Select(e => e.Text)).Replace("\r", "");

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task RegionRenameDeleteUndoAndReloadPreserveOtherCodeAndIdentity(string nl)
    {
        var baseline = await Actor("cat");
        baseline = await Inspect(baseline, baseline.FullSource.Replace("\r\n", "\n").Replace("\n", nl));
        baseline = Body(baseline, "members", "# 保留用户原文 😀" + nl);
        var added = ObjectScriptFeatures.AddMessage(baseline, "开始\"游戏😀");
        var id = added.Regions!.Last().Id;
        added = Body(added, id, "print('你好😀')" + nl);
        var initialized = ObjectScriptFeatures.AddInitialize(added);
        var renamed = ObjectScriptFeatures.RenameMessage(initialized, id, "下一关😀");
        Assert.Equal(id, renamed.Regions!.Last(r => r.Kind == "event").Id);
        Assert.Equal(ObjectScriptStatus.Recognized, (await Inspect(renamed, renamed.FullSource)).Status);
        Assert.Throws<ArgumentException>(() => ObjectScriptFeatures.AddMessage(renamed, "下一关😀"));
        var deleted = ObjectScriptFeatures.DeleteMessage(renamed, id);
        Assert.Contains("# 保留用户原文 😀" + nl, deleted.FullSource);
        Assert.Contains("def initialize(self):", deleted.FullSource);
        Assert.DoesNotContain("print('你好😀')", deleted.FullSource);
        Assert.Equal(ObjectScriptStatus.Recognized, (await Inspect(deleted, deleted.FullSource)).Status);
        var text = new TextDocument(new MultiRegionProjection(renamed).ViewSource);
        StructuralEditorState? restored = null;
        StructuralEditorHistory.Apply(text, new(renamed, 0, 0, 0), new(deleted, 0, 0, 0), () => { }, s => restored = s);
        text.UndoStack.Undo();
        Assert.Equal(renamed.FullSource, restored!.Document.FullSource);
        Assert.Contains("当收到消息：下一关😀", text.Text);
        text.UndoStack.Redo();
        Assert.Equal(deleted.FullSource, restored!.Document.FullSource);
        Save(deleted);
        Assert.Equal(ObjectScriptStatus.Recognized, (await Inspect(deleted, store.ReadScript(root, project.FindScript(deleted.ScriptId)!.Path))).Status);
    }

    [Fact]
    public async Task BroadcastUsesEntryRouteAndRunsNestedMessagesBeforeContinuingInObjectOrder()
    {
        Save(Body(await Actor("sender"), "start", "broadcast('无人接收')\nbroadcast('开始')\nprint('done')\n"));
        var cat = ObjectScriptFeatures.AddMessage(await Actor("cat"), "开始");
        Save(Body(cat, cat.Regions!.Last().Id, "print('cat')\nbroadcast('内部')\n"));
        var scene = ObjectScriptFeatures.AddMessage(await Actor("scene", ProjectScriptKind.Backdrop), "开始");
        scene = Body(scene, scene.Regions!.Last().Id, "print('scene')\n");
        scene = ObjectScriptFeatures.AddMessage(scene, "内部");
        Save(Body(scene, scene.Regions!.Last().Id, "print('inner')\n"));
        var (exit, events, request) = await Run();
        Assert.Equal(0, exit);
        Assert.Equal("cat\ninner\nscene\ndone\n", Output(events));
        Assert.Equal(3, request.Handlers.Count(h => h.Event == ProjectEventKinds.Broadcast));
        // Deletion changes the runtime bindings without a second persisted binding registry.
        Save(ObjectScriptFeatures.DeleteMessage(cat, cat.Regions!.Last().Id));
        var next = await Run();
        Assert.Equal("scene\ndone\n", Output(next.Events));
    }

    [Fact]
    public async Task ReceiverFailureMapsToVisibleMessageRegionAndAbortsSender()
    {
        Save(Body(await Actor("sender"), "start", "broadcast('开始')\nprint('must not run')\n"));
        var cat = ObjectScriptFeatures.AddMessage(await Actor("cat"), "开始");
        Save(Body(cat, cat.Regions!.Last().Id, "raise ValueError('消息失败')\n"));
        var result = await Run();
        Assert.Equal(1, result.Exit);
        Assert.DoesNotContain("must not run", Output(result.Events));
        var report = new RuntimeErrorPresentationService().Present(Assert.Single(result.Events, e => e.Kind == "exception"), result.Request.PresentationSources!);
        Assert.Contains(report.Frames, f => f.Code.Contains("raise ValueError") && f.DisplayText.Contains("当收到消息：开始"));
        Assert.DoesNotContain("_vac_event_", report.UserText);
        Assert.DoesNotContain(root, report.UserText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitializationBroadcastAndRecursiveMessagesFailClearly(bool recursive)
    {
        var cat = ObjectScriptFeatures.AddMessage(await Actor("cat"), "循环");
        cat = Body(cat, cat.Regions!.Last().Id, "broadcast('循环')\n");
        if (recursive) cat = Body(cat, "start", "broadcast('循环')\n");
        else { cat = ObjectScriptFeatures.AddInitialize(cat); cat = Body(cat, "initialize", "broadcast('循环')\n"); }
        Save(cat);
        var result = await Run();
        Assert.Equal(1, result.Exit);
        var error = Assert.Single(result.Events, e => e.Kind == "exception").Exception!;
        Assert.Contains(recursive ? "32" : "初始化", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UserModulesCanSendButWorkerThreadCannot(bool workerThread)
    {
        var created = new ProjectService(store).CreateScript(project, ProjectScriptKind.User, "api");
        project = created.Project;
        store.WriteScript(root, created.Script.Path, "from vac_runtime import broadcast\nimport threading\n"
            + "def send():\n    broadcast('开始')\n"
            + "def worker():\n    thread = threading.Thread(target=send)\n    thread.start()\n    thread.join()\n");
        var sender = await Actor("sender");
        var imports = await new ScriptImportService(bridge).InspectAsync(project, project.FindScript(sender.ScriptId)!, 1,
            sender.FullSource, sender, [.. sender.Imports!.Entries,
                new("scripts.user.api", Alias: "api", Source: "project", ScriptId: created.Script.Id)]);
        sender = await Inspect(sender, imports.Source);
        Save(Body(sender, "start", workerThread ? "api.worker()\n" : "api.send()\n"));
        var receiver = ObjectScriptFeatures.AddMessage(await Actor("receiver"), "开始");
        Save(Body(receiver, receiver.Regions!.Last().Id, "print('received')\n"));
        var result = await Run();
        Assert.Equal(workerThread ? 1 : 0, result.Exit);
        if (workerThread) Assert.Contains("主线程", Assert.Single(result.Events, e => e.Kind == "exception").Exception!.Message);
        else Assert.Equal("received\n", Output(result.Events));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MessageInputOrLoopCanStopAndNextRunHasFreshBindings(bool loop)
    {
        Save(Body(await Actor("sender"), "start", "broadcast('开始')\n"));
        var receiver = ObjectScriptFeatures.AddMessage(await Actor("receiver"), "开始");
        Save(Body(receiver, receiver.Regions!.Last().Id,
            loop ? "print('waiting', flush=True)\nwhile True: pass\n" : "print('waiting', flush=True)\nprint(input('名字：'))\n"));
        var request = await new ProjectRunService(store, bridge).PrepareAsync(project, PythonEnvironment.DefaultExecutable, default);
        await using var runtime = new PythonRuntimeProcess(Path.Combine(AppContext.BaseDirectory, "python-runtime"));
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new ConcurrentQueue<PythonRunEvent>();
        var running = runtime.RunAsync(request, e =>
        {
            events.Enqueue(e);
            if (e.Kind == "stdout" && e.Text.Contains("waiting")) waiting.TrySetResult();
        }, cancel.Token);
        await waiting.Task.WaitAsync(cancel.Token);
        if (loop)
        {
            cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        else
        {
            await runtime.SendInputAsync("小明", cancel.Token);
            Assert.Equal(0, await running);
            Assert.Contains("小明", Output(events.ToArray()));
        }
        Save(ObjectScriptFeatures.DeleteMessage(receiver, receiver.Regions!.Last().Id));
        var next = await new ProjectRunService(store, bridge).PrepareAsync(project, PythonEnvironment.DefaultExecutable, default);
        using var nextTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        Assert.Equal(0, await runtime.RunAsync(next, _ => { }, nextTimeout.Token));
    }

    [Fact]
    public async Task DeletionCanBeSavedUndoneAndSavedAgainWithoutOverwritingExternalEdits()
    {
        var actor = ObjectScriptFeatures.AddMessage(await Actor("actor"), "开始");
        var id = actor.Regions!.Last().Id;
        actor = Body(actor, id, "print('保留代码')\n");
        Save(actor);
        await using var vmBridge = new PythonProcessBridge();
        await using var vm = new MainViewModel(new InMemorySettingsStore(), new InMemoryRecentProjectsStore(), new ProjectSession(), vmBridge);
        await vm.OpenProject(root);
        await vm.OpenScriptFromExplorerCommand.ExecuteAsync(Assert.Single(vm.ObjectScripts));
        var baseline = (await vm.InspectEditorProtectionAsync(default))!;
        var deleted = ObjectScriptFeatures.DeleteMessage(baseline, id);
        var text = new TextDocument(new MultiRegionProjection(baseline).ViewSource);
        StructuralEditorHistory.Apply(text, new(baseline, 0, 0, 0), new(deleted, 0, 0, 0), () => { },
            state => vm.RestoreEditorStructure(new MultiRegionProjection(state.Document)));
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal("已保存", vm.EditorState);
        Assert.DoesNotContain(id, store.ReadScript(root, project.FindScript(actor.ScriptId)!.Path));
        text.SetOwnerThread(Thread.CurrentThread);
        text.UndoStack.Undo();
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Equal("已保存", vm.EditorState);
        Assert.Contains("print('保留代码')", store.ReadScript(root, project.FindScript(actor.ScriptId)!.Path));
        text.SetOwnerThread(Thread.CurrentThread);
        text.UndoStack.Redo();
        var external = actor.FullSource + "# external\n";
        store.WriteScript(root, project.FindScript(actor.ScriptId)!.Path, external);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.Contains("外部修改", vm.EditorState);
        Assert.Equal(external, store.ReadScript(root, project.FindScript(actor.ScriptId)!.Path));
    }
}
