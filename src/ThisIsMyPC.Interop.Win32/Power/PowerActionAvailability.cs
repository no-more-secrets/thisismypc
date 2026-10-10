namespace ThisIsMyPC.Interop.Win32.Power;

/// <summary>Read-only availability of sleep and full hibernation.</summary>
public static class PowerActionAvailability
{
    public static (bool? Sleep, bool? Hibernate, bool ModernStandby) Read()
    {
        if (!NativePower.GetPwrCapabilities(out var caps)) return (null, null, false);
        return (caps.SystemS1 != 0 || caps.SystemS2 != 0 || caps.SystemS3 != 0 || caps.AoAc != 0,
            caps.SystemS4 != 0 && caps.HiberFilePresent != 0 && caps.HiberFileType == 2, caps.AoAc != 0);
    }
}
