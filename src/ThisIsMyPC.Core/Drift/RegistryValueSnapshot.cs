using System.Globalization;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Core.Drift;

/// <summary>
/// One registry value as observed at one instant: either present with typed
/// data, or absent. Absence is a distinct state, never a sentinel string: a
/// present empty string and a present DWORD 0 are both "present" and neither
/// equals <see cref="Absent"/>. Owner Mode restoration reasons about this
/// record, not about the "__absent__" text the drift report uses for display.
/// </summary>
public sealed record RegistryValueSnapshot
{
    private RegistryValueSnapshot(RegistryValueData? value)
    {
        Value = value;
    }

    /// <summary>The value does not exist under its key.</summary>
    public static RegistryValueSnapshot Absent { get; } = new((RegistryValueData?)null);

    /// <summary>Typed data when present; null when absent.</summary>
    public RegistryValueData? Value { get; }

    public bool IsPresent => Value is not null;

    /// <summary>A present value. Empty strings and zero are valid present data.</summary>
    public static RegistryValueSnapshot Present(RegistryValueData value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new RegistryValueSnapshot(value);
    }

    /// <summary>
    /// Maps a typed read to a snapshot. Only a NotFound failure means absent;
    /// any other failure (access denied, hive not loaded, service error) is
    /// passed through so a failed probe is never mistaken for a missing value.
    /// </summary>
    public static OperationResult<RegistryValueSnapshot> FromRead(OperationResult<RegistryValueData> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        if (read.IsSuccess && read.Value is { } value)
            return OperationResult<RegistryValueSnapshot>.Success(Present(value));
        if (!read.IsSuccess && read.ErrorCategory == ErrorCategory.NotFound)
            return OperationResult<RegistryValueSnapshot>.Success(Absent);
        return OperationResult<RegistryValueSnapshot>.Failure(
            read.ErrorMessage ?? "Registry read failed without a message.",
            read.ErrorCategory ?? ErrorCategory.ServiceUnavailable,
            read.Exception);
    }

    /// <summary>
    /// True when the value is present, has the same kind as <paramref name="desired"/>,
    /// and the same canonical data. Kinds never cross-match: a string "1" does not
    /// match DWORD 1. Data that fails to canonicalize on either side never matches.
    /// </summary>
    public bool Matches(RegistryValueData desired)
    {
        ArgumentNullException.ThrowIfNull(desired);
        if (Value is null || Value.Kind != desired.Kind)
            return false;
        return TryCanonicalize(Value, out var actual)
            && TryCanonicalize(desired, out var expected)
            && actual == expected;
    }

    /// <summary>
    /// Canonical form of one value: numbers re-rendered as invariant decimal
    /// (so "01" and "1" agree, whitespace and hex do not), binary re-encoded
    /// as base64, strings unchanged (case and whitespace are data; empty is
    /// valid). Returns false, never throws, for null data, an unrecognized
    /// kind, or data that is not valid for its kind.
    /// </summary>
    public static bool TryCanonicalize(RegistryValueData value, out RegistryValueData canonical)
    {
        ArgumentNullException.ThrowIfNull(value);
        canonical = value;
        var data = value.Data;
        if (data is null)
            return false;

        switch (value.Kind)
        {
            case RegistryValueDataKind.String:
            case RegistryValueDataKind.ExpandString:
            case RegistryValueDataKind.MultiString:
                return true;
            case RegistryValueDataKind.DWord:
                if (!int.TryParse(data, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var dword))
                    return false;
                canonical = RegistryValueData.FromDWord(dword);
                return true;
            case RegistryValueDataKind.QWord:
                if (!long.TryParse(data, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var qword))
                    return false;
                canonical = RegistryValueData.FromQWord(qword);
                return true;
            case RegistryValueDataKind.Binary:
                var buffer = new byte[data.Length];
                if (!Convert.TryFromBase64String(data, buffer, out var written))
                    return false;
                canonical = RegistryValueData.FromBinary(buffer[..written]);
                return true;
            default:
                return false;
        }
    }
}
