using ThisIsMyPC.Interop.Com.Packages;
using Windows.Foundation;

namespace ThisIsMyPC.Integration.Tests.Packages;

public class AppxDeploymentWaitTests
{
    [Fact]
    public async Task StartedDeployment_WaitsForCompletionBeforeReadingResult()
    {
        var reads = 0;
        var closed = false;
        var result = await AppxPackageService.WaitForDeploymentAsync(
            () => ++reads < 3 ? AsyncStatus.Started : AsyncStatus.Completed,
            () => { Assert.Equal(3, reads); return 42; },
            () => closed = true);
        Assert.Equal(42, result);
        Assert.True(closed);
    }

    [Theory]
    [InlineData(AsyncStatus.Error)]
    [InlineData(AsyncStatus.Canceled)]
    public async Task TerminalFailure_ReadsNativeErrorAndClosesOperation(AsyncStatus status)
    {
        var closed = false;
        var expected = new InvalidOperationException("Native deployment error");
        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AppxPackageService.WaitForDeploymentAsync<int>(() => status,
                () => throw expected, () => closed = true));
        Assert.Same(expected, actual);
        Assert.True(closed);
    }

    [Fact]
    public async Task StatusFailure_ClosesOperation()
    {
        var closed = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AppxPackageService.WaitForDeploymentAsync(
                () => throw new InvalidOperationException("Status failed"),
                () => 42, () => closed = true));
        Assert.True(closed);
    }
}
