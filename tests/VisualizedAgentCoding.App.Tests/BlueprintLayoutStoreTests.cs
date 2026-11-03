using happy_agent_coding.Services;
using VisualizedAgentCoding.Domain.Blueprint;

namespace VisualizedAgentCoding.App.Tests;

public sealed class BlueprintLayoutStoreTests
{
    [Fact]
    public async Task LoadsOldLayoutAndWritesNewPath()
    {
        var root = CreateRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "blocks"));
            await File.WriteAllTextAsync(Path.Combine(root, "blocks", "layout.json"), """
                {
                  "version": 1,
                  "scripts": {
                    "script-1": {
                      "regions": {
                        "start": {
                          "nodes": {
                            "node-1": { "x": 12, "y": 34, "collapsed": true, "fingerprint": "motion.move" }
                          }
                        }
                      }
                    }
                  }
                }
                """);

            var layout = await new BlueprintLayoutStore().LoadAsync(root);

            var node = layout.Scripts["script-1"].Regions["start"].Nodes["node-1"];
            Assert.Equal(12, node.X);
            Assert.Equal(34, node.Y);
            Assert.True(node.Collapsed);
            Assert.True(File.Exists(Path.Combine(root, "blueprint", "layout.json")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task NewLayoutWinsOverOldLayout()
    {
        var root = CreateRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "blocks"));
            Directory.CreateDirectory(Path.Combine(root, "blueprint"));
            await File.WriteAllTextAsync(Path.Combine(root, "blocks", "layout.json"),
                """{"version":1,"scripts":{"old":{"regions":{}}}}""");
            await File.WriteAllTextAsync(Path.Combine(root, "blueprint", "layout.json"),
                """{"version":1,"scripts":{"new":{"regions":{}}}}""");

            var layout = await new BlueprintLayoutStore().LoadAsync(root);

            Assert.True(layout.Scripts.ContainsKey("new"));
            Assert.False(layout.Scripts.ContainsKey("old"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task CorruptLayoutFallsBackToEmpty()
    {
        var root = CreateRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "blueprint"));
            await File.WriteAllTextAsync(Path.Combine(root, "blueprint", "layout.json"), "{ not json");

            var layout = await new BlueprintLayoutStore().LoadAsync(root);

            Assert.Empty(layout.Scripts);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "vac-blueprint-layout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
