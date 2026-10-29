using System;
using System.IO;
using happy_agent_coding.Services;
using happy_agent_coding.ViewModels;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Infrastructure.Projects;

namespace VisualizedAgentCoding.App.Tests;

public class MainViewModelTests
{
    [Fact]
    public async Task StartsWithoutAProjectOrEditorDocument()
    {
        await using var viewModel = TestMainViewModelFactory.Create(new InMemorySettingsStore());

        Assert.Equal(string.Empty, viewModel.PythonCode);
        Assert.Equal("1", viewModel.LineNumbers);
        Assert.Equal(string.Empty, viewModel.FileName);
        Assert.False(viewModel.HasOpenProject);
        Assert.False(viewModel.IsEditorVisible);
        Assert.True(viewModel.ShowOpenProjectPrompt);
        Assert.Equal("尚未打开项目", viewModel.EditorEmptyPromptTitle);
        Assert.Equal("打开或新建一个项目后，即可在此编辑脚本。", viewModel.EditorEmptyPromptMessage);
        Assert.False(viewModel.ShowStageCanvas);
    }

    [Fact]
    public async Task SavingWithoutAProjectReportsThatNoProjectIsOpen()
    {
        await using var viewModel = TestMainViewModelFactory.Create(new InMemorySettingsStore());

        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("尚未打开项目", viewModel.EditorState);
    }

    [Fact]
    public async Task OpeningAProjectLeavesTheEditorEmptyAndDoesNotExposeTheEntryScript()
    {
        var root = Path.Combine(Path.GetTempPath(), $"visualized-project-{Guid.NewGuid():N}");
        try
        {
            await using var viewModel = TestMainViewModelFactory.Create(new InMemorySettingsStore());
            await viewModel.CreateProject(root, "Demo");

            Assert.Equal("Demo", viewModel.ProjectName);
            Assert.True(Directory.Exists(Path.Combine(root, "scripts", "user")));
            Assert.Contains("def main():", File.ReadAllText(Path.Combine(root, "scripts", "main.py")));
            Assert.Equal(string.Empty, viewModel.PythonCode);
            Assert.Equal(string.Empty, viewModel.FileName);
            Assert.Null(viewModel.OpenScript);
            Assert.False(viewModel.IsEditorVisible);
            Assert.True(viewModel.ShowStageCanvas);
            Assert.False(viewModel.ShowOpenProjectPrompt);
            Assert.Equal("尚未打开脚本", viewModel.EditorEmptyPromptTitle);
            Assert.Contains("资源管理器", viewModel.EditorEmptyPromptMessage);
            Assert.Empty(viewModel.UserScripts);
            Assert.Empty(viewModel.ObjectScripts);
            Assert.Empty(viewModel.BackdropScripts);

            viewModel.PythonCode = "print('saved')";
            await viewModel.SaveCommand.ExecuteAsync(null);

            Assert.Equal("尚未打开脚本", viewModel.EditorState);
            Assert.Contains("def main():", File.ReadAllText(Path.Combine(root, "scripts", "main.py")));

            await using var reopened = TestMainViewModelFactory.Create(new InMemorySettingsStore());
            await reopened.OpenProject(root);

            Assert.Equal(string.Empty, reopened.PythonCode);
            Assert.Null(reopened.OpenScript);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ResourceExplorerGroupsOnlyScriptsFromTheThreeScriptDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), $"visualized-project-{Guid.NewGuid():N}");
        try
        {
            new happy_agent_coding.Services.ProjectSession().Create(root, "Demo");
            File.WriteAllText(Path.Combine(root, "scripts", "user", "helpers.py"), "pass");
            File.WriteAllText(Path.Combine(root, "scripts", "objects", "hero.py"), "pass");
            File.WriteAllText(Path.Combine(root, "scripts", "backdrops", "forest.py"), "pass");
            File.WriteAllText(Path.Combine(root, "scripts", "user", "notes.json"), "{}");
            Directory.CreateDirectory(Path.Combine(root, "assets", "images"));
            File.WriteAllText(Path.Combine(root, "assets", "images", "hero.png"), "image");
            await using var viewModel = TestMainViewModelFactory.Create(new InMemorySettingsStore());
            await viewModel.OpenProject(root);

            Assert.Equal("helpers", Assert.Single(viewModel.UserScripts).DisplayName);
            Assert.Equal("hero", Assert.Single(viewModel.ObjectScripts).DisplayName);
            Assert.Equal("forest", Assert.Single(viewModel.BackdropScripts).DisplayName);

            viewModel.ResourceSearchQuery = "hero";

            Assert.Empty(viewModel.UserScripts);
            Assert.Equal("scripts/objects/hero.py", Assert.Single(viewModel.ObjectScripts).RelativePath);
            Assert.Empty(viewModel.BackdropScripts);
            Assert.Equal("1 项脚本", viewModel.ProjectResourceStatus);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SwitchingProjectsClearsTheEditorWithoutWritingTheEntryScript()
    {
        var first = Path.Combine(Path.GetTempPath(), $"visualized-project-{Guid.NewGuid():N}");
        var second = Path.Combine(Path.GetTempPath(), $"visualized-project-{Guid.NewGuid():N}");
        try
        {
            await using var viewModel = TestMainViewModelFactory.Create(new InMemorySettingsStore());
            await viewModel.CreateProject(first);
            var originalMainScript = File.ReadAllText(Path.Combine(first, "scripts", "main.py"));
            viewModel.PythonCode = "print('first')";
            await viewModel.CreateProject(second);

            Assert.Equal(originalMainScript, File.ReadAllText(Path.Combine(first, "scripts", "main.py")));
            Assert.Equal(string.Empty, viewModel.PythonCode);
            Assert.Equal(Path.GetFullPath(second), viewModel.ProjectRootPath);

            await viewModel.CloseProject();
            Assert.Null(viewModel.ProjectName);
            Assert.Equal(string.Empty, viewModel.PythonCode);
        }
        finally
        {
            if (Directory.Exists(first))
                Directory.Delete(first, recursive: true);
            if (Directory.Exists(second))
                Directory.Delete(second, recursive: true);
        }
    }
}
