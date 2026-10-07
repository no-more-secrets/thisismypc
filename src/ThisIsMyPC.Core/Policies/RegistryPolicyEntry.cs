using System.Collections.Immutable;

namespace ThisIsMyPC.Core.Policies;

/// <summary>
/// An ordered Registry.pol instruction. The source file determines the registry hive.
/// Data stays opaque so unknown value types and policy directives survive unchanged.
/// </summary>
public sealed record RegistryPolicyEntry(string KeyPath, string ValueName, uint ValueType, ImmutableArray<byte> Data)
{
    /// <summary>Deletion and security instructions must not be presented as ordinary setting values.</summary>
    public bool IsDirective => ValueName.StartsWith("**", StringComparison.Ordinal);
}
