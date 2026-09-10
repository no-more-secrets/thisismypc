using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Core.Services;

/// <summary>Deletes a file only while its verified content and identity remain protected from concurrent changes.</summary>
public interface IComparedFileDeletionService
{
    OperationResult<bool> DeleteIfMatches(string path, byte[] expected);
}
