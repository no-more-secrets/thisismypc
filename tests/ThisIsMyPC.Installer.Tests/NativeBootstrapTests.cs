using ThisIsMyPC.Installer;

namespace ThisIsMyPC.Installer.Tests;

public sealed class NativeBootstrapTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"tipc-native-bootstrap-{Guid.NewGuid():N}");

    public NativeBootstrapTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void EnsureResourceFile_ReplacesSameLengthPoisonedCacheEntry()
    {
        var target = Path.Combine(_root, "native.dll");
        File.WriteAllBytes(target, [9, 9, 9, 9]);
        using var source = new MemoryStream([1, 2, 3, 4]);

        NativeBootstrap.EnsureResourceFile(source, target);

        Assert.Equal([1, 2, 3, 4], File.ReadAllBytes(target));
    }

    [Fact]
    public void EnsureResourceFile_ReplacesEvenAnExactExistingEntry()
    {
        var target = Path.Combine(_root, "native.dll");
        File.WriteAllBytes(target, [1, 2, 3, 4]);
        File.SetLastWriteTimeUtc(target, DateTime.UnixEpoch);
        using var source = new MemoryStream([1, 2, 3, 4]);

        NativeBootstrap.EnsureResourceFile(source, target);

        Assert.NotEqual(DateTime.UnixEpoch, File.GetLastWriteTimeUtc(target));
        Assert.Equal([1, 2, 3, 4], File.ReadAllBytes(target));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }
}
