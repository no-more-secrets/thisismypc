using ThisIsMyPC.Core.Hardware;
using ThisIsMyPC.Core.Hardware.Detection;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Core.Tests.Fakes;

namespace ThisIsMyPC.Core.Tests.Hardware;

public sealed class CompanionDetectorTests
{
    private const string UninstallHklm = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    private sealed class TaskFake : IScheduledTaskService
    {
        public Dictionary<string, ScheduledTaskInfo> Tasks { get; } = new(StringComparer.OrdinalIgnoreCase);

        public OperationResult<IReadOnlyList<ScheduledTaskInfo>> EnumerateAll() =>
            OperationResult<IReadOnlyList<ScheduledTaskInfo>>.Success(Tasks.Values.ToList());

        public OperationResult<ScheduledTaskInfo> Query(string taskPath) =>
            Tasks.TryGetValue(taskPath, out var task)
                ? OperationResult<ScheduledTaskInfo>.Success(task)
                : OperationResult<ScheduledTaskInfo>.Failure("not found", ErrorCategory.NotFound);

        public OperationResult<bool> SetEnabled(string taskPath, bool enabled) => throw new NotSupportedException();
    }

    private sealed class ServiceFake : IServiceControlService
    {
        public Dictionary<string, ServiceState> Services { get; } = new(StringComparer.OrdinalIgnoreCase);

        public OperationResult<ServiceStatusInfo> Query(string serviceName) =>
            Services.TryGetValue(serviceName, out var state)
                ? OperationResult<ServiceStatusInfo>.Success(new ServiceStatusInfo(serviceName, serviceName, state, ServiceStartType.Automatic))
                : OperationResult<ServiceStatusInfo>.Failure("not found", ErrorCategory.NotFound);

