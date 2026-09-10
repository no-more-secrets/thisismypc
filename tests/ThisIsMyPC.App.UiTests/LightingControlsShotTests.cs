using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Hardware;
using ThisIsMyPC.Core.Hardware.Lighting;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Modules.Hardware.Models;

namespace ThisIsMyPC.App.UiTests;

/// <summary>
/// CI-safe: the Lighting tab's device controls over a scripted backend
/// session. Every write the page makes lands in the fake's log; nothing
/// reaches a device.
/// </summary>
public class LightingControlsShotTests
{
    private sealed class FakeSession : ILightingSession
    {
        public List<string> Writes { get; } = [];
        public List<LightingDevice> Devices { get; } = [];
        public bool IsConnected { get; set; } = true;
        public event EventHandler? DeviceListChanged;

        public void RaiseDeviceListChanged() => DeviceListChanged?.Invoke(this, EventArgs.Empty);

        public Task<OperationResult<IReadOnlyList<LightingDevice>>> GetDevicesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<IReadOnlyList<LightingDevice>>.Success(Devices.ToList()));

        public Task<OperationResult<LightingDevice>> GetDeviceAsync(int deviceIndex, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<LightingDevice>.Success(Devices[deviceIndex]));

        public Task<OperationResult<bool>> SetModeAsync(int deviceIndex, LightingMode mode, CancellationToken cancellationToken = default)
        {
            Writes.Add($"mode:{deviceIndex}:{mode.Index}:{mode.Name}:speed={mode.Speed}:brightness={mode.Brightness}:colorMode={mode.ColorMode}:colors={string.Join(",", mode.Colors.Select(c => c.ToHex()))}");
            Devices[deviceIndex] = Devices[deviceIndex] with { ActiveModeIndex = mode.Index, Modes = Devices[deviceIndex].Modes.Select(m => m.Index == mode.Index ? mode : m).ToList() };
            return Task.FromResult(OperationResult<bool>.Success(true));
        }

        public Task<OperationResult<bool>> SetLedsAsync(int deviceIndex, IReadOnlyList<RgbColor> colors, CancellationToken cancellationToken = default)
        {
            Writes.Add($"leds:{deviceIndex}:{colors.Count}x{colors[0].ToHex()}");
            return Task.FromResult(OperationResult<bool>.Success(true));
        }

        public Task<OperationResult<bool>> SetZoneLedsAsync(int deviceIndex, int zoneIndex, IReadOnlyList<RgbColor> colors, CancellationToken cancellationToken = default)
        {
            Writes.Add($"zone:{deviceIndex}:{zoneIndex}:{colors.Count}x{colors[0].ToHex()}");
            return Task.FromResult(OperationResult<bool>.Success(true));
        }

        public Task<OperationResult<bool>> SaveModeAsync(int deviceIndex, LightingMode mode, CancellationToken cancellationToken = default)
        {
            Writes.Add($"save:{deviceIndex}:{mode.Index}");
            return Task.FromResult(OperationResult<bool>.Success(true));
        }

