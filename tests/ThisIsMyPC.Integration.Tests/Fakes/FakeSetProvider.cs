using ThisIsMyPC.Core.Sets;

namespace ThisIsMyPC.Integration.Tests.Fakes;

internal sealed class FakeSetProvider : ISetProvider
{
    public SetLoadResult Result { get; set; } = new() { Sets = [], Warnings = [] };

    public int LoadCount { get; private set; }

    public SetLoadResult LoadSets()
    {
        LoadCount++;
        return Result;
    }
}
