using System.Runtime.InteropServices;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Interop.Win32.Restore;

namespace ThisIsMyPC.Integration.Tests;

public class RestorePointServiceTests
{
    [Theory]
    [InlineData(RestorePointOutcome.Created)]
    [InlineData(RestorePointOutcome.Failed)]
    public void ExistingProtectionOrOtherFailure_DoesNotEnable(RestorePointOutcome outcome)
    {
        var expected = new RestorePointResult { Outcome = outcome };
        var actual = RestorePointService.CreateWithProtection("Before changes", _ => expected,
            () => throw new InvalidOperationException("Must not enable"));
        Assert.Same(expected, actual);
    }

    [Fact]
    public void DisabledProtection_EnablesThenRetriesSameDescription()
    {
        var calls = new List<string>();
        var created = new RestorePointResult { Outcome = RestorePointOutcome.Created, SequenceNumber = 42 };
        var result = RestorePointService.CreateWithProtection("Before changes", description =>
        {
            calls.Add(description);
            return calls.Count == 1 ? Disabled() : created;
        }, () =>
        {
            Assert.Single(calls);
            return 0;
        });
        Assert.Same(created, result);
        Assert.Equal(new[] { "Before changes", "Before changes" }, calls);
    }

    [Theory]
    [InlineData(5, "Windows error 5")]
    [InlineData(1260, "administrator policy")]
    public void EnableFailure_DoesNotRetryOrClaimSuccess(uint status, string message)
    {
        var calls = 0;
        var result = RestorePointService.CreateWithProtection("Before changes", _ =>
        {
            calls++;
            return Disabled();
        }, () => status);
        Assert.Equal(1, calls);
        Assert.False(result.IsSuccess);
        Assert.Equal(RestorePointOutcome.SystemRestoreDisabled, result.Outcome);
        Assert.Contains(message, result.Message);
    }

    [Theory]
    [InlineData(RestorePointOutcome.SystemRestoreDisabled)]
    [InlineData(RestorePointOutcome.Failed)]
    public void RetryFailure_ReturnsFailureAndNeverEnablesTwice(RestorePointOutcome outcome)
    {
        var calls = 0;
        var enables = 0;
        var expected = new RestorePointResult { Outcome = outcome, Message = "Retry refused" };
        var result = RestorePointService.CreateWithProtection("Before changes",
            _ => ++calls == 1 ? Disabled() : expected,
            () => { enables++; return 0; });
        Assert.Same(expected, result);
        Assert.False(result.IsSuccess);
        Assert.Equal(2, calls);
        Assert.Equal(1, enables);
    }

    [Theory]
    [InlineData(true, 0, 0, RestorePointOutcome.Failed)]
    [InlineData(false, 1058, 0, RestorePointOutcome.SystemRestoreDisabled)]
    [InlineData(false, 5, 0, RestorePointOutcome.Failed)]
    [InlineData(false, 0, 0, RestorePointOutcome.Failed)]
    [InlineData(true, 0, 42, RestorePointOutcome.Created)]
    public void NativeResult_RequiresSuccessAndARealSequence(
        bool succeeded, uint status, long sequence, RestorePointOutcome expected)
    {
        var result = RestorePointService.ReadCreationResult(succeeded, status, sequence);
        Assert.Equal(expected, result.Outcome);
        if (expected == RestorePointOutcome.Created)
            Assert.Equal(sequence, result.SequenceNumber);
        else
            Assert.Null(result.SequenceNumber);
    }

    [Fact]
    public void NativeStatus_UsesWindowsSdkPackedLayout()
    {
        Assert.Equal(12, Marshal.SizeOf<NativeRestore.STATEMGRSTATUS>());
        Assert.Equal(4, Marshal.OffsetOf<NativeRestore.STATEMGRSTATUS>("llSequenceNumber").ToInt32());
    }

    private static RestorePointResult Disabled() => new()
    {
        Outcome = RestorePointOutcome.SystemRestoreDisabled,
    };
}