        public void Dispose() => Writes.Add("disposed");
    }

    private sealed class FakeBackend(FakeSession session) : ILightingBackend
    {
        public int Opens { get; private set; }

        public Task<OperationResult<LightingInventory>> DetectAsync(bool rescan = false, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult<LightingInventory>.Success(new LightingInventory(
                session.Devices.Select(d => new LightingDeviceSummary(d.Name, d.Type, "fake", d.Location)).ToList(), [], DateTimeOffset.Now)));

        public Task<OperationResult<ILightingSession>> OpenAsync(CancellationToken cancellationToken = default)
        {
            Opens++;
            return Task.FromResult(OperationResult<ILightingSession>.Success(session));
        }
    }

    private static LightingMode Direct => new()
    {
        Index = 0,
        Name = "Direct",
        Flags = LightingModeFlags.HasPerLedColor | LightingModeFlags.HasBrightness,
        BrightnessMin = 0,
        BrightnessMax = 100,
        Brightness = 80,
        ColorMode = LightingColorMode.PerLed,
    };

    private static LightingMode Breathing => new()
    {
        Index = 1,
        Name = "Breathing",
        Value = 2,
        Flags = LightingModeFlags.HasSpeed | LightingModeFlags.HasModeSpecificColor | LightingModeFlags.HasRandomColor | LightingModeFlags.ManualSave,
        SpeedMin = 1,
        SpeedMax = 5,
        Speed = 3,
        ColorsMin = 1,
        ColorsMax = 2,
        ColorMode = LightingColorMode.ModeSpecific,
        Colors = [new RgbColor(255, 64, 0)],
    };

    private static LightingDevice Motherboard(int activeMode = 0) => new()
    {
        Index = 0,
        Type = LightingDeviceType.Motherboard,
        Name = "ASUS ROG STRIX B550-F",
        Vendor = "ASUS",
        ActiveModeIndex = activeMode,
        Modes = [Direct, Breathing],
        Zones =
        [
            new LightingZone { Index = 0, Name = "Aura Header 1", LedCount = 4 },
            new LightingZone { Index = 1, Name = "Chipset", LedCount = 6 },
        ],
        Leds = Enumerable.Range(0, 10).Select(i => new LightingLed($"LED {i + 1}", (uint)i)).ToList(),
        Colors = Enumerable.Repeat(new RgbColor(255, 0, 0), 4).Concat(Enumerable.Repeat(new RgbColor(0, 0, 255), 6)).ToList(),
    };

    private static LightingDevice Gpu => new()
    {
        Index = 1,
        Type = LightingDeviceType.Gpu,
        Name = "ASUS ROG STRIX GeForce RTX 4080 Gaming",
        Vendor = "ASUS",
        ActiveModeIndex = 0,
        Modes = [Direct with { Flags = LightingModeFlags.HasPerLedColor }],
        Zones = [new LightingZone { Index = 0, Name = "GPU", LedCount = 1 }],
        Leds = [new LightingLed("GPU LED", 0)],
        Colors = [new RgbColor(0, 255, 128)],
    };

    private static readonly LightingDeviceSummary[] FoundDevices =
    [
        new("ASUS ROG STRIX B550-F", LightingDeviceType.Motherboard, "ENE SMBus", "I2C: SMBus, address 0x4E"),
        new("ASUS ROG STRIX GeForce RTX 4080 Gaming", LightingDeviceType.Gpu, "ENE SMBus", "I2C: NVIDIA NvAPI I2C on GPU 0, address 0x67"),
    ];

    /// <param name="available">Detection found devices. Otherwise none were found and the Advanced override shows the (dead) controls.</param>
    private static HardwareTabScanData LightingData(bool available = true)
    {
        var companions = Enum.GetValues<CompanionApp>().Select(CompanionObservation.NotInstalled).ToList();
        var facts = new ObservedHardwareFacts
        {
            Identity = MachineIdentity.From("ASUSTeK COMPUTER INC.", "ROG STRIX B550-F GAMING"),
            FormFactor = new FormFactorEvidence { SmbiosChassisTypes = [3] },
            Companions = companions,
            LightingDevices = available ? FoundDevices : [],
        };
        var report = HardwareCompatibilityPolicy.Decide(facts, new HardwareCompatibilityOptions(ShowAllControls: !available));
        return new HardwareTabScanData(report.For(HardwareDomain.Lighting), report, null, ["fake"], new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero));
    }

    [AvaloniaFact]
    public async Task Lighting_ListsDevices_AndWritesModeBrightnessAndColors()
    {
        var session = new FakeSession();
        session.Devices.AddRange([Motherboard(), Gpu]);
        var backend = new FakeBackend(session);
        using var vm = new HardwareTabViewModel(LightingData(), refreshOnOpen: false, lightingBackend: backend);

        using var s = UiSession.ForView(new HardwareTabView(), vm, "lighting", width: 976, height: 900);
        await s.WaitForAsync(() => vm.Lighting is { HasDevices: true }, what: "device load");
        s.Pump();
        s.Screenshot("devices-dark");
        s.SetTheme(ThemeVariant.Light);
        s.Screenshot("devices-light");
        s.SetTheme(ThemeVariant.Dark);

        Assert.Equal(1, backend.Opens);
        Assert.False(vm.HasAction);
        Assert.True(s.IsTextVisible("Available"));
        Assert.True(s.IsTextVisible("ASUS ROG STRIX B550-F"));
        Assert.True(s.IsTextVisible("ASUS, Motherboard, 2 zones, 10 LEDs"));
        Assert.True(s.IsTextVisible("ASUS ROG STRIX GeForce RTX 4080 Gaming"));
        Assert.True(s.IsTextVisible("ASUS, Graphics card, 1 LED"));
        Assert.True(s.IsTextVisible("All LEDs"));
        Assert.True(s.IsTextVisible("Aura Header 1"));
        Assert.True(s.IsTextVisible("Chipset"));
        Assert.Empty(session.Writes);

        // Brightness slider: one coalesced mode write with the new value.
        var board = vm.Lighting!.Devices[0];
        board.Brightness = 40;
        await s.WaitForAsync(() => session.Writes.Count > 0, what: "brightness write");
        Assert.Contains("mode:0:0:Direct:speed=0:brightness=40:colorMode=PerLed:colors=", session.Writes);

        // A zone color writes that zone only; the All LEDs color writes every LED.
        session.Writes.Clear();
        board.Colors.Single(c => c.Label == "Chipset").Hex = "#00FF00";
        await s.WaitForAsync(() => session.Writes.Count > 0, what: "zone write");
        Assert.Equal(["zone:0:1:6x#00FF00"], session.Writes);
        session.Writes.Clear();
        board.Colors.Single(c => c.Label == "All LEDs").Red = 0;
        board.Colors.Single(c => c.Label == "All LEDs").Blue = 255;
        await s.WaitForAsync(() => session.Writes.Count > 0 && session.Writes[^1].EndsWith("#0000FF", StringComparison.Ordinal), what: "all-LEDs write");
        Assert.Equal("#0000FF", board.Colors.Single(c => c.Label == "Chipset").Hex);

        // Switching to Breathing writes the mode and swaps the rows to speed, random and one mode color.
        session.Writes.Clear();
        board.SelectedMode = board.Modes[1];
        await s.WaitForAsync(() => session.Writes.Count > 0, what: "mode write");
        Assert.StartsWith("mode:0:1:Breathing:speed=3:brightness=0:colorMode=ModeSpecific:colors=#FF4000", session.Writes[0], StringComparison.Ordinal);
        s.Pump();
        s.Screenshot("breathing-dark");
        Assert.True(s.IsTextVisible("Speed"));
        Assert.True(s.IsTextVisible("Use random colors"));
        Assert.True(s.IsTextVisible("Save to device"));
        Assert.DoesNotContain(board.Colors, c => c.Label == "All LEDs");
        Assert.Equal(["Color"], board.Colors.Select(c => c.Label));
        Assert.Equal(3, board.Speed);

        session.Writes.Clear();
        s.ClickText("Save to device");
        await s.WaitForAsync(() => session.Writes.Count > 0, what: "save");
        Assert.Equal(["save:0:1"], session.Writes);
    }

    [AvaloniaFact]
    public async Task Lighting_OverrideShowsControls_ButRefusesEveryWrite()
    {
        var session = new FakeSession();
        session.Devices.Add(Gpu);
        using var vm = new HardwareTabViewModel(LightingData(available: false), refreshOnOpen: false, lightingBackend: new FakeBackend(session));

        using var s = UiSession.ForView(new HardwareTabView(), vm, "lighting", width: 976, height: 700);
        await s.WaitForAsync(() => vm.Lighting is { HasDevices: true }, what: "device load");
        s.Pump();
        s.Screenshot("override-dark");

        Assert.False(vm.Decision.LiveWritesAllowed);
        Assert.False(vm.Lighting!.WritesAllowed);
        Assert.True(s.IsTextVisible("Shown by the Advanced setting. Nothing here can write to hardware on this PC."));
        Assert.False(s.Find<ComboBox>(_ => true).IsEffectivelyEnabled);
        var gpu = vm.Lighting!.Devices[0];
        gpu.Colors[0].Hex = "#123456";
        s.Pump();
        Assert.Empty(session.Writes);
        Assert.Equal("Lighting writes are not permitted on this PC.", gpu.LastError);
    }

    [AvaloniaFact]
    public async Task Lighting_DeviceListChange_Reloads_AndDisposeClosesTheSession()
    {
        var session = new FakeSession();
        session.Devices.Add(Gpu);
        var vm = new HardwareTabViewModel(LightingData(), refreshOnOpen: false, lightingBackend: new FakeBackend(session));
        using var s = UiSession.ForView(new HardwareTabView(), vm, "lighting", width: 976, height: 700);
        await s.WaitForAsync(() => vm.Lighting is { HasDevices: true }, what: "device load");

        session.Devices.Add(Motherboard());
        session.RaiseDeviceListChanged();
        await s.WaitForAsync(() => vm.Lighting!.Devices.Count == 2, what: "reload");
        Assert.True(s.IsTextVisible("ASUS ROG STRIX B550-F"));

        vm.Dispose();
        Assert.Contains("disposed", session.Writes);
    }

    [AvaloniaFact]
    public void Lighting_NoSupportedDevice_IsUnavailable_WithNoButton_AndNoCards()
    {
        var session = new FakeSession();
        using var vm = new HardwareTabViewModel(
            new HardwareTabScanData(
                HardwareCompatibilityPolicy.Decide(new ObservedHardwareFacts
                {
                    Identity = MachineIdentity.From("ASUSTeK COMPUTER INC.", "ROG STRIX B550-F GAMING"),
                    FormFactor = new FormFactorEvidence { SmbiosChassisTypes = [3] },
                    Companions = Enum.GetValues<CompanionApp>().Select(CompanionObservation.NotInstalled).ToList(),
                    LightingDevices = [],
                }).For(HardwareDomain.Lighting),
                HardwareCompatibilityPolicy.Decide(ObservedHardwareFacts.Empty), null,
                ["Lighting controllers: HID collections enumerated: 12.", "Lighting controllers: NVIDIA GPU I2C: nvapi64.dll is not installed (no NVIDIA driver)."],
                new DateTimeOffset(2026, 9, 10, 9, 0, 0, TimeSpan.Zero)),
            refreshOnOpen: false, lightingBackend: new FakeBackend(session));

        using var s = UiSession.ForView(new HardwareTabView(), vm, "lighting", width: 976, height: 676);
        s.Screenshot("no-devices-dark");

        Assert.True(s.IsTextVisible("Not available"));
        Assert.True(s.IsTextVisible("No supported lighting device was found on this PC. Lighting drives the devices it has a built-in controller for."));
        Assert.False(vm.HasAction);
        Assert.Null(s.TryFind<Button>(b => b.Content is string content && (content.StartsWith("Install", StringComparison.Ordinal) || content.StartsWith("Open", StringComparison.Ordinal))));
        Assert.Null(vm.Lighting);

        s.ClickText("Details");
        s.Pump();
        Assert.True(s.IsTextVisible("Lighting controllers: NVIDIA GPU I2C: nvapi64.dll is not installed (no NVIDIA driver)."));
    }
}
