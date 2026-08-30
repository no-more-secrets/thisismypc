using ThisIsMyPC.Core.Packages;
using ThisIsMyPC.Modules.Software.Services;

namespace ThisIsMyPC.Modules.Software.Tests;

public class SoftwareCatalogTests
{
    [Fact]
    public void Entries_LoadsEmbeddedCatalog()
    {
        var entries = SoftwareCatalog.Entries;

        Assert.True(entries.Count >= 200, $"Expected the ported winutil catalog, got {entries.Count} entries");
    }

    [Fact]
    public void Entries_IdsAreUnique()
    {
        var entries = SoftwareCatalog.Entries;

        Assert.Equal(entries.Count, entries.Select(e => e.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Entries_AllFieldsPopulated()
    {
        Assert.All(SoftwareCatalog.Entries, e =>
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Id));
            Assert.False(string.IsNullOrWhiteSpace(e.Name));
            Assert.False(string.IsNullOrWhiteSpace(e.Description));
            Assert.False(string.IsNullOrWhiteSpace(e.Category));
            Assert.False(string.IsNullOrWhiteSpace(e.WingetId));
            // Winget ids never contain whitespace; a corrupted port would break installs.
            Assert.DoesNotContain(e.WingetId, c => char.IsWhiteSpace(c));
        });
    }

    [Fact]
    public void Entries_MsStoreSourceIsParsed()
    {
        // winutil marks ChatGPT and WhatsApp as msstore-sourced.
        Assert.Contains(SoftwareCatalog.Entries, e => e.Source == WingetSource.MsStore);
        Assert.All(
            SoftwareCatalog.Entries.Where(e => e.Source == WingetSource.MsStore),
            e => Assert.DoesNotContain("msstore:", e.WingetId, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Entries_HasExpectedCategories()
    {
        var categories = SoftwareCatalog.Entries.Select(e => e.Category).Distinct().ToList();

        Assert.Contains("Browsers", categories);
        Assert.Contains("Development", categories);
        Assert.Contains("Utilities", categories);
    }
}
