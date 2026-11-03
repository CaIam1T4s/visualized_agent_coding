using happy_agent_coding.ViewModels;
using VisualizedAgentCoding.Application.Editor.ObjectScripts;

namespace VisualizedAgentCoding.App.Tests;

public sealed class BlueprintClassPageTests
{
    [Fact]
    public void BuildsMethodAndEventNavigationAndSelectsStartByDefault()
    {
        var page = new BlueprintClassPageModel();
        page.SetRegions("cat", "Cat",
        [
            new ObjectScriptRegion("members", "members", new SourceSpan(0, 0), 4),
            new ObjectScriptRegion("init", "initialize", new SourceSpan(0, 0), 4),
            new ObjectScriptRegion("start", "start", new SourceSpan(0, 0), 4),
            new ObjectScriptRegion("click", "event", new SourceSpan(0, 0), 4, "sprite-click"),
        ], null, null);

        Assert.Equal(["类定义", "初始化", "程序启动时运行", "当角色被点击"], page.Canvases.Select(item => item.Title));
        Assert.Equal("start", page.SelectedCanvas?.RegionId);
    }

    [Fact]
    public void KeepsExplicitSelection()
    {
        var page = new BlueprintClassPageModel();
        page.SetRegions("cat", "Cat",
        [
            new ObjectScriptRegion("start", "start", new SourceSpan(0, 0), 4),
            new ObjectScriptRegion("init", "initialize", new SourceSpan(0, 0), 4),
        ], null, "init");

        Assert.Equal("init", page.SelectedCanvas?.RegionId);
    }
}
