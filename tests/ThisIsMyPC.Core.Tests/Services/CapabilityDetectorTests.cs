using ThisIsMyPC.Core.Modules;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Tests.Fakes;

namespace ThisIsMyPC.Core.Tests.Services;

public sealed class CapabilityDetectorTests
{
    private const string CurrentVersionKeyPath = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    private static CapabilityDetector CreateDetector(string? editionId, out FakeRegistryService registry)
    {
        registry = new FakeRegistryService();
        if (editionId is not null)
            registry.SetString(CurrentVersionKeyPath, "EditionID", editionId);
        return new CapabilityDetector(registry);
    }

    [Fact]
    public void IsOwnerModeAvailable_False_UntilEpic28ShipsTheService()
    {
        var detector = CreateDetector("Professional", out _);

        Assert.False(detector.IsOwnerModeAvailable);
    }

    [Theory]
    [InlineData("Core", WindowsSku.Home)]
    [InlineData("CoreN", WindowsSku.Home)]
    [InlineData("CoreSingleLanguage", WindowsSku.Home)]
    [InlineData("CoreCountrySpecific", WindowsSku.Home)]
    [InlineData("Professional", WindowsSku.Pro)]
    [InlineData("ProfessionalN", WindowsSku.Pro)]
    [InlineData("ProfessionalWorkstation", WindowsSku.Pro)]
    [InlineData("Enterprise", WindowsSku.Enterprise)]
    [InlineData("EnterpriseN", WindowsSku.Enterprise)]
    [InlineData("EnterpriseS", WindowsSku.Enterprise)]
    [InlineData("EnterpriseSN", WindowsSku.Enterprise)]
    [InlineData("IoTEnterprise", WindowsSku.Enterprise)]
    [InlineData("IoTEnterpriseS", WindowsSku.Enterprise)]
    [InlineData("Education", WindowsSku.Education)]
    [InlineData("EducationN", WindowsSku.Education)]
    [InlineData("ProfessionalEducation", WindowsSku.Education)]
    [InlineData("ProfessionalEducationN", WindowsSku.Education)]
    public void Sku_MapsEditionIdToFamily(string editionId, WindowsSku expected)
    {
        var detector = CreateDetector(editionId, out _);
        Assert.Equal(expected, detector.Sku);
    }

    [Theory]
    [InlineData("professional", WindowsSku.Pro)]
    [InlineData("EDUCATION", WindowsSku.Education)]
    [InlineData("cOrE", WindowsSku.Home)]
    public void Sku_MappingIsCaseInsensitive(string editionId, WindowsSku expected)
    {
        var detector = CreateDetector(editionId, out _);
        Assert.Equal(expected, detector.Sku);
    }

    [Theory]
    [InlineData("ServerStandard")]
    [InlineData("Cloud")]
    [InlineData("")]
    public void Sku_IsNullForUnrecognizedEditionId(string editionId)
    {
        var detector = CreateDetector(editionId, out _);
        Assert.Null(detector.Sku);
    }

