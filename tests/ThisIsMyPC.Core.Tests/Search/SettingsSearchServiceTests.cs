using ThisIsMyPC.Core.Search;

namespace ThisIsMyPC.Core.Tests.Search;

public class SettingsSearchServiceTests
{
    private sealed class StubContributor(string moduleId, params SearchEntry[] entries) : ISearchSettingsContributor
    {
        public string ModuleId { get; } = moduleId;
        public IReadOnlyList<SearchEntry> GetSearchEntries() => entries;
    }

    private static SearchEntry Entry(string module, string id, string name, string description = "d", params string[] keywords) =>
        new(module, id, name, description, keywords);

    private static SettingsSearchService Create(params ISearchSettingsContributor[] contributors) =>
        new(contributors, _ => (true, null));

    [Fact]
    public void ShortOrEmptyQuery_ReturnsNothing()
    {
        var service = Create(new StubContributor("M", Entry("M", "a", "Taskbar alignment")));

        Assert.Empty(service.Search(""));
        Assert.Empty(service.Search(" "));
        Assert.Empty(service.Search("t"));
    }

    [Fact]
    public void Ranks_NameOverKeywordOverDescription()
    {
        var service = Create(new StubContributor("M",
            Entry("M", "desc", "Something else", "mentions taskbar in description"),
            Entry("M", "key", "Widgets button", "d", "taskbar"),
            Entry("M", "name", "Taskbar alignment")));

        var results = service.Search("taskbar");

        Assert.Equal(["name", "key", "desc"], results.Select(r => r.Entry.SettingId));
    }

    [Fact]
    public void KeywordMatch_FindsRegistryPathsAndServiceNames()
    {
        var service = Create(new StubContributor("M",
            Entry("M", "wu", "Notify before downloading updates", "d",
                @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "AUOptions")));

        Assert.Single(service.Search("AUOptions"));
        Assert.Single(service.Search("Policies\\Microsoft"));
    }

    [Fact]
    public void UnavailableModules_StillListed_MarkedWithReason()
    {
        var service = new SettingsSearchService(
            [new StubContributor("Ghost", Entry("Ghost", "g", "Fan curve editor"))],
            _ => (false, "Hardware not detected"));

        var result = Assert.Single(service.Search("fan curve"));

        Assert.False(result.ModuleAvailable);
        Assert.Equal("Hardware not detected", result.UnavailableReason);
    }

    [Fact]
    public void Results_AreCapped()
    {
        var entries = Enumerable.Range(0, 40)
            .Select(i => Entry("M", $"s{i}", $"Taskbar setting {i}"))
            .ToArray();
        var service = Create(new StubContributor("M", entries));

        Assert.Equal(SettingsSearchService.MaxResults, service.Search("taskbar").Count);
    }
}
