using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Hardware;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Hardware.Cooling;
using ThisIsMyPC.Modules.Hardware.Models;

namespace ThisIsMyPC.App.UiTests;

public sealed class CoolingProfilesShotTests
{
    private const string Profile = """
        {"__VERSION__":"226","Main":{"Controls":[{"Identifier":"fan/1","Name":"Fan 1","NickName":"Case fans","Enable":true,"ManualControl":false,"ManualControlValue":40,"Calibration":{"20":600},"SelectedFanCurve":{"Name":"Quiet","CommandMode":0,"Percent":40}}],"FanCurves":[{"Name":"Quiet","CommandMode":0,"Percent":40},{"Name":"CPU curve","CommandMode":0,"MinimumTemperature":20,"MaximumTemperature":100,"MaximumCommand":100,"Points":["20,30","60,60","100,100"],"SelectedTempSource":{"Identifier":"cpu/temp","Name":"CPU package"}}]},"Sensors":{"Unknown":123}}
        """;

    private sealed class Fixture : IDisposable, IHardwareFactsProvider
    {
        public readonly string Root = Path.Combine(Path.GetTempPath(), "tipc-cooling-ui-" + Guid.NewGuid().ToString("N"));
        public readonly PendingChangesService Pending = new();
        public FanControlProfileStore Store { get; }
        public Fixture()
        {
            Directory.CreateDirectory(Path.Combine(Root, "Configurations"));
            File.WriteAllText(Path.Combine(Root, "Configurations", "Quiet.json"), Profile);
            File.WriteAllText(Path.Combine(Root, "Configurations", "Second.json"), Profile);
            Store = new(this);
        }
        public HardwareDetectionSnapshot? Current => new(ObservedHardwareFacts.Empty,
            new HardwareSnapshot { Facts = ObservedHardwareFacts.Empty },
            new Dictionary<CompanionApp, string> { [CompanionApp.FanControl] = Path.Combine(Root, "FanControl.exe") }, [], DateTimeOffset.Now);
        public event EventHandler? Changed { add { } remove { } }
        public Task<HardwareDetectionSnapshot> GetAsync(bool refresh = false, CancellationToken cancellationToken = default) => Task.FromResult(Current!);
        public void Dispose() => Directory.Delete(Root, true);
    }