        public OperationResult<IReadOnlyList<ServiceEntryInfo>> EnumerateAll() => throw new NotSupportedException();
        public OperationResult<bool> SetStartType(string serviceName, ServiceStartType startType) => throw new NotSupportedException();
        public Task<OperationResult<bool>> StopAsync(string serviceName, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationResult<bool>> StartAsync(string serviceName, TimeSpan timeout, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static CompanionDetection Of(IReadOnlyList<CompanionDetection> detections, CompanionApp app) =>
        detections.Single(d => d.Observation.App == app);

    [Fact]
    public void EmptyMachine_RecordsNotInstalledForEveryCompanion_NeverLeavesOneOut()
    {
        var detector = new CompanionDetector(new FakeRegistryService(), new FakeHardwareProbeEnvironment());

        var detections = detector.DetectAll(asusPlatformDriverPresent: false, openRgb: null);

        Assert.Equal(Enum.GetValues<CompanionApp>().Order(), detections.Select(d => d.Observation.App).Order());
        Assert.All(detections, d =>
        {
            Assert.False(d.Observation.IsInstalled);
            Assert.False(d.Observation.IsRunning);
            Assert.Empty(d.Observation.ObservedOwnership);
            Assert.Null(d.LaunchPath);
            Assert.Contains(d.Notes, n => n.EndsWith("not found.", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void FanControl_FoundByScheduledTask_ResolvesTheExeAgainstStartIn()
    {
        var env = new FakeHardwareProbeEnvironment();
        env.AddFile(@"C:\Program Files (x86)\FanControl_199_net_4_8\FanControl.exe");
        var tasks = new TaskFake();
        tasks.Tasks[@"\FanControl"] = new ScheduledTaskInfo(
            "FanControl", @"\FanControl", null, null, ["Logon"], null, 0, true,
            Command: "FanControl.exe", WorkingDirectory: @"C:\Program Files (x86)\FanControl_199_net_4_8\");
        var detector = new CompanionDetector(new FakeRegistryService(), env, tasks);

        var fanControl = Of(detector.DetectAll(false, null), CompanionApp.FanControl);

        Assert.True(fanControl.Observation.IsInstalled);
        Assert.False(fanControl.Observation.IsRunning);
        Assert.Equal(@"C:\Program Files (x86)\FanControl_199_net_4_8\FanControl.exe", fanControl.LaunchPath);
        Assert.Contains(fanControl.Notes, n => n.Contains("scheduled task", StringComparison.Ordinal));
    }

    [Fact]
    public void FanControl_RunningWithASavedConfiguration_OwnsCooling()
    {
        var env = new FakeHardwareProbeEnvironment();
        env.AddFile(@"C:\Program Files (x86)\FanControl\FanControl.exe");
        env.AddFile(@"C:\Program Files (x86)\FanControl\Configurations\userConfig.json");
        env.Processes["FanControl"] = @"C:\Program Files (x86)\FanControl\FanControl.exe";
        var detector = new CompanionDetector(new FakeRegistryService(), env);

        var fanControl = Of(detector.DetectAll(false, null), CompanionApp.FanControl);

        Assert.True(fanControl.Observation.IsRunning);
        Assert.Equal([HardwareDomain.Cooling], fanControl.Observation.ObservedOwnership);
    }

    [Fact]
    public void FanControl_RunningWithoutAConfiguration_OwnsNothing()
    {
        var env = new FakeHardwareProbeEnvironment();
        env.AddFile(@"C:\Tools\FanControl\FanControl.exe");
        env.Processes["FanControl"] = @"C:\Tools\FanControl\FanControl.exe";
        var detector = new CompanionDetector(new FakeRegistryService(), env);

        var fanControl = Of(detector.DetectAll(false, null), CompanionApp.FanControl);

        Assert.True(fanControl.Observation.IsRunning);
        Assert.Empty(fanControl.Observation.ObservedOwnership);
        Assert.Equal(@"C:\Tools\FanControl\FanControl.exe", fanControl.LaunchPath);
    }

    [Fact]
    public void RunningProcess_WithUnreadablePath_CountsAsRunning_WithoutALaunchPath()
    {
        var env = new FakeHardwareProbeEnvironment();
        env.Processes["FanControl"] = string.Empty;
        var detector = new CompanionDetector(new FakeRegistryService(), env);

        var fanControl = Of(detector.DetectAll(false, null), CompanionApp.FanControl);

        Assert.True(fanControl.Observation.IsRunning);
        Assert.True(fanControl.Observation.IsInstalled);
        Assert.Null(fanControl.LaunchPath);
        Assert.Contains(fanControl.Notes, n => n.Contains("path not readable", StringComparison.Ordinal));
    }

    [Fact]
    public void OpenRgb_MsiInstall_FoundByUninstallEntryAndProgramFiles()
    {
        var registry = new FakeRegistryService();
        registry.SetString(UninstallHklm + @"\{E62B1ADD-A259-4E92-82BC-9E2C1F0370FB}", "DisplayName", "OpenRGB");
        registry.SetString(UninstallHklm + @"\{OTHER}", "DisplayName", "Something Else");
        var env = new FakeHardwareProbeEnvironment();
        env.AddFile(@"C:\Program Files\OpenRGB\OpenRGB.exe");
        var detector = new CompanionDetector(registry, env);

        var openRgb = Of(detector.DetectAll(false, null), CompanionApp.OpenRgb);

        Assert.True(openRgb.Observation.IsInstalled);
        Assert.False(openRgb.Observation.IsRunning);
        Assert.Equal(@"C:\Program Files\OpenRGB\OpenRGB.exe", openRgb.LaunchPath);
    }

    [Fact]
    public void OpenRgb_DisplayIconWithIndex_IsTrimmedToTheExe()
    {
        var registry = new FakeRegistryService();
        registry.SetString(UninstallHklm + @"\OpenRGB", "DisplayName", "OpenRGB 0.9");
        registry.SetString(UninstallHklm + @"\OpenRGB", "DisplayIcon", @"""D:\Apps\OpenRGB\OpenRGB.exe"",0");
        var env = new FakeHardwareProbeEnvironment();
        env.AddFile(@"D:\Apps\OpenRGB\OpenRGB.exe");
        var detector = new CompanionDetector(registry, env);

        var openRgb = Of(detector.DetectAll(false, null), CompanionApp.OpenRgb);

        Assert.Equal(@"D:\Apps\OpenRGB\OpenRGB.exe", openRgb.LaunchPath);
    }

    [Fact]
    public void OpenRgb_RunningWithDevicesOnTheServer_OwnsLighting()
    {
        var env = new FakeHardwareProbeEnvironment();
        env.Processes["OpenRGB"] = @"C:\Program Files\OpenRGB\OpenRGB.exe";
        env.AddFile(@"C:\Program Files\OpenRGB\OpenRGB.exe");
        var detector = new CompanionDetector(new FakeRegistryService(), env);

        var owning = Of(detector.DetectAll(false, new OpenRgbProbeResult(true, 2, "answered")), CompanionApp.OpenRgb);
        var idle = Of(detector.DetectAll(false, new OpenRgbProbeResult(true, 0, "answered")), CompanionApp.OpenRgb);
        var unreachable = Of(detector.DetectAll(false, OpenRgbProbeResult.Unreachable("refused")), CompanionApp.OpenRgb);

        Assert.Equal([HardwareDomain.Lighting], owning.Observation.ObservedOwnership);
        Assert.Empty(idle.Observation.ObservedOwnership);
        Assert.Empty(unreachable.Observation.ObservedOwnership);
    }

    [Fact]
    public void OpenRgb_SettingsFolderAlone_MeansInstalledButNoLaunchPath()
    {
        var env = new FakeHardwareProbeEnvironment();
        env.Directories.Add(@"C:\Users\tester\AppData\Roaming\OpenRGB");
        var detector = new CompanionDetector(new FakeRegistryService(), env);

        var openRgb = Of(detector.DetectAll(false, null), CompanionApp.OpenRgb);

        Assert.True(openRgb.Observation.IsInstalled);
        Assert.Null(openRgb.LaunchPath);
    }

    [Fact]
    public void GHelper_FoundByRunValue_AndByWingetPortableFolder()
    {
        var registry = new FakeRegistryService();
        registry.SetString(@"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "GHelper", @"""C:\Users\tester\GHelper\GHelper.exe""");
        var env = new FakeHardwareProbeEnvironment();
        env.AddFile(@"C:\Users\tester\GHelper\GHelper.exe");
        var byRun = Of(new CompanionDetector(registry, env).DetectAll(true, null), CompanionApp.GHelper);

        var wingetEnv = new FakeHardwareProbeEnvironment();
        wingetEnv.AddFile(@"C:\Users\tester\AppData\Local\Microsoft\WinGet\Packages\seerge.g-helper_Microsoft.Winget.Source_8wekyb3d8bbwe\GHelper.exe");
        var byWinget = Of(new CompanionDetector(new FakeRegistryService(), wingetEnv).DetectAll(true, null), CompanionApp.GHelper);

        Assert.Equal(@"C:\Users\tester\GHelper\GHelper.exe", byRun.LaunchPath);
        Assert.True(byRun.Observation.IsInstalled);
        Assert.EndsWith(@"8wekyb3d8bbwe\GHelper.exe", byWinget.LaunchPath, StringComparison.Ordinal);
        Assert.Empty(byWinget.Observation.ObservedOwnership);
    }

    [Fact]
    public void ArmouryCrate_LightingServiceRunning_OwnsLighting_PlatformDomainsNeedTheDriver()
    {
        var services = new ServiceFake();
        services.Services["ArmouryCrateService"] = ServiceState.Running;
        services.Services["LightingService"] = ServiceState.Running;
        var registry = new FakeRegistryService();
        var env = new FakeHardwareProbeEnvironment();

        var withDriver = Of(new CompanionDetector(registry, env, services: services).DetectAll(true, null), CompanionApp.ArmouryCrate);
        var withoutDriver = Of(new CompanionDetector(registry, env, services: services).DetectAll(false, null), CompanionApp.ArmouryCrate);

        Assert.True(withDriver.Observation.IsRunning);
        Assert.Equal(
            [HardwareDomain.Lighting, HardwareDomain.SystemControl, HardwareDomain.Cooling],
            withDriver.Observation.ObservedOwnership);
        Assert.Equal([HardwareDomain.Lighting], withoutDriver.Observation.ObservedOwnership);
        Assert.Null(withDriver.LaunchPath);
    }

    [Fact]
    public void ArmouryCrate_ServicesStopped_IsInstalledNotRunning_OwnsNothing()
    {
        var services = new ServiceFake();
        services.Services["ArmouryCrateService"] = ServiceState.Stopped;
        var detector = new CompanionDetector(new FakeRegistryService(), new FakeHardwareProbeEnvironment(), services: services);

        var crate = Of(detector.DetectAll(true, null), CompanionApp.ArmouryCrate);

        Assert.True(crate.Observation.IsInstalled);
        Assert.False(crate.Observation.IsRunning);
        Assert.Empty(crate.Observation.ObservedOwnership);
    }

    [Fact]
    public void HwInfo_FoundBySettingsKey_AndRunningProcess()
    {
        var registry = new FakeRegistryService();
        registry.AddKey(@"HKCU\Software\HWiNFO64");
        var env = new FakeHardwareProbeEnvironment();
        env.Processes["HWiNFO64"] = @"C:\Program Files\HWiNFO64\HWiNFO64.exe";
        env.AddFile(@"C:\Program Files\HWiNFO64\HWiNFO64.exe");
        var detector = new CompanionDetector(registry, env);

        var hwInfo = Of(detector.DetectAll(false, null), CompanionApp.HwInfo);

        Assert.True(hwInfo.Observation.IsInstalled);
        Assert.True(hwInfo.Observation.IsRunning);
        Assert.Equal(@"C:\Program Files\HWiNFO64\HWiNFO64.exe", hwInfo.LaunchPath);
    }

    [Fact]
    public void SignalRgbAndLibreHardwareMonitor_FoundByUninstallEntries()
    {
        var registry = new FakeRegistryService();
        registry.SetString(UninstallHklm + @"\SignalRGB", "DisplayName", "SignalRGB");
        registry.SetString(UninstallHklm + @"\SignalRGB", "InstallLocation", @"C:\Users\tester\AppData\Local\VortxEngine");
        registry.SetString(UninstallHklm + @"\LHM", "DisplayName", "LibreHardwareMonitor");
        var env = new FakeHardwareProbeEnvironment();
        env.AddFile(@"C:\Users\tester\AppData\Local\VortxEngine\SignalRgbLauncher.exe");
        var detector = new CompanionDetector(registry, env);

        var detections = detector.DetectAll(false, null);

        Assert.True(Of(detections, CompanionApp.SignalRgb).Observation.IsInstalled);
        Assert.Equal(@"C:\Users\tester\AppData\Local\VortxEngine\SignalRgbLauncher.exe", Of(detections, CompanionApp.SignalRgb).LaunchPath);
        Assert.True(Of(detections, CompanionApp.LibreHardwareMonitor).Observation.IsInstalled);
        Assert.Null(Of(detections, CompanionApp.LibreHardwareMonitor).LaunchPath);
    }
}
