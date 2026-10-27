using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using VisualizedAgentCoding.Application.Projects;

namespace VisualizedAgentCoding.Infrastructure.Projects;

public sealed class JsonRecentProjectsStore : IRecentProjectsStore
{
    private const string ApplicationFolderName = "VisualizedAgentCoding";
    private const string RecentProjectsFileName = "recent-projects.json";
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    public JsonRecentProjectsStore(string? filePath = null)
    {
        FilePath = filePath ?? GetDefaultFilePath();
    }

    public string FilePath { get; }

    public IReadOnlyList<RecentProjectEntry> Load()
    {
        if (!File.Exists(FilePath))
            return [];

        try
        {
            return JsonSerializer.Deserialize<List<RecentProjectEntry>>(
                       File.ReadAllText(FilePath), SerializerOptions)
                   ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }
    }

    public void Save(IEnumerable<RecentProjectEntry> projects)
    {
        ArgumentNullException.ThrowIfNull(projects);

        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var temporaryPath = FilePath + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(projects, SerializerOptions);
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public static string GetDefaultFilePath()
    {
        var roamingFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(roamingFolder))
            roamingFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return Path.Combine(roamingFolder, ApplicationFolderName, RecentProjectsFileName);
    }
}
