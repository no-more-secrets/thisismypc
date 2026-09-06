using ThisIsMyPC.Core.Hardware;

namespace ThisIsMyPC.Core.Tests.Hardware;

public sealed class FormFactorClassifierTests
{
    [Fact]
    public void NoEvidence_IsUnknown()
    {
        var decision = FormFactorClassifier.Classify(FormFactorEvidence.None);

        Assert.Equal(MachineFormFactor.Unknown, decision.FormFactor);
        Assert.Contains(decision.Reasons, r => r.Contains("No form-factor evidence", StringComparison.Ordinal));
    }

    [Fact]
    public void NullEvidence_IsUnknown()
    {
        Assert.Equal(MachineFormFactor.Unknown, FormFactorClassifier.Classify(null).FormFactor);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(14)]
    [InlineData(30)]
    [InlineData(31)]
    [InlineData(32)]
    public void PortableChassis_IsLaptop(int chassisType)
    {
        var decision = FormFactorClassifier.Classify(new FormFactorEvidence { SmbiosChassisTypes = [chassisType] });

        Assert.Equal(MachineFormFactor.Laptop, decision.FormFactor);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(13)]
    [InlineData(35)]
    public void StationaryChassis_IsDesktop(int chassisType)
    {
        var decision = FormFactorClassifier.Classify(new FormFactorEvidence { SmbiosChassisTypes = [chassisType] });

        Assert.Equal(MachineFormFactor.Desktop, decision.FormFactor);
    }

    [Theory]
    [InlineData(1)]  // Other
    [InlineData(2)]  // Unknown
    [InlineData(12)] // Docking station
    public void UnclassifiedChassis_StaysUnknown_EvenWithRole(int chassisType)
    {
        var unknown = FormFactorClassifier.Classify(new FormFactorEvidence { SmbiosChassisTypes = [chassisType] });
        Assert.Equal(MachineFormFactor.Unknown, unknown.FormFactor);

        var withRole = FormFactorClassifier.Classify(new FormFactorEvidence
        {
            SmbiosChassisTypes = [chassisType],
            PlatformRole = PlatformRole.Mobile,
        });
        Assert.Equal(MachineFormFactor.Unknown, withRole.FormFactor);
    }

    [Fact]
    public void ChassisNamingBothClasses_IsUnknown()
    {
        var decision = FormFactorClassifier.Classify(new FormFactorEvidence { SmbiosChassisTypes = [3, 10] });

        Assert.Equal(MachineFormFactor.Unknown, decision.FormFactor);
        Assert.Contains(decision.Reasons, r => r.Contains("both portable and stationary", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(PlatformRole.Mobile)]
    [InlineData(PlatformRole.Desktop)]
    public void ContradictoryChassis_StaysUnknown_EvenWithARole(PlatformRole role)
    {
        // Regression: the role used to break the tie for [3, 10].
        var decision = FormFactorClassifier.Classify(new FormFactorEvidence
        {
            SmbiosChassisTypes = [3, 10],
            PlatformRole = role,
            HasSystemBattery = true,
            HasInternalDisplayPanel = true,
        });

        Assert.Equal(MachineFormFactor.Unknown, decision.FormFactor);
        Assert.Contains(decision.Reasons, r => r.Contains("regardless of platform role", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(PlatformRole.Mobile)]
    [InlineData(PlatformRole.Slate)]
    [InlineData(PlatformRole.Desktop)]
    [InlineData(PlatformRole.Workstation)]
    [InlineData(PlatformRole.SohoServer)]
    [InlineData(PlatformRole.Unspecified)]
    public void PlatformRoleAlone_NeverClassifies(PlatformRole role)
    {
        // PowerDeterminePlatformRoleEx infers Mobile from battery presence when
        // the ACPI FADT has no profile, so an API-only role is corroborating.
        var decision = FormFactorClassifier.Classify(new FormFactorEvidence { PlatformRole = role });

        Assert.Equal(MachineFormFactor.Unknown, decision.FormFactor);
    }

    [Fact]
    public void MobileRoleWithBattery_NoChassis_StaysUnknown_BatteryFallbackRegression()
    {
        // The exact shape the API produces on a battery-backed machine with no FADT
        // profile: Mobile role plus battery. Neither is firmware provenance.
        var decision = FormFactorClassifier.Classify(new FormFactorEvidence
        {
            PlatformRole = PlatformRole.Mobile,
            HasSystemBattery = true,
            HasInternalDisplayPanel = true,
        });

        Assert.Equal(MachineFormFactor.Unknown, decision.FormFactor);
        Assert.Contains(decision.Reasons, r => r.Contains("inferred from battery presence", StringComparison.Ordinal));
    }

    [Fact]
    public void ChassisWithAgreeingRole_RecordsRoleAsCorroborating()
    {
        var decision = FormFactorClassifier.Classify(new FormFactorEvidence
        {
            SmbiosChassisTypes = [10],
            PlatformRole = PlatformRole.Mobile,
        });

        Assert.Equal(MachineFormFactor.Laptop, decision.FormFactor);
        Assert.Contains(decision.Reasons, r => r.Contains("agrees with the chassis type", StringComparison.Ordinal));
    }

    [Fact]
    public void ChassisWinsOverDisagreeingRole_AndSaysSo()
    {
        var decision = FormFactorClassifier.Classify(new FormFactorEvidence
        {
            SmbiosChassisTypes = [10],
            PlatformRole = PlatformRole.Desktop,
        });

        Assert.Equal(MachineFormFactor.Laptop, decision.FormFactor);
        Assert.Contains(decision.Reasons, r => r.Contains("chassis type wins", StringComparison.Ordinal));
    }

    [Fact]
    public void BatteryAlone_IsNotALaptop()
    {
        var decision = FormFactorClassifier.Classify(new FormFactorEvidence { HasSystemBattery = true });

        Assert.Equal(MachineFormFactor.Unknown, decision.FormFactor);
        Assert.Contains(decision.Reasons, r => r.Contains("not treated as proof", StringComparison.Ordinal));
    }

    [Fact]
    public void BatteryAndPanelWithoutChassisOrRole_StillUnknown()
    {
        var decision = FormFactorClassifier.Classify(new FormFactorEvidence
        {
            HasSystemBattery = true,
            HasInternalDisplayPanel = true,
            PlatformRole = PlatformRole.Unspecified,
        });

        Assert.Equal(MachineFormFactor.Unknown, decision.FormFactor);
    }

    [Fact]
    public void BatteryOnDesktopChassis_StaysDesktop()
    {
        // UPS-backed or firmware-quirk desktops report a battery.
        var decision = FormFactorClassifier.Classify(new FormFactorEvidence
        {
            SmbiosChassisTypes = [3],
            HasSystemBattery = true,
        });

        Assert.Equal(MachineFormFactor.Desktop, decision.FormFactor);
        Assert.Contains(decision.Reasons, r => r.Contains("corroborating only", StringComparison.Ordinal));
    }

    [Fact]
    public void Classify_IsDeterministic()
    {
        var evidence = new FormFactorEvidence
        {
            SmbiosChassisTypes = [10],
            PlatformRole = PlatformRole.Mobile,
            HasSystemBattery = true,
            HasInternalDisplayPanel = true,
        };

        var first = FormFactorClassifier.Classify(evidence);
        var second = FormFactorClassifier.Classify(evidence);

        Assert.Equal(first.FormFactor, second.FormFactor);
        Assert.Equal(first.Reasons, second.Reasons);
    }
}
