using ThisIsMyPC.Core.Drift;
using ThisIsMyPC.Core.Drift.Eligibility;
using ThisIsMyPC.Service;

namespace ThisIsMyPC.Ipc.Tests;

public sealed class NativeRestorationReadinessTests
{
    [Fact]
    public void PartialEligibilityAllowsOnlyTheSupportedSubset()
    {
        Assert.Null(NativeRestorationService.ReadinessFailure([Evidence(RestorationManagementState.Managed), Evidence(RestorationManagementState.Unmanaged)]));
        Assert.NotNull(NativeRestorationService.ReadinessFailure([Evidence(RestorationManagementState.Managed)]));
        Assert.NotNull(NativeRestorationService.ReadinessFailure([Evidence(RestorationManagementState.Unknown)]));
        Assert.NotNull(NativeRestorationService.ReadinessFailure([]));
        Assert.NotNull(NativeRestorationService.ReadinessFailure([new(null, null)]));
    }

    private static RestorationLoopEvidence Evidence(RestorationManagementState state) =>
        new(new("owner", RestorationProfileState.SupportedAndLoaded), new(new("module", "setting", "key", "value", "owner"), state));
}
