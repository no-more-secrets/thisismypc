using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.App.UiTests.Fakes;

/// <summary>Never touches System Restore; reports instant success.</summary>
public sealed class UiFakeRestorePointService : IRestorePointService
{
    public List<string> Requests { get; } = [];

    public Task<RestorePointResult> CreateRestorePointAsync(string description)
    {
        Requests.Add(description);
        return Task.FromResult(new RestorePointResult { Outcome = RestorePointOutcome.Created });
    }
}
