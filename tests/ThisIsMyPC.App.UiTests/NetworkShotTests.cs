using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ThisIsMyPC.App.ViewModels;
using ThisIsMyPC.App.Views;
using ThisIsMyPC.Core.Network;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Network;

namespace ThisIsMyPC.App.UiTests;

public sealed class NetworkShotTests
{
    [AvaloniaFact]
    public void ExpandedAdapterStatesKeepHeaderGeometry()
    {
        using var vm = new NetworkViewModel(Data(), new PendingChangesService());
        using var session = UiSession.ForView(new NetworkView(), vm, "network-expanded", 1000, 800);
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        {
            session.SetTheme(theme);
            var toggle = session.Find<ToggleSwitch>(t => t.IsEffectivelyVisible && t.IsEnabled);
            var top = toggle.TranslatePoint(default, session.Window)!.Value.Y;
            session.ClickText("Ethernet");
            Assert.Equal(top, toggle.TranslatePoint(default, session.Window)!.Value.Y, 0.5);
            var headerButton = session.Find<Avalonia.Controls.Primitives.ToggleButton>(b => b.Name == "ExpanderHeader" && b.IsEffectivelyVisible);
            headerButton.Focus(Avalonia.Input.NavigationMethod.Tab);
            session.Window.KeyPress(Avalonia.Input.Key.Space, Avalonia.Input.RawInputModifiers.None);
            session.Window.KeyRelease(Avalonia.Input.Key.Space, Avalonia.Input.RawInputModifiers.None);
            session.Pump();
            Assert.False(session.Find<Expander>(e => e.IsEffectivelyVisible).IsExpanded);
            session.Window.KeyPress(Avalonia.Input.Key.Tab, Avalonia.Input.RawInputModifiers.None);
            session.Window.KeyRelease(Avalonia.Input.Key.Tab, Avalonia.Input.RawInputModifiers.None);
            session.Pump();
            Assert.True(toggle.IsFocused);
            session.Screenshot($"keyboard-focus-{theme.Key}");
            session.ClickText("Ethernet");
            var content = session.Find<Border>(b => b.Name == "ExpanderContent" && b.IsEffectivelyVisible);
            Assert.Equal(new Thickness(0), content.BorderThickness);
            session.Click(content);
            var row = session.Find<ListBoxItem>(r => r.IsEffectivelyVisible);
            Assert.True(row.IsSelected);
            var presenter = row.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().First();
            Assert.Equal(Avalonia.Media.Colors.Transparent, Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(presenter.Background).Color);
            session.Screenshot($"expanded-selected-{theme.Key}");
            session.HoverText("Ethernet");
            var header = session.Find<Border>(b => b.Name == "ToggleButtonBackground" && b.IsEffectivelyVisible);
            Assert.Equal(Avalonia.Media.Colors.Transparent, Assert.IsAssignableFrom<Avalonia.Media.ISolidColorBrush>(header.Background).Color);
            session.Screenshot($"expanded-header-hover-{theme.Key}");
            session.HoverText("Connections");
            session.Screenshot($"expanded-idle-{theme.Key}");
            session.Click(toggle);
            session.Screenshot($"expanded-staged-{theme.Key}");
            session.Click(toggle);
            session.ClickText("Ethernet");
            Assert.Equal(top, toggle.TranslatePoint(default, session.Window)!.Value.Y, 0.5);
        }
    }
    [AvaloniaFact, Trait("Category", "Diagnostic")]
    public async Task FullWindowReadsNetworkWithoutApplying()
    {
        using var session = UiSession.ForMainWindow("network-window");
        var main = (MainWindowViewModel)session.Window.DataContext!;
        await session.WaitForAsync(() => main.SidebarGroups.Count > 0, timeoutMs: 30000, what: "sidebar");
        session.OpenModule(NetworkChanges.ModuleName);
        await session.WaitForAsync(() => main.CurrentContent is NetworkViewModel, timeoutMs: 30000, what: "network scan");
        foreach (var tab in new[] { "Connections", "DNS", "Firewall" })
        {
            session.ClickText(tab); session.Pump(); session.Screenshot(tab);
        }
        session.ClickText("Firewall profiles"); session.Pump(); session.Screenshot("profiles");
    }

    private static NetworkScanData Data() => new([
        new(Guid.Parse("c7d51f0e-d423-430b-82d2-117bf8dd2fa5"), "Ethernet", "Example Ethernet adapter", "Ethernet", "Up", "192.0.2.10", "192.0.2.1", "192.0.2.1", "001122334455", 1000000000, true, NetworkChanges.Automatic, null),
        new(Guid.Parse("ad6a876a-c0df-44dd-adf0-e8fbba63d321"), "Wi-Fi", "Example wireless adapter", "Wireless80211", "Down", "", "", "", "112233445566", 0, false, null, "DNS settings could not be read."),
    ], new([
        new(1, "Domain", true, false, "Block", "Allow", false),
        new(2, "Private", true, true, "Block", "Allow", true),
        new(4, "Public", true, false, "Block", "Allow", true),
    ], Enumerable.Range(1, 100).Select(i => new FirewallRuleState($"Example application {i}", @"C:\Program Files\Example\app.exe", "", true, "Inbound", "Allow", "TCP", "443", "*", "*", "LocalSubnet", "Private")).ToArray()));

