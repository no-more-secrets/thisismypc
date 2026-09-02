using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Services;

/// <summary>A small shell icon as raw 32-bit BGRA pixels, top-down, unpremultiplied.</summary>
public sealed record FileIcon(int Width, int Height, byte[] Bgra);

/// <summary>The icon Explorer would show for a file (or for its type, when the file is missing).</summary>
public interface IFileIconService
{
    OperationResult<FileIcon> GetSmallIcon(string path);
}
