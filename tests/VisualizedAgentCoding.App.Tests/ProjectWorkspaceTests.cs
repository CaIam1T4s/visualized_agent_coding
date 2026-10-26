using System;
using System.IO;
using System.Text.Json;
using happy_agent_coding.Services;
using happy_agent_coding.ViewModels;

namespace VisualizedAgentCoding.App.Tests;

public class ProjectWorkspaceTests
{
    [Fact]
    public void CreateWritesManifestEntryScriptAndDocumentedDirectories()
    {
        var root = NewTempDirectory();
        try
        {
            ProjectWorkspace.Create(root, "Demo");

            var manifestPath = Path.Combine(root, "project.json");
            Assert.True(File.Exists(manifestPath));
            Assert.Contains("def main():", File.ReadAllText(Path.Combine(root, "scripts", "main.py")));

            foreach (var directory in ProjectWorkspace.ProjectDirectories)
                Assert.True(Directory.Exists(Path.Combine(root, directory)), $"Missing directory: {directory}");

            var manifest = JsonSerializer.Deserialize<ProjectManifest>(File.ReadAllText(manifestPath));
            Assert.NotNull(manifest);
            Assert.Equal("Demo", manifest.Name);
            Assert.Equal("scripts/main.py", manifest.MainScript);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void CreateUsesTheFolderNameWhenNoNameIsGiven()
    {
        var root = NewTempDirectory();
        try
        {
            var project = ProjectWorkspace.Create(root);

            Assert.Equal(new DirectoryInfo(root).Name, project.Name);
            Assert.True(Directory.Exists(Path.Combine(root, "scripts")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void CreateRefusesAFolderThatAlreadyHoldsAProject()
    {
        var root = NewTempDirectory();
        try
        {
            ProjectWorkspace.Create(root, "First");

            Assert.Throws<ProjectFormatException>(() => ProjectWorkspace.Create(root, "Second"));
            Assert.False(ProjectWorkspace.IsProject(Path.Combine(root, "scenes")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void OpenRefusesAFolderWithoutAManifest()
    {
        var root = NewTempDirectory();
        try
        {
            Assert.Throws<ProjectFormatException>(() => ProjectWorkspace.Open(root));
            Assert.False(ProjectWorkspace.IsProject(root));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void OpenRefusesAFolderWithInvalidManifestJson()
    {
        var root = NewTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "project.json"), "{ not json");

            Assert.Throws<ProjectFormatException>(() => ProjectWorkspace.Open(root));
            Assert.False(ProjectWorkspace.IsProject(root));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(99)]
    public void OpenRefusesAnUnsupportedSchemaVersion(int schemaVersion)
    {
        var root = NewTempDirectory();
        try
        {
            ProjectWorkspace.Create(root, "Demo");
            WriteManifest(root, new ProjectManifest(schemaVersion, "Demo", "scripts/main.py"));

            Assert.Throws<ProjectFormatException>(() => ProjectWorkspace.Open(root));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void OpenRefusesAnUnexpectedEntryScript()
    {
        var root = NewTempDirectory();
        try
        {
            ProjectWorkspace.Create(root, "Demo");
            WriteManifest(root, new ProjectManifest(1, "Demo", "scripts/start.py"));

            Assert.Throws<ProjectFormatException>(() => ProjectWorkspace.Open(root));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void OpenRefusesAProjectWhoseEntryScriptIsMissing()
    {
        var root = NewTempDirectory();
        try
        {
            ProjectWorkspace.Create(root, "Demo");
            File.Delete(Path.Combine(root, "scripts", "main.py"));

            Assert.Throws<ProjectFormatException>(() => ProjectWorkspace.Open(root));
            Assert.False(ProjectWorkspace.IsProject(root));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void OpenRestoresAMissingDirectoryWithoutReplacingProjectContent()
    {
        var root = NewTempDirectory();
        try
        {
            ProjectWorkspace.Create(root, "Demo");
            File.WriteAllText(Path.Combine(root, "scripts", "main.py"), "print('kept')");
            Directory.Delete(Path.Combine(root, "assets"), recursive: true);

            var project = ProjectWorkspace.Open(root);

            Assert.Equal("Demo", project.Name);
            Assert.Equal("print('kept')", project.LoadMainScript());
            Assert.True(Directory.Exists(Path.Combine(root, "assets", "images")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ResourceExplorerNeverListsTheEntryScript()
    {
        var root = NewTempDirectory();
        try
        {
            ProjectWorkspace.Create(root, "Demo");
            File.WriteAllText(Path.Combine(root, "scripts", "user", "helpers.py"), "pass");
            var viewModel = new MainViewModel(new TestSettingsStore());

            viewModel.OpenProject(root);

            Assert.DoesNotContain(viewModel.UserScripts, item => item.RelativePath == "scripts/main.py");
            Assert.DoesNotContain(viewModel.ObjectScripts, item => item.RelativePath == "scripts/main.py");
            Assert.DoesNotContain(viewModel.BackdropScripts, item => item.RelativePath == "scripts/main.py");
            Assert.Equal("helpers", Assert.Single(viewModel.UserScripts).DisplayName);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void RejectedProjectKeepsTheCurrentlyOpenProject()
    {
        var validRoot = NewTempDirectory();
        var invalidRoot = NewTempDirectory();
        try
        {
            var viewModel = new MainViewModel(new TestSettingsStore());
            viewModel.CreateProject(validRoot, "Valid");
            viewModel.PythonCode = "print('valid')";

            viewModel.OpenRecentProjectCommand.Execute(invalidRoot);

            Assert.True(viewModel.HasProjectError);
            Assert.Equal("Valid", viewModel.ProjectName);
            Assert.Equal("print('valid')", viewModel.PythonCode);
        }
        finally
        {
            DeleteDirectory(validRoot);
            DeleteDirectory(invalidRoot);
        }
    }

    private static string NewTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"visualized-project-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteManifest(string root, ProjectManifest manifest) =>
        File.WriteAllText(Path.Combine(root, "project.json"), JsonSerializer.Serialize(manifest));

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }

    private sealed class TestSettingsStore : ISettingsStore
    {
        private AppSettings settings = new();

        public AppSettings Load() => settings;

        public void Save(AppSettings value) => settings = value;
    }
}
