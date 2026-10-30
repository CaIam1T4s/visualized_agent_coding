using System.IO;
using SkiaSharp;
using VisualizedAgentCoding.Domain.Projects;

namespace happy_agent_coding.Services;

/// <summary>Read a stable image snapshot and inspect its header before allocating a bitmap.</summary>
public static class StageImageLoader
{
    public static byte[] Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 20 * 1024 * 1024) throw new ProjectOperationException("图片不能超过 20 MB。");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        Validate(bytes);
        return bytes;
    }
    public static void Validate(byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length > 20 * 1024 * 1024) throw new ProjectOperationException("图片为空或超过 20 MB。");
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null) throw new ProjectOperationException("图片内容无法解码，请选择有效图片。");
        var info = codec.Info;
        if (info.Width is <= 0 or > 8192 || info.Height is <= 0 or > 8192 || (long)info.Width * info.Height > 16_000_000)
            throw new ProjectOperationException("图片长宽不能超过 8192 像素，总像素不能超过 1600 万。");
    }
}
