using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Integration.Tests.Fakes;

public sealed class FakeRestorePointService : IRestorePointService
{
    public List<string> Descriptions { get; } = [];
    public int CallCount => Descriptions.Count;
    public RestorePointResult NextResult { get; set; } = new()
    {
        Outcome = RestorePointOutcome.Created,
        SequenceNumber = 1,
    };

    public Task<RestorePointResult> CreateRestorePointAsync(string description)
    {
        Descriptions.Add(description);
        return Task.FromResult(NextResult);
    }
}
