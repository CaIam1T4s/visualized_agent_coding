using happy_agent_coding.Services;
using happy_agent_coding.ViewModels;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Application.Python;
using VisualizedAgentCoding.Domain.Projects;
using VisualizedAgentCoding.Infrastructure.Projects;
using VisualizedAgentCoding.Infrastructure.Python;

namespace VisualizedAgentCoding.App.Tests;

public sealed class BlockProjectionTests
{
    [Fact]
    public async Task HelperProjectsSupportedBlocksAndPreservesRawCode()
    {
        await using var bridge = new PythonProcessBridge();
        var tree = await bridge.BuildBlockTreeAsync(new("""
self.move(10)
for _ in range(3):
    self.turn(15)
custom_call()
"""));
        Assert.Null(tree.Error);
        Assert.Equal(["motion.move", "control.repeat", "raw"], tree.Nodes.Select(node => node.Type));
        Assert.Equal("motion.turn", Assert.Single(tree.Nodes[1].Children!).Type);
    }

    [Fact]
    public async Task OpenObjectStartRegionPopulatesReadOnlyBlockLines()
    {
        var root = Path.Combine(Path.GetTempPath(), "vac-blocks-" + Guid.NewGuid().ToString("N"));
        var store = new JsonProjectStore();
        var project = new ProjectService(store).Create(root);
        var created = new ProjectService(store).CreateScript(project, ProjectScriptKind.Object, "cat");
        var source = ProjectScriptTemplates.CreateWithImports(ProjectScriptKind.Object, "Cat")
            .Replace(ProjectScriptTemplates.UserPlaceholderStatement, "self.move(10)\n        wait(1)");
        store.WriteScript(project.RootPath, created.Script.Path, source);
        try
        {
            await using var bridge = new PythonProcessBridge();
            await using var vm = new MainViewModel(new JsonSettingsStore(Path.Combine(root, "settings.json")),
                new InMemoryRecentProjectsStore(), new ProjectSession(), bridge);
            await vm.OpenProject(root);
            await vm.OpenScriptFromExplorerCommand.ExecuteAsync(Assert.Single(vm.ObjectScripts));
            vm.EditorModeIndex = 1;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!vm.BlockEditor.HasBlocks) await Task.Delay(20, timeout.Token);
            Assert.Contains(vm.BlockEditor.Lines, line => line.Text.Contains("当启动时"));
            Assert.Contains(vm.BlockEditor.Lines, line => line.Text.Contains("移动 10 步"));
            Assert.Contains(vm.BlockEditor.Lines, line => line.Text.Contains("等待 1 秒"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
