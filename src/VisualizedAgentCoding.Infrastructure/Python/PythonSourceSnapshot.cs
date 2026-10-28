using System.Text;
using VisualizedAgentCoding.Application.Python;

namespace VisualizedAgentCoding.Infrastructure.Python;

/// <summary>Ephemeral disk view for a static analyzer. Owned and cleaned by C#, including cancellation.</summary>
internal sealed class PythonSourceSnapshot : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "vac-analysis-" + Guid.NewGuid().ToString("N"));
    private readonly string originalRoot;
    private PythonSourceSnapshot(string originalRoot) => this.originalRoot = Path.GetFullPath(originalRoot);

    public static async Task<PythonSourceSnapshot> CreateAsync(string projectRoot, string targetUri, PythonSourceDocument[] documents, CancellationToken token)
    {
        var snapshot = new PythonSourceSnapshot(projectRoot);
        try
        {
            if (documents.Length > 2000) throw new PythonBridgeException("引用查询超过 2000 个文件的限制。");
            long bytes = 0;
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Directory.CreateDirectory(snapshot.Root);
            foreach (var document in documents)
            {
                token.ThrowIfCancellationRequested();
                bytes += Encoding.UTF8.GetByteCount(document.Source);
                if (bytes > 20 * 1024 * 1024) throw new PythonBridgeException("引用查询源码超过 20 MiB 限制。");
                var target = new Uri(snapshot.ToSnapshotUri(document.Uri)).LocalPath;
                if (!written.Add(target)) throw new PythonBridgeException("引用快照包含重复文件。");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await File.WriteAllTextAsync(target, document.Source, new UTF8Encoding(false), token).ConfigureAwait(false);
            }
            if (!File.Exists(new Uri(snapshot.ToSnapshotUri(targetUri)).LocalPath))
                throw new PythonBridgeException("引用快照缺少目标文件。");
            return snapshot;
        }
        catch { snapshot.Dispose(); throw; }
    }

    public string ToSnapshotUri(string originalUri) => Map(originalUri, originalRoot, Root);
    public string ToOriginalUri(string snapshotUri) => Map(snapshotUri, Root, originalRoot);

    private static string Map(string uri, string from, string to)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || !parsed.IsFile)
            throw new PythonBridgeException("引用查询仅支持文件 URI。");
        var path = Path.GetFullPath(parsed.LocalPath);
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(from) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || !path.EndsWith(".py", StringComparison.OrdinalIgnoreCase))
            throw new PythonBridgeException("引用查询文件超出项目范围。");
        return new Uri(Path.Combine(to, Path.GetRelativePath(from, path))).AbsoluteUri;
    }

    public void Dispose()
    {
        // Root is generated and owned solely by this instance, never supplied by a request.
        if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
    }
}