    [AvaloniaFact]
    public void TabsRenderInBothThemesAndNarrowWidth()
    {
        using var vm = new NetworkViewModel(Data(), new PendingChangesService());
        var card = new Border { Margin = new Thickness(16), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = new NetworkView() };
        card.Bind(Border.BackgroundProperty, card.GetResourceObservable("RaisedBrush"));
        card.Bind(Border.BorderBrushProperty, card.GetResourceObservable("OutlineBrush"));
        using var session = UiSession.ForView(card, vm, "network", 1000, 800);
        foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
        foreach (var width in new[] { 1000, 760 })
        {
            session.SetTheme(theme); session.Window.Width = width;
            foreach (var tab in new[] { "Connections", "DNS", "Firewall" })
            {
                session.ClickText(tab); session.Pump(); session.Screenshot($"{tab}-{theme.Key}-{width}");
                if (tab == "Firewall")
                {
                    session.ClickText("Firewall profiles"); session.Pump();
                    session.Screenshot($"profiles-{theme.Key}-{width}");
                    session.ClickText("Firewall profiles");
                }
                var strip = session.Find<Border>(b => b.Name == "PART_Strip" && b.IsEffectivelyVisible);
                Assert.Equal(card.Bounds.Width - 2, strip.Bounds.Width, 0.5);
                Assert.Equal(1, strip.TranslatePoint(default, card)!.Value.Y, 0.5);
            }
        }
    }

    [AvaloniaFact]
    public void DnsStagesOnlyOnSaveAndDiscardRestoresValue()
    {
        var pending = new PendingChangesService();
        using var vm = new NetworkViewModel(Data(), pending);
        using var session = UiSession.ForView(new NetworkView(), vm, "network-actions");
        session.ClickText("DNS");
        session.ClickText("Edit DNS");
        var box = session.Find<TextBox>(t => t.IsEffectivelyVisible && t.IsEnabled && t.Watermark == "Automatic");
        session.Type(box, "1.1.1.1 8.8.8.8");
        Assert.Equal(0, pending.PendingCount);
        session.ClickText("Stage DNS");
        Assert.Equal(1, pending.PendingCount);
        Assert.Equal(NetworkChanges.Automatic, pending.PendingGroups[0].Changes[0].BeforeValue);
        Assert.Equal("1.1.1.1,8.8.8.8", pending.PendingGroups[0].Changes[0].AfterValue);
        session.Screenshot("dns-staged");
        session.ClickText("Revert");
        Assert.Equal(0, pending.PendingCount);
        Assert.Equal("", vm.Adapters[0].Dns.DnsText);
        vm.Adapters[0].Dns.DnsText = "not-an-address";
        vm.Adapters[0].Dns.SaveDnsCommand.Execute(null);
        Assert.True(vm.Adapters[0].Dns.IsInvalid);
        Assert.Equal(0, pending.PendingCount);
    }

    [AvaloniaFact]
    public async Task SwitchBackCancelsAndAppliedStateBecomesNewBaseline()
    {
        var pending = new PendingChangesService();
        using var vm = new NetworkViewModel(Data(), pending);
        using var session = UiSession.ForView(new NetworkView(), vm, "network-toggle");
        session.Click(session.Find<ToggleSwitch>(t => t.IsEffectivelyVisible && t.IsEnabled));
        Assert.Equal(1, pending.PendingCount);
        Assert.True(vm.Adapters[0].Connection.CanEdit);
        session.Screenshot("staged");
        session.Click(session.Find<ToggleSwitch>(t => t.IsEffectivelyVisible && t.IsEnabled));
        Assert.Equal(0, pending.PendingCount);
        Assert.True(vm.Adapters[0].Connection.Enabled);
        session.Click(session.Find<ToggleSwitch>(t => t.IsEffectivelyVisible && t.IsEnabled));
        await pending.ApplyAllAsync(_ => Task.FromResult(ThisIsMyPC.Core.Results.OperationResult<bool>.Success(true)),
            _ => Task.FromResult(ThisIsMyPC.Core.Results.OperationResult<bool>.Success(true)));
        session.Pump();
        Assert.False(vm.Adapters[0].Connection.Enabled);
        session.Click(session.Find<ToggleSwitch>(t => t.IsEffectivelyVisible && t.IsEnabled));
        Assert.Equal("false", pending.PendingGroups.Single().Changes.Single().BeforeValue);
        session.Click(session.Find<ToggleSwitch>(t => t.IsEffectivelyVisible && t.IsEnabled));
        Assert.Equal(0, pending.PendingCount);
        vm.Profiles[1].Enabled = false;
        Assert.Equal(1, pending.PendingCount);
        vm.Profiles[1].Enabled = true;
        Assert.Equal(0, pending.PendingCount);
    }

