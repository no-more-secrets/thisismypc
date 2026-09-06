using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Services;

/// <summary>A small shell icon as raw 32-bit BGRA pixels, top-down, unpremultiplied.</summary>
public sealed record FileIcon(int Width, int Height, byte[] Bgra);

/// <summary>The generic shell icon for a file type. The implementation must not parse the target file.</summary>
public interface IFileIconService
{
    OperationResult<FileIcon> GetSmallIcon(string path);
}
