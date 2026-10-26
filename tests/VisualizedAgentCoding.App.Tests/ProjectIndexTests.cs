using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using VisualizedAgentCoding.Application.Projects;
using VisualizedAgentCoding.Domain.Projects;
using VisualizedAgentCoding.Infrastructure.Projects;

namespace VisualizedAgentCoding.App.Tests;

public class ProjectIndexTests
{
    [Fact]
    public void CreateWritesStableProjectAndEntryScriptIds()
    {
        var root = NewTempDirectory();
        try
        {
            var project = CreateService().Create(root, "Demo");

            Assert.Equal(ProjectLayout.CurrentSchemaVersion, project.Manifest.SchemaVersion);
            Assert.StartsWith("project-", project.ProjectId, StringComparison.Ordinal);
            Assert.StartsWith("script-", project.Manifest.MainScriptId, StringComparison.Ordinal);
            Assert.Empty(project.Manifest.Scripts);
            Assert.Empty(project.Manifest.EventHandlers);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void StableIdsSurviveClosingAndReopeningTheProject()
    {
        var root = NewTempDirectory();
        try
        {
            var created = CreateService().Create(root, "Demo");
            var reopened = CreateService().Open(root);

            Assert.Equal(created.ProjectId, reopened.ProjectId);
            Assert.Equal(created.Manifest.MainScriptId, reopened.Manifest.MainScriptId);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void CreateScriptRegistersAStableEntryAndWritesTheFile()
    {
        var root = NewTempDirectory();
        try
        {
            var service = CreateService();
            var project = service.Create(root, "Demo");

            var creation = service.CreateScript(project, ProjectScriptKind.Object, "hero");

            Assert.Equal("scripts/objects/hero.py", creation.Script.Path);
            Assert.Equal(ProjectScriptKind.Object, creation.Script.Kind);
            Assert.StartsWith("object-", creation.Script.ObjectId, StringComparison.Ordinal);
            Assert.Contains("class Hero(ElementBase):", File.ReadAllText(Path.Combine(root, "scripts", "objects", "hero.py")));

            var reopened = service.Open(root);
            var indexed = Assert.Single(reopened.Manifest.Scripts);
            Assert.Equal(creation.Script.Id, indexed.Id);
            Assert.Equal(creation.Script.ObjectId, indexed.ObjectId);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void CreateScriptRefusesADuplicateName()
    {
        var root = NewTempDirectory();
        try
        {
            var service = CreateService();
            var project = service.Create(root, "Demo");
            var creation = service.CreateScript(project, ProjectScriptKind.User, "helpers");

            Assert.Throws<ProjectOperationException>(
                () => service.CreateScript(creation.Project, ProjectScriptKind.User, "helpers"));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData("bad name")]
    [InlineData("bad/name")]
    [InlineData("bad.name")]
    [InlineData("")]
    public void CreateScriptRefusesAnUnsafeName(string name)
    {
        var root = NewTempDirectory();
        try
        {
            var service = CreateService();
            var project = service.Create(root, "Demo");

            Assert.Throws<ProjectOperationException>(
                () => service.CreateScript(project, ProjectScriptKind.User, name));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void RegisteringStartHandlerPersistsItAndServesMatching()
    {
        var root = NewTempDirectory();
        try
        {
            var service = CreateService();
            var project = service.Create(root, "Demo");
            var creation = service.CreateScript(project, ProjectScriptKind.User, "player");

            var updated = service.RegisterEventHandler(
                creation.Project,
                creation.Script.Id,
                new EventHandlerRegistration(ProjectEventKinds.Start, null, "on_start"));

            var matches = service.MatchHandlers(updated, ProjectEventKinds.Start, null);
            var handler = Assert.Single(matches);
            Assert.Equal(creation.Script.Id, handler.ScriptId);
            Assert.Equal("on_start", handler.Handler);

            var reopened = service.Open(root);
            Assert.Single(reopened.Manifest.EventHandlers);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void KeyHandlerOnlyMatchesTheSameKey()
    {
        var root = NewTempDirectory();
        try
        {
            var service = CreateService();
            var project = service.Create(root, "Demo");
            var creation = service.CreateScript(project, ProjectScriptKind.User, "player");
            var updated = service.RegisterEventHandler(
                creation.Project,
                creation.Script.Id,
                new EventHandlerRegistration(ProjectEventKinds.Key, "space", "on_space"));

            Assert.Single(service.MatchHandlers(updated, ProjectEventKinds.Key, "space"));
            Assert.Empty(service.MatchHandlers(updated, ProjectEventKinds.Key, "enter"));
            Assert.Empty(service.MatchHandlers(updated, ProjectEventKinds.Start, null));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void UnregisterRemovesTheBinding()
    {
        var root = NewTempDirectory();
        try
        {
            var service = CreateService();
            var project = service.Create(root, "Demo");
            var creation = service.CreateScript(project, ProjectScriptKind.User, "player");
            var updated = service.RegisterEventHandler(
                creation.Project,
                creation.Script.Id,
                new EventHandlerRegistration(ProjectEventKinds.Broadcast, "go", "on_go"));

            var removed = service.UnregisterEventHandler(
                updated,
                creation.Script.Id,
                ProjectEventKinds.Broadcast,
                "go");

            Assert.Empty(removed.Manifest.EventHandlers);
            Assert.Empty(service.Open(root).Manifest.EventHandlers);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void EventHandlersRequireAnArgumentWhereTheEventNeedsOne()
    {
        var root = NewTempDirectory();
        try
        {
            var service = CreateService();
            var project = service.Create(root, "Demo");
            var creation = service.CreateScript(project, ProjectScriptKind.User, "player");

            Assert.Throws<ProjectOperationException>(() => service.RegisterEventHandler(
                creation.Project,
                creation.Script.Id,
                new EventHandlerRegistration(ProjectEventKinds.Key, null, "on_key")));
            Assert.Throws<ProjectOperationException>(() => service.RegisterEventHandler(
                creation.Project,
                creation.Script.Id,
                new EventHandlerRegistration(ProjectEventKinds.Start, "unexpected", "on_start")));
            Assert.Throws<ProjectOperationException>(() => service.RegisterEventHandler(
                creation.Project,
                creation.Script.Id,
                new EventHandlerRegistration("unknown-event", null, "on_unknown")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void HandlerNamesMustBePythonIdentifiers()
    {
        var root = NewTempDirectory();
        try
        {
            var service = CreateService();
            var project = service.Create(root, "Demo");
            var creation = service.CreateScript(project, ProjectScriptKind.User, "player");

            Assert.Throws<ProjectOperationException>(() => service.RegisterEventHandler(
                creation.Project,
                creation.Script.Id,
                new EventHandlerRegistration(ProjectEventKinds.Start, null, "not a name")));
            Assert.Throws<ProjectOperationException>(() => service.RegisterEventHandler(
                creation.Project,
                creation.Script.Id,
                new EventHandlerRegistration(ProjectEventKinds.Start, null, "1st")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void SpriteClickNeedsAnObjectScriptAndBackdropSwitchABackdropScript()
    {
        var root = NewTempDirectory();
        try
        {
            var service = CreateService();
            var project = service.Create(root, "Demo");
            var user = service.CreateScript(project, ProjectScriptKind.User, "player");

            Assert.Throws<ProjectOperationException>(() => service.RegisterEventHandler(
                user.Project,
                user.Script.Id,
                new EventHandlerRegistration(ProjectEventKinds.SpriteClick, null, "on_click")));

            var background = service.CreateScript(user.Project, ProjectScriptKind.Backdrop, "forest");
            Assert.Throws<ProjectOperationException>(() => service.RegisterEventHandler(
                background.Project,
                user.Script.Id,
                new EventHandlerRegistration(ProjectEventKinds.BackdropSwitch, "night", "on_switch")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void UnknownScriptIdIsRefused()
    {
        var root = NewTempDirectory();
        try
        {
            var service = CreateService();
            var project = service.Create(root, "Demo");

            Assert.Throws<ProjectOperationException>(() => service.RegisterEventHandler(
                project,
                "script-does-not-exist",
                new EventHandlerRegistration(ProjectEventKinds.Start, null, "on_start")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void OpenAdoptsScriptsCreatedOutsideTheIndex()
    {
        var root = NewTempDirectory();
        try
        {
            var service = CreateService();
            service.Create(root, "Demo");
            File.WriteAllText(Path.Combine(root, "scripts", "user", "external.py"), "pass");

            var reopened = service.Open(root);

            var adopted = Assert.Single(reopened.Manifest.Scripts);
            Assert.Equal("scripts/user/external.py", adopted.Path);
            Assert.StartsWith("script-", adopted.Id, StringComparison.Ordinal);
            Assert.Single(service.Open(root).Manifest.Scripts);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void OpenRefusesAnIndexedScriptThatIsMissingOnDisk()
    {
        var root = NewTempDirectory();
        try
        {
            var service = CreateService();
            var project = service.Create(root, "Demo");
            var creation = service.CreateScript(project, ProjectScriptKind.User, "helpers");
            File.Delete(Path.Combine(root, "scripts", "user", "helpers.py"));

            Assert.Throws<ProjectFormatException>(() => service.Open(root));
            Assert.False(service.IsProject(root));
            Assert.Equal("scripts/user/helpers.py", creation.Script.Path);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void OpenRefusesDuplicateScriptIds()
    {
        var root = NewTempDirectory();
        try
        {
            var service = CreateService();
            var project = service.Create(root, "Demo");
            var first = service.CreateScript(project, ProjectScriptKind.User, "first");
            var second = service.CreateScript(first.Project, ProjectScriptKind.User, "second");

            var manifest = second.Project.Manifest with
            {
                Scripts = second.Project.Manifest.Scripts
                    .Select(script => script.Path.EndsWith("second.py", StringComparison.Ordinal)
                        ? script with { Id = first.Script.Id }
                        : script)
                    .ToList(),
            };
            WriteManifest(root, manifest);

            Assert.Throws<ProjectFormatException>(() => service.Open(root));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void OpenRefusesAScriptDeclaredWithTheWrongKind()
    {
        var root = NewTempDirectory();
        try
        {
            var service = CreateService();
            var project = service.Create(root, "Demo");
            var creation = service.CreateScript(project, ProjectScriptKind.Object, "hero");

            var manifest = creation.Project.Manifest with
            {
                Scripts = creation.Project.Manifest.Scripts
                    .Select(script => script with { Kind = ProjectScriptKind.User, ObjectId = null })
                    .ToList(),
            };
            WriteManifest(root, manifest);

            Assert.Throws<ProjectFormatException>(() => service.Open(root));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void SchemaVersionOneManifestIsUpgradedOnOpen()
    {
        var root = NewTempDirectory();
        try
        {
            var service = CreateService();
            var project = service.Create(root, "Demo");
            var legacy = JsonSerializer.Serialize(
                project.Manifest with { SchemaVersion = 1, ProjectId = string.Empty, MainScriptId = string.Empty },
                ManifestJsonOptions);
            File.WriteAllText(Path.Combine(root, "project.json"), legacy);

            var upgraded = service.Open(root);

            Assert.Equal(ProjectLayout.CurrentSchemaVersion, upgraded.Manifest.SchemaVersion);
            Assert.StartsWith("project-", upgraded.ProjectId, StringComparison.Ordinal);
            Assert.StartsWith("script-", upgraded.Manifest.MainScriptId, StringComparison.Ordinal);
            Assert.Contains("\"schemaVersion\": 2", File.ReadAllText(Path.Combine(root, "project.json")));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public void ManifestIsWrittenWithCamelCaseFieldNames()
    {
        var root = NewTempDirectory();
        try
        {
            var service = CreateService();
            service.Create(root, "Demo");

            var json = File.ReadAllText(Path.Combine(root, "project.json"));
            Assert.Contains("\"schemaVersion\"", json);
            Assert.Contains("\"projectId\"", json);
            Assert.Contains("\"mainScriptId\"", json);
            Assert.Contains("\"eventHandlers\"", json);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static ProjectService CreateService() => new(new JsonProjectStore());

    private static string NewTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"visualized-index-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteManifest(string root, ProjectManifest manifest) =>
        File.WriteAllText(Path.Combine(root, "project.json"), JsonSerializer.Serialize(manifest, ManifestJsonOptions));

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