    [Fact]
    public void Sku_IsNullWhenRegistryReadFails()
    {
        var detector = CreateDetector(editionId: null, out _);
        Assert.Null(detector.Sku);
        Assert.Contains("EditionID read failed", detector.SkuDetectionFailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void SkuDetectionFailureReason_NamesUnrecognizedEditionId()
    {
        var detector = CreateDetector("ServerStandard", out _);
        Assert.Contains("ServerStandard", detector.SkuDetectionFailureReason, StringComparison.Ordinal);
    }

    [Fact]
    public void SkuDetectionFailureReason_IsNullOnSuccess()
    {
        var detector = CreateDetector("Professional", out _);
        Assert.Null(detector.SkuDetectionFailureReason);
    }

    [Fact]
    public void Constructor_SwallowsThrowingRegistryService()
    {
        var detector = new CapabilityDetector(new ThrowingRegistryService());
        Assert.Null(detector.Sku);
        Assert.Contains("threw", detector.SkuDetectionFailureReason, StringComparison.Ordinal);
    }

    private sealed class ThrowingRegistryService : IRegistryService
    {
        public OperationResult<string> ReadString(string keyPath, string valueName)
            => throw new InvalidOperationException("boom");

        public OperationResult<int> ReadDWord(string keyPath, string valueName) => throw new NotSupportedException();
        public OperationResult<string> ReadExpandString(string keyPath, string valueName) => throw new NotSupportedException();
        public OperationResult<string[]> ReadMultiString(string keyPath, string valueName) => throw new NotSupportedException();
        public OperationResult<byte[]> ReadBinary(string keyPath, string valueName) => throw new NotSupportedException();
        public OperationResult<bool> WriteBinary(string keyPath, string valueName, byte[] value) => throw new NotSupportedException();
        public OperationResult<bool> WriteDWord(string keyPath, string valueName, int value) => throw new NotSupportedException();
        public OperationResult<bool> WriteString(string keyPath, string valueName, string value) => throw new NotSupportedException();
        public OperationResult<bool> WriteExpandString(string keyPath, string valueName, string value) => throw new NotSupportedException();
        public OperationResult<bool> WriteMultiString(string keyPath, string valueName, string[] values) => throw new NotSupportedException();
        public OperationResult<bool> DeleteValue(string keyPath, string valueName) => throw new NotSupportedException();
        public OperationResult<bool> DeleteKey(string keyPath, bool recursive = false) => throw new NotSupportedException();
        public OperationResult<bool> KeyExists(string keyPath) => throw new NotSupportedException();
        public OperationResult<bool> ValueExists(string keyPath, string valueName) => throw new NotSupportedException();
        public OperationResult<IReadOnlyList<string>> EnumerateSubKeys(string keyPath) => throw new NotSupportedException();
        public OperationResult<IReadOnlyList<string>> EnumerateValues(string keyPath) => throw new NotSupportedException();
        public OperationResult<string> ReadValueBeforeWrite(string keyPath, string valueName) => throw new NotSupportedException();
    }

    [Fact]
    public void Sku_IsDetectedExactlyOncePerSession()
    {
        var detector = CreateDetector("Professional", out var registry);

        _ = detector.Sku;
        _ = detector.Sku;
        _ = detector.IsSkuRestricted(WindowsSku.Pro);

        Assert.Equal(1, registry.ReadStringCallCount);
    }

    [Fact]
    public void IsSkuRestricted_TrueOnlyWhenCurrentTierIsBelowTheMinimum()
    {
        // SkuRestriction = minimum edition tier honoring the policy
        // (Home < Pro < Enterprise/Education).
        var pro = CreateDetector("Professional", out _);
        Assert.False(pro.IsSkuRestricted(WindowsSku.Home));
        Assert.False(pro.IsSkuRestricted(WindowsSku.Pro));
        Assert.True(pro.IsSkuRestricted(WindowsSku.Enterprise));
        Assert.True(pro.IsSkuRestricted(WindowsSku.Education));

        var home = CreateDetector("Core", out _);
        Assert.True(home.IsSkuRestricted(WindowsSku.Pro));
        Assert.False(home.IsSkuRestricted(WindowsSku.Home));
    }

    [Fact]
    public void EnterpriseAndEducation_AreTheSameTier()
    {
        var enterprise = CreateDetector("Enterprise", out _);
        var education = CreateDetector("Education", out _);

        Assert.False(enterprise.IsSkuRestricted(WindowsSku.Education));
        Assert.False(education.IsSkuRestricted(WindowsSku.Enterprise));
    }

    [Fact]
    public void IsSkuRestricted_FalseWhenNoRestriction()
    {
        var detector = CreateDetector("Professional", out _);
        Assert.False(detector.IsSkuRestricted(null));
    }

    [Fact]
    public void IsSkuRestricted_FalseWhenSkuUnknown()
    {
        var detector = CreateDetector(editionId: null, out _);

        Assert.False(detector.IsSkuRestricted(WindowsSku.Pro));
        Assert.False(detector.IsSkuRestricted(null));
    }

    [Fact]
    public void Registry_CapabilityReportsAvailable()
    {
        var detector = CreateDetector("Professional", out _);

        Assert.True(detector.IsAvailable(SystemCapability.Registry));
        Assert.True(detector.GetAvailability(SystemCapability.Registry).IsAvailable);
    }

    // 5-1: Com/Wmi/NativeApi are now always-available; HwInfo is registry-driven
    // (CapabilityDetectorReportTests); AsusAtkacpi/OpenRgb probe the live file system
    // so their outcome is environment-dependent and not asserted here.
    [Theory]
    [InlineData(SystemCapability.DdcCi)]
    public void UndetectedCapabilities_ReportUnavailableWithReason(SystemCapability capability)
    {
        var detector = CreateDetector("Professional", out _);

        var availability = detector.GetAvailability(capability);
        Assert.False(availability.IsAvailable);
        Assert.False(string.IsNullOrWhiteSpace(availability.Reason));
        Assert.False(detector.IsAvailable(capability));
    }

    [Fact]
    public void Constructor_ThrowsOnNullRegistryService()
    {
        Assert.Throws<ArgumentNullException>(() => new CapabilityDetector(null!));
    }
}
