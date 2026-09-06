namespace ThisIsMyPC.Core.Coordination;

/// <summary>
/// Lock object names. The production lease is one machine-wide object in the
/// global kernel namespace so the Session 0 service and the interactive app
/// contend for the same lock. Tests never use it: they take a fresh
/// <c>Local\</c> name per run through <see cref="ForTest"/>.
/// </summary>
public static class MutationLeaseNames
{
    /// <summary>The one production lock. Nothing in the test tree may reference it.</summary>
    public const string Production = @"Global\ThisIsMyPC.OwnerMode.MutationLease";

    private const string GlobalPrefix = @"Global\";
    private const string LocalPrefix = @"Local\";
    private const string TestPrefix = @"Local\ThisIsMyPC.Test.MutationLease.";

    /// <summary>A unique session-local name that can never collide with production.</summary>
    public static string ForTest() => TestPrefix + Guid.NewGuid().ToString("N");

    /// <summary>
    /// True for <c>Global\</c> or <c>Local\</c> names whose remainder is
    /// non-empty, has no further backslash, and contains only printable ASCII.
    /// </summary>
    public static bool IsValid(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        string remainder;
        if (name.StartsWith(GlobalPrefix, StringComparison.Ordinal))
            remainder = name[GlobalPrefix.Length..];
        else if (name.StartsWith(LocalPrefix, StringComparison.Ordinal))
            remainder = name[LocalPrefix.Length..];
        else
            return false;

        if (remainder.Length == 0 || remainder.Length > 200)
            return false;

        foreach (char c in remainder)
        {
            if (c is < (char)0x21 or > (char)0x7E || c == '\\')
                return false;
        }

        return true;
    }
}
