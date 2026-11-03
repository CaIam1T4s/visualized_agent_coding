using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VisualizedAgentCoding.Domain.Blueprint;

namespace happy_agent_coding.Services;

/// <summary>
/// Reads and writes the view-only node layout. The new location is
/// <c>blueprint/layout.json</c>; the old <c>blocks/layout.json</c> is read once and migrated.
/// A missing or corrupt layout falls back to the default layout and never blocks editing.
/// </summary>
public sealed class BlueprintLayoutStore
{
    public const string NewRelativePath = "blueprint/layout.json";
    public const string OldRelativePath = "blocks/layout.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public async Task<BlueprintLayout> LoadAsync(string projectRoot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        var newPath = Path.Combine(projectRoot, NewRelativePath);
        if (File.Exists(newPath))
        {
            var loaded = await ReadAsync(newPath, cancellationToken).ConfigureAwait(false);
            return loaded is { Version: 1 } ? loaded : new BlueprintLayout();
        }

        var oldPath = Path.Combine(projectRoot, OldRelativePath);
        if (!File.Exists(oldPath))
            return new BlueprintLayout();

        var migrated = await ReadAsync(oldPath, cancellationToken).ConfigureAwait(false);
        if (migrated is not { Version: 1 }) migrated = new BlueprintLayout();
        await TryWriteAsync(newPath, migrated, cancellationToken).ConfigureAwait(false);
        return migrated;
    }

    public async Task SaveAsync(string projectRoot, BlueprintLayout layout, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        ArgumentNullException.ThrowIfNull(layout);
        var path = Path.Combine(projectRoot, NewRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(layout, JsonOptions);
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, json, cancellationToken).ConfigureAwait(false);
        File.Move(temporary, path, overwrite: true);
    }

    private static async Task<BlueprintLayout?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<BlueprintLayout>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static async Task TryWriteAsync(string path, BlueprintLayout layout, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var json = JsonSerializer.Serialize(layout, JsonOptions);
            await File.WriteAllTextAsync(path, json, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // Layout is disposable view state; migration failure must not block the project.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
