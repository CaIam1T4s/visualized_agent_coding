using VisualizedAgentCoding.Application.Editor.ObjectScripts;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Domain.Projects;
using VisualizedAgentCoding.Infrastructure.Projects;
using VisualizedAgentCoding.Infrastructure.Python;

namespace VisualizedAgentCoding.App.Tests;

public sealed class MultiRegionObjectScriptTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "multi-region-" + Guid.NewGuid().ToString("N"));
    private readonly JsonProjectStore store = new();

    [Theory]
    [InlineData(ProjectScriptKind.Object, "\n")]
    [InlineData(ProjectScriptKind.Backdrop, "\r\n")]
    public async Task DefaultAndExtendedV2TemplatesRoundTripWithoutChangingAnyCharacter(ProjectScriptKind kind, string newline)
    {
        var projects = new ProjectService(store);
        var created = projects.CreateScript(projects.Create(root, "Demo"), kind, "hero");
        await using var bridge = new PythonProcessBridge();
        var documents = new ObjectScriptDocumentService(bridge);
        foreach (var definitions in new ObjectScriptRegionDefinition[]?[] { null,
            [new("members", "members"), new("init", "initialize"), new("start", "start"),
             new("click", "event", "clicked"), new("message", "event", "message:你好😀")] })
        {
            var source = ProjectScriptTemplates.CreateMultiRegion(kind, "Hero", definitions).Replace("\n", newline);
            store.WriteScript(root, created.Script.Path, source);
            var document = await documents.InspectAsync(created.Project, created.Script.Id, 1, source);
            Assert.Equal(ObjectScriptStatus.Recognized, document.Status);
            Assert.Equal(2, document.TemplateVersion);
            Assert.True(document.HasRegions);
            Assert.False(document.HasUserRegion);
            Assert.Equal(definitions?.Length ?? 2, document.Regions!.Length);
            Assert.Equal("", Slice(document, document.Regions[0]));
            Assert.All(document.Regions.Skip(1), r => Assert.Equal("        " + ProjectScriptTemplates.UserPlaceholderStatement + newline, Slice(document, r)));
            var plan = ObjectScriptRegionSaveService.Prepare(created.Project, document, 2, new Dictionary<string, string>());
            Assert.Equal(source, plan.MergedSource);
            var saves = new ObjectScriptSaveService(documents, store);
            saves.Apply(plan, 2);
            Assert.Equal(source, store.ReadScript(root, created.Script.Path));
            var reopened = await documents.InspectAsync(created.Project, created.Script.Id, 2, plan.MergedSource);
            Assert.Equal(document.Regions, reopened.Regions);
            Assert.Empty((await bridge.ParseAsync(new(document.Uri, 2, source))).Diagnostics);
        }
    }

    [Fact]
    public async Task RegionEditPreservesOtherBodiesAndShellUpdatesOffsetsAndKeepsEmptyBody()
    {
        var projects = new ProjectService(store);
        var created = projects.CreateScript(projects.Create(root, "Demo"), ProjectScriptKind.Object, "hero");
        var source = ProjectScriptTemplates.CreateMultiRegion(ProjectScriptKind.Object, "Hero");
        store.WriteScript(root, created.Script.Path, source);
        await using var bridge = new PythonProcessBridge();
        var documents = new ObjectScriptDocumentService(bridge);
        var document = await documents.InspectAsync(created.Project, created.Script.Id, 1, source);
        var body = "    text = \"\"\"你好😀\n string content\n\"\"\"\n    # 类定义\n";
        var plan = ObjectScriptRegionSaveService.Prepare(created.Project, document, 2,
            new Dictionary<string, string> { ["members"] = body });
        Assert.Equal(source.Insert(document.Regions![0].Span.Offset, body), plan.MergedSource);
        Assert.Equal(document.Regions[1].Span.Offset + body.Length, plan.Document!.Regions![1].Span.Offset);
        Assert.Equal("        " + ProjectScriptTemplates.UserPlaceholderStatement + "\n", Slice(plan.Document, plan.Document.Regions[1]));
        var saves = new ObjectScriptSaveService(documents, store);
        saves.Apply(plan, 2);
        var reopened = await documents.InspectAsync(created.Project, created.Script.Id, 2, plan.MergedSource);
        Assert.Equal(body, Slice(reopened, reopened.Regions![0]));
        var empty = ObjectScriptRegionSaveService.Prepare(created.Project, reopened, 3,
            new Dictionary<string, string> { ["start"] = "" });
        saves.Apply(empty, 3);
        var emptyReopened = await documents.InspectAsync(created.Project, created.Script.Id, 3, empty.MergedSource);
        Assert.True(emptyReopened.HasRegions);
        Assert.Equal("", Slice(emptyReopened, emptyReopened.Regions![1]));
        Assert.NotEmpty((await bridge.ParseAsync(new(document.Uri, 3, empty.MergedSource))).Diagnostics);
        Assert.Throws<ProjectOperationException>(() => ObjectScriptRegionSaveService.Prepare(created.Project, reopened, 1, new Dictionary<string, string>()));
        Assert.Throws<ProjectOperationException>(() => ObjectScriptRegionSaveService.Prepare(created.Project, reopened, 3, new Dictionary<string, string> { ["missing"] = "" }));
        Assert.Throws<ProjectOperationException>(() => ObjectScriptRegionSaveService.Prepare(created.Project, reopened, 3, new Dictionary<string, string> { ["start"] = "        value = 1" }));
        Assert.Throws<ProjectOperationException>(() => saves.Apply(plan, 3));
        Assert.Throws<ProjectOperationException>(() => saves.Apply(plan, 2));
        Assert.Equal(empty.MergedSource, store.ReadScript(root, created.Script.Path));
    }

    [Fact]
    public async Task FullBufferSaveRejectsSystemEditsAndRegionIdentityChanges()
    {
        var projects = new ProjectService(store);
        var created = projects.CreateScript(projects.Create(root, "Demo"), ProjectScriptKind.Object, "hero");
        var source = ProjectScriptTemplates.CreateMultiRegion(ProjectScriptKind.Object, "Hero");
        store.WriteScript(root, created.Script.Path, source);
        await using var bridge = new PythonProcessBridge();
        var saves = new ObjectScriptSaveService(new ObjectScriptDocumentService(bridge), store);
        var edited = source.Replace("        " + ProjectScriptTemplates.UserPlaceholderStatement + "\n", "        self.value = 1\n");
        var plan = await saves.PrepareFullBufferAsync(created.Project, created.Script.Id, 2, source, edited);
        saves.Apply(plan, 2);
        Assert.Equal(edited, store.ReadScript(root, created.Script.Path));
        await Assert.ThrowsAsync<ProjectOperationException>(() => saves.PrepareFullBufferAsync(created.Project, created.Script.Id, 3,
            edited, edited.Replace("from runtime.element_base import ElementBase", "from runtime.element_base import ElementBase # changed")));
        await Assert.ThrowsAsync<ProjectOperationException>(() => saves.PrepareFullBufferAsync(created.Project, created.Script.Id, 3,
            edited, edited.Replace("\"id\":\"start\"", "\"id\":\"other\"").Replace("# vac:region-end start", "# vac:region-end other")));
    }

    [Fact]
    public void FactoryRejectsInvalidDefinitionsAndDoesNotChangeDefaultV1Creation()
    {
        Assert.StartsWith("# vac:template-version=1", ProjectScriptTemplates.Create(ProjectScriptKind.Object, "Hero"));
        Assert.Throws<ArgumentException>(() => ProjectScriptTemplates.CreateMultiRegion(ProjectScriptKind.User, "Hero"));
        Assert.Throws<ArgumentException>(() => ProjectScriptTemplates.CreateMultiRegion(ProjectScriptKind.Object, "Hero",
            [new("members", "members"), new("members", "start")]));
        Assert.Throws<ArgumentException>(() => ProjectScriptTemplates.CreateMultiRegion(ProjectScriptKind.Object, "Hero",
            [new("members", "members"), new("start", "start"), new("a", "event", "clicked"), new("b", "event", "clicked")]));
    }

    private static string Slice(ObjectScriptDocument document, ObjectScriptRegion region) => document.FullSource.Substring(region.Span.Offset, region.Span.Length);
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
