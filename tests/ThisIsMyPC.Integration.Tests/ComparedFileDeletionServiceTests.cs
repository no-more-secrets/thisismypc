using ThisIsMyPC.Interop.Win32;

namespace ThisIsMyPC.Integration.Tests;

public sealed class ComparedFileDeletionServiceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "tipc-compare-delete-" + Guid.NewGuid().ToString("N"));
    private readonly ComparedFileDeletionService _service = new();

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    [InlineData(131072)]
    public void MatchingFile_IsDeleted(int length)
    {
        var bytes = new byte[length];
        Random.Shared.NextBytes(bytes);
        File.WriteAllBytes(_path, bytes);
        var result = _service.DeleteIfMatches(_path, bytes);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.False(File.Exists(_path));
    }

    [Theory]
    [InlineData("different")]
    [InlineData("old")]
    public void ChangedFile_IsPreserved(string current)
    {
        File.WriteAllText(_path, current);
        Assert.False(_service.DeleteIfMatches(_path, "expected!"u8.ToArray()).IsSuccess);
        Assert.Equal(current, File.ReadAllText(_path));
    }

    [Fact]
    public void OpenWriter_PreventsDeletion()
    {
        File.WriteAllBytes(_path, [1, 2, 3]);
        using (var writer = new FileStream(_path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete))
            Assert.False(_service.DeleteIfMatches(_path, [1, 2, 3]).IsSuccess);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(_path));
    }

    [Fact]
    public void MissingFile_ReturnsFailure() => Assert.False(_service.DeleteIfMatches(_path, []).IsSuccess);

    public void Dispose() => File.Delete(_path);
}