    [AvaloniaFact]
    public async Task ProfileEdit_StagesCopy_AndKeepsDrafts()
    {
        using var fixture = new Fixture();
        using var vm = new CoolingProfilesViewModel(fixture.Store, fixture.Pending, loadOnOpen: false);
        using var session = UiSession.ForView(new ScrollViewer { Content = new CoolingProfilesView { DataContext = vm } }, vm,
            "cooling-profiles", width: 900, height: 676);
        session.ClickText("Reload profiles");
        await session.WaitForAsync(() => vm.HasProfile && !vm.IsBusy);
        session.Screenshot("fans-dark");
        var nickname = session.Find<TextBox>(box => box.Watermark is "Fan 1");
        session.Click(nickname);
        nickname.SelectAll();
        session.Window.KeyTextInput("Edited case fans");
        session.Pump();
        Assert.Equal("Edited case fans", vm.SelectedFan!.Nickname);
        vm.SelectedProfile = "Second.json";
        await session.WaitForAsync(() => vm.HasProfile && !vm.IsBusy);
        vm.SelectedProfile = "Quiet.json";
        await session.WaitForAsync(() => vm.HasProfile && !vm.IsBusy);
        Assert.Equal("Edited case fans", vm.SelectedFan!.Nickname);
        session.Find<Button>(button => button.Content is "Stage profile save").BringIntoView();
        session.Pump();
        session.ClickText("Stage profile save");
        await session.WaitForAsync(() => fixture.Pending.PendingCount == 1);
        var change = Assert.Single(Assert.Single(fixture.Pending.PendingGroups).Changes);
        Assert.Contains("Edited case fans", System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(change.AfterValue!)));
        Assert.False(File.Exists(Path.Combine(fixture.Root, "Configurations", vm.SaveName)));
        session.Screenshot("staged-dark");
        session.SetTheme(ThemeVariant.Light);
        session.Screenshot("staged-light");
    }

    [AvaloniaFact]
    public async Task GraphPoints_AreEditable_AndFlatSpeedBinds()
    {
        using var fixture = new Fixture();
        using var vm = new CoolingProfilesViewModel(fixture.Store, fixture.Pending, loadOnOpen: false);
        using var session = UiSession.ForView(new ScrollViewer { Content = new CoolingProfilesView { DataContext = vm } }, vm,
            "cooling-profiles", width: 900, height: 676);
        session.ClickText("Reload profiles");
        await session.WaitForAsync(() => vm.HasProfile && !vm.IsBusy);
        session.ClickText("Fans");
        session.ClickText("Curves");
        var speed = session.Find<NumericUpDown>(control => control.IsEffectivelyVisible);
        speed.Value = 45;
        session.Pump();
        Assert.Equal(45, vm.SelectedCurve!.Percent);
        session.Screenshot("flat-dark");
        session.Find<ComboBox>(box => ReferenceEquals(box.ItemsSource, vm.Curves)).SelectedItem = vm.Curves.Single(curve => curve.IsGraph);
        session.Pump();
        session.ClickText("Add point");
        Assert.Equal(4, vm.SelectedCurve.Points.Count);
        session.Screenshot("graph-dark");
        session.SetTheme(ThemeVariant.Light);
        session.Screenshot("graph-light");
        session.Find<Button>(button => button.Content is "Stage profile save").BringIntoView();
        session.Pump();
        session.ClickText("Stage profile save");
        await session.WaitForAsync(() => !vm.IsBusy);
        Assert.False(vm.Failed, vm.Message);
        Assert.Single(fixture.Pending.PendingGroups);
    }
    [AvaloniaFact]
    [Trait("Category", "Diagnostic")]
    public async Task CoolingPage_UsesMainWindowGeometry()
    {
        using var fixture = new Fixture();
        using var session = UiSession.ForMainWindow("cooling-profiles-host");
        var main = (MainWindowViewModel)session.Window.DataContext!;
        await session.WaitForAsync(() => main.SidebarGroups.Count > 0);
        var facts = new ObservedHardwareFacts
        {
            Identity = MachineIdentity.From("ASUS", "Desktop"),
            FormFactor = new FormFactorEvidence { SmbiosChassisTypes = [3], PlatformRole = PlatformRole.Desktop, HasSystemBattery = false },
            Companions = [CompanionObservation.Running(CompanionApp.FanControl, HardwareDomain.Cooling)]
        };
        var report = HardwareCompatibilityPolicy.Decide(facts);
        using var editor = new CoolingProfilesViewModel(fixture.Store, fixture.Pending);
        using var hardware = new HardwareTabViewModel(new HardwareTabScanData(report.For(HardwareDomain.Cooling), report, null, [], DateTimeOffset.Now), cooling: editor);
        main.CurrentContent = hardware;
        main.ContentTitle = "Cooling";
        await session.WaitForAsync(() => editor.HasProfile && !editor.IsBusy);
        session.Screenshot("cooling-dark");
        session.SetTheme(ThemeVariant.Light);
        session.Screenshot("cooling-light");
        session.SetTheme(ThemeVariant.Dark);
        foreach (var domain in new[] { HardwareDomain.SystemControl, HardwareDomain.Lighting, HardwareDomain.Monitoring })
        {
            using var other = new HardwareTabViewModel(new HardwareTabScanData(report.For(domain), report, null, [], DateTimeOffset.Now));
            main.CurrentContent = other;
            main.ContentTitle = domain.ToString();
            session.Pump();
            session.Screenshot(domain.ToString());
        }
    }}