    [AvaloniaFact]
    public void LargeInventoryGroupsWithoutLosingRulesAndSearchCrossesFilters()
    {
        var data = Data();
        var rules = Enumerable.Range(0, 1100).Select(i => data.Firewall.Rules[0] with
        {
            Name = $"Rule {i}", Application = i < 1000 ? @"C:\Apps\Example\app.exe" : @"%SystemRoot%\System32\system.exe",
            LocalPorts = i == 1099 ? "54321" : "443",
        }).ToArray();
        using var vm = new NetworkViewModel(data with { Firewall = data.Firewall with { Rules = rules } }, new PendingChangesService());
        Assert.Single(vm.VisibleAdapters);
        vm.AdapterSearch = "Wi-Fi";
        Assert.Equal("Wi-Fi", Assert.Single(vm.VisibleAdapters).State.Name);
        vm.AdapterSearch = "";
        vm.AdapterScope = 4;
        Assert.Equal(2, vm.VisibleAdapters.Count);
        Assert.Single(vm.RuleGroups);
        Assert.Equal(1000, vm.Rules.Count);
        vm.RuleCategory = 1;
        Assert.Equal(100, vm.Rules.Count);
        vm.RuleCategory = 0;
        vm.RuleSearch = "54321";
        Assert.Equal("Rule 1099", Assert.Single(vm.Rules).Name);
        Assert.True(Assert.Single(vm.RuleGroups).Expanded);
        Assert.False(vm.IsBrowsingRules);
        vm.RuleSearch = "Rule";
        Assert.Equal(1100, vm.RuleGroups.Sum(g => g.Rules.Count));
        vm.RuleSearch = "";
        vm.SelectedTabIndex = 2;
        using var session = UiSession.ForView(new NetworkView(), vm, "network-grouped", 1000, 800);
        session.ClickText("app.exe · 1000 rules");
        session.Screenshot("large-group");
        session.Type(session.Find<TextBox>(t => t.IsEffectivelyVisible && t.Watermark!.StartsWith("Search all rules", StringComparison.Ordinal)), "54321");
        Assert.Equal("Rule 1099", Assert.Single(vm.Rules).Name);
        session.ClickText("Rule 1099");
        session.Screenshot("cross-category-search");
    }

    [AvaloniaFact]
    public void OtherQueueChangesPreserveDnsDraftsAndAppliedRowsUpdate()
    {
        var pending = new PendingChangesService();
        using var vm = new NetworkViewModel(Data(), pending);
        var dns = vm.Adapters[0].Dns;
        dns.DnsText = "9.9.9.9";
        vm.Adapters[1].Connection.Enabled = true;
        Assert.Equal("9.9.9.9", dns.DnsText);
        pending.DiscardAll();
        Assert.Equal("9.9.9.9", dns.DnsText);
        Assert.False(vm.Adapters[1].Connection.Enabled);
        dns.SaveDnsCommand.Execute(null);
        dns.DnsText = "1.1.1.1";
        vm.Adapters[1].Connection.Enabled = true;
        Assert.Equal("1.1.1.1", dns.DnsText);
        dns.SaveDnsCommand.Execute(null);
        Assert.Equal("1.1.1.1", pending.PendingGroups.Single(g => g.GroupId == dns.GroupId).Changes.Single().AfterValue);
        Assert.Equal(NetworkChanges.Automatic, pending.PendingGroups.Single(g => g.GroupId == dns.GroupId).Changes.Single().BeforeValue);
    }

    [AvaloniaFact]
    public void FilteringAndUnavailableStatesRemainExplicit()
    {
        using var vm = new NetworkViewModel(Data(), new PendingChangesService());
        vm.RuleSearch = "no matching rule";
        Assert.True(vm.NoRules);
        Assert.False(vm.Adapters[1].Dns.CanEdit);
        vm.NavigateToSearchResult("firewall", "Windows Firewall");
        Assert.Equal(2, vm.SelectedTabIndex);
        using var empty = new NetworkViewModel(new([], new([], [], "Firewall service unavailable"), "Adapters unavailable"), new PendingChangesService());
        using var session = UiSession.ForView(new NetworkView(), empty, "network-empty");
        session.Screenshot("connections-unavailable");
        session.ClickText("Firewall"); session.Screenshot("firewall-unavailable");
        Assert.False(empty.NoRules);
        Assert.False(empty.NoAdapters);
    }
}
