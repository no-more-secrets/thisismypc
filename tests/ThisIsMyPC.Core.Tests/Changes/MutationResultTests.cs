using ThisIsMyPC.Core.Changes;

namespace ThisIsMyPC.Core.Tests.Changes;

public sealed class MutationResultTests
{
    [Fact]
    public void RequiredRestarts_defaults_to_empty()
    {
        var result = new MutationResult
        {
            IsSuccess = true,
            Applied = [],
            RolledBack = [],
        };

        Assert.Empty(result.RequiredRestarts);
    }

    [Fact]
    public void RequiredRestarts_can_contain_multiple_types()
    {
        var result = new MutationResult
        {
            IsSuccess = true,
            Applied = [],
            RolledBack = [],
            RequiredRestarts = [RestartRequirement.ExplorerRestart, RestartRequirement.ExplorerRefresh],
        };

        Assert.Equal(2, result.RequiredRestarts.Count);
        Assert.Contains(RestartRequirement.ExplorerRestart, result.RequiredRestarts);
        Assert.Contains(RestartRequirement.ExplorerRefresh, result.RequiredRestarts);
    }
}
