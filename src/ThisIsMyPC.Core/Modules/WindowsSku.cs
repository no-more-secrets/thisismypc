namespace ThisIsMyPC.Core.Modules;

/// <summary>
/// Windows edition. For policy support these form tiers: Home &lt; Pro &lt;
/// Enterprise/Education (the last two are equivalent; see <see cref="Tier"/>).
/// </summary>
public enum WindowsSku { Home, Pro, Enterprise, Education }

public static class WindowsSkuExtensions
{
    /// <summary>Policy-support tier: Home=0, Pro=1, Enterprise/Education=2.</summary>
    public static int Tier(this WindowsSku sku) => sku switch
    {
        WindowsSku.Home => 0,
        WindowsSku.Pro => 1,
        _ => 2,
    };
}
