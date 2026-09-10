namespace ThisIsMyPC.Modules.Power.Models;

/// <summary>
/// The Group Policy override for "Allow standby states (S1-S3) when sleeping",
/// one index per power source. Null means the value is absent, so the active
/// plan decides. 0 blocks sleep and removes Sleep from the power menu.
/// </summary>
/// <param name="PluggedIn">ACSettingIndex, or null when absent.</param>
/// <param name="OnBattery">DCSettingIndex, or null when absent.</param>
public sealed record SleepPolicy(int? PluggedIn, int? OnBattery)
{
    /// <summary>Neither value exists; plans decide.</summary>
    public static readonly SleepPolicy Unset = new(null, null);

    /// <summary>True unless a policy value blocks sleep for either power source.</summary>
    public bool AllowsSleep => PluggedIn != 0 && OnBattery != 0;
}
