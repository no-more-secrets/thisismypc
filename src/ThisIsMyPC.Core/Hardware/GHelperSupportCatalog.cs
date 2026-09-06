namespace ThisIsMyPC.Core.Hardware;

/// <summary>How sure the policy is that a companion supports this machine.</summary>
public enum SupportVerdict
{
    /// <summary>Not enough identity to say anything.</summary>
    Unknown,

    /// <summary>Known not to apply (wrong vendor, desktop).</summary>
    Unsupported,

    /// <summary>Plausible from vendor, form factor and platform driver, but not confirmed on this model.</summary>
    Unverified,

    /// <summary>Confirmed on this exact model with the platform driver present.</summary>
    Verified,
}

/// <summary>
/// G-Helper support policy. Deliberately tiny: G-Helper targets ASUS laptops on
/// the ATKACPI platform, but not every ASUS laptop, so a model is Verified only
/// when the driver is observed present and the model appears in
/// <see cref="VerifiedModels"/> (added after a real pass on that machine). A
/// running G-Helper process is not support evidence: it starts on unsupported
/// machines too. Everything else that looks plausible is Unverified, and the tab
/// says so instead of offering an install. docs/planning/hardware-compatibility.md
/// tracks the pending entries.
/// </summary>
public static class GHelperSupportCatalog
{
    /// <summary>
    /// Exact SystemProductName values confirmed with G-Helper on real hardware.
    /// Empty until the first verified pass lands; see the planning doc.
    /// </summary>
    public static IReadOnlyList<string> VerifiedModels { get; } = [];

    public static SupportVerdict Evaluate(ObservedHardwareFacts facts, MachineFormFactor formFactor)
    {
        ArgumentNullException.ThrowIfNull(facts);

        if (facts.Identity.Vendor == MachineVendor.Unknown || formFactor == MachineFormFactor.Unknown)
            return SupportVerdict.Unknown;

        if (facts.Identity.Vendor != MachineVendor.Asus || formFactor == MachineFormFactor.Desktop)
            return SupportVerdict.Unsupported;

        // Driver gate first: without ATKACPI nothing G-Helper does can work, and
        // an unprobed driver leaves the answer unknown whatever else is true.
        switch (facts.AsusPlatformDriverPresent)
        {
            case false:
                return SupportVerdict.Unsupported;
            case null:
                return SupportVerdict.Unknown;
        }

        var verified = facts.Identity.Model is { } model
            && VerifiedModels.Contains(model, StringComparer.OrdinalIgnoreCase);

        return verified ? SupportVerdict.Verified : SupportVerdict.Unverified;
    }
}
