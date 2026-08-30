namespace ThisIsMyPC.Core.Cards;

/// <summary>
/// UI-facing projection of SettingEnforcement. The card layer needs the enforcement
/// posture (badge level) and a user-facing summary; never companion service names or
/// other enforcement internals.
/// </summary>
public record EnforcementProfile
{
    public required EnforcementLevel Level { get; init; }

    /// <summary>User-facing mechanism summary, e.g. "Protected by UCPD driver".</summary>
    public string? Summary { get; init; }

    /// <summary>Known reversion vectors, e.g. "May revert after Windows feature updates".</summary>
    public IReadOnlyList<string>? ReversionRisks { get; init; }
}

public enum EnforcementLevel { None, Simple, Enforced, OwnerRequired }
