using System.Text;
using ThisIsMyPC.Core.Drift.Consent;

namespace ThisIsMyPC.Core.Tests.Drift.Consent;

public sealed class MachineConsentDocumentTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Explicit_consent_round_trips(bool enabled)
    {
        var result = MachineConsentDocument.Parse(MachineConsentDocument.Encode(enabled, Now), Now);
        Assert.Equal(MachineConsentStatus.Loaded, result.Status);
        Assert.Equal(enabled, result.IsGranted);
        Assert.Equal(Now, result.ChangedAtUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"enabled\":true}")]
    [InlineData("{\"version\":1,\"enabled\":true,\"enabled\":false,\"changedAtUtc\":\"2026-09-06T12:00:00Z\"}")]
    [InlineData("{\"version\":1,\"enabled\":\"true\",\"changedAtUtc\":\"2026-09-06T12:00:00Z\"}")]
    [InlineData("{\"version\":\"1\",\"enabled\":true,\"changedAtUtc\":\"2026-09-06T12:00:00Z\"}")]
    [InlineData("{\"version\":1,\"enabled\":true,\"changedAtUtc\":\"2026-09-07T12:00:00Z\"}")]
    [InlineData("{\"version\":1,\"enabled\":true,\"changedAtUtc\":\"2026-09-06T11:00:00+01:00\"}")]
    [InlineData("{\"version\":1,\"enabled\":true,\"changedAtUtc\":\"2026-09-06T12:00:00Z\",\"extra\":1}")]
    public void Malformed_or_ambiguous_documents_are_off(string json)
    {
        var result = MachineConsentDocument.Parse(Encoding.UTF8.GetBytes(json), Now);
        Assert.Equal(MachineConsentStatus.Corrupt, result.Status);
        Assert.False(result.IsGranted);
    }

    [Fact]
    public void Unsupported_and_oversized_documents_are_off()
    {
        var unsupported = MachineConsentDocument.Parse(Encoding.UTF8.GetBytes(
            "{\"version\":2,\"enabled\":true,\"changedAtUtc\":\"2026-09-06T12:00:00Z\"}"), Now);
        Assert.Equal(MachineConsentStatus.Unsupported, unsupported.Status);
        Assert.False(unsupported.IsGranted);
        Assert.False(MachineConsentDocument.Parse(new byte[MachineConsentDocument.MaximumBytes + 1], Now).IsGranted);
    }
}