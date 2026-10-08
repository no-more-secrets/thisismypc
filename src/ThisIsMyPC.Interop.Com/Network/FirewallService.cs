using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using ThisIsMyPC.Core.Network;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Interop.Com.Network;

/// <summary>NativeAOT-safe Windows Firewall COM access. Slots follow Windows SDK netfw.h.</summary>
public sealed unsafe partial class FirewallService : IFirewallService
{
    public FirewallState Read() => WithPolicy(policy =>
    {
        var profiles = new List<FirewallProfileState>();
        var active = Int(policy, 7);
        var modifiable = Int(policy, 28) == 0;
        foreach (var (id, name) in new[] { (1, "Domain"), (2, "Private"), (4, "Public") })
            profiles.Add(new(id, name, ProfileBool(policy, 8, id), (active & id) != 0,
                ProfileInt(policy, 23, id) == 1 ? "Allow" : "Block",
                ProfileInt(policy, 25, id) == 1 ? "Allow" : "Block", modifiable && !HasProfilePolicy(name)));
        try { return new FirewallState(profiles, ReadRules(policy)); }
        catch (Exception ex) { return new FirewallState(profiles, [], "Firewall rules could not be read: " + ex.Message); }
    });

    public OperationResult<bool> SetEnabled(int profile, bool expected, bool desired)
    {
        try
        {
            return WithPolicy(policy =>
            {
                var name = profile switch { 1 => "Domain", 2 => "Private", 4 => "Public", _ => throw new ArgumentOutOfRangeException(nameof(profile)) };
                if (Int(policy, 28) != 0 || HasProfilePolicy(name))
                    throw new InvalidOperationException("Firewall policy prevents local changes.");
                if (ProfileBool(policy, 8, profile) != expected)
                    throw new InvalidOperationException("Firewall state changed. Refresh before applying.");
                Check(((delegate* unmanaged[Stdcall]<nint, int, short, int>)Table(policy)[9])(policy, profile, desired ? (short)-1 : (short)0));
                return OperationResult<bool>.Success(true);
            });
        }
        catch (Exception ex) { return OperationResult<bool>.Failure(ex.Message, ErrorCategory.ServiceUnavailable, ex); }
    }

    private static bool HasProfilePolicy(string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\WindowsFirewall\" + name + "Profile");
        return key is not null && (key.ValueCount != 0 || key.SubKeyCount != 0);
    }

    private static IReadOnlyList<FirewallRuleState> ReadRules(nint policy)
    {
        var rules = Pointer(policy, 18);
        try
        {
            var unknown = Pointer(rules, 11);
            try
            {
                var iid = new Guid("00020404-0000-0000-C000-000000000046");
                nint enumerator = 0;
                Check(((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Table(unknown)[0])(unknown, &iid, &enumerator));
                try
                {
                    var result = new List<FirewallRuleState>();
                    while (true)
                    {
                        Variant item = default;
                        uint fetched = 0;
                        var hr = ((delegate* unmanaged[Stdcall]<nint, uint, Variant*, uint*, int>)Table(enumerator)[3])(enumerator, 1, &item, &fetched);
                        Check(hr);
                        try
                        {
                            if (fetched == 0) break;
                            if (result.Count >= 20000) throw new InvalidOperationException("The firewall rule count exceeds this view's limit.");
                            if (item.Type != 9 || item.Value == 0) throw new InvalidOperationException("Unexpected firewall rule representation.");
                            var ruleIid = new Guid("AF230D27-BABA-4E42-ACED-F524F22CFCE2");
                            nint rule = 0;
                            Check(((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Table(item.Value)[0])(item.Value, &ruleIid, &rule));
                            try
                            {
                                var protocol = Int(rule, 15);
                                result.Add(new(ResolveName(Text(rule, 7)), Text(rule, 11), Text(rule, 13), Bool(rule, 33),
                                    Int(rule, 27) == 1 ? "Inbound" : "Outbound", Int(rule, 41) == 1 ? "Allow" : "Block",
                                    protocol switch { 6 => "TCP", 17 => "UDP", 256 => "Any", _ => protocol.ToString(CultureInfo.InvariantCulture) },
                                    protocol is 6 or 17 ? Text(rule, 17) : "", protocol is 6 or 17 ? Text(rule, 19) : "",
                                    Text(rule, 21), Text(rule, 23), ProfileNames(Int(rule, 37))));
                            }
                            finally { Release(rule); }
                        }
                        finally { Check(VariantClear(ref item)); }
                    }
                    return result.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
                }
                finally { Release(enumerator); }
            }
            finally { Release(unknown); }
        }
        finally { Release(rules); }
    }

    private static string ProfileNames(int flags) => string.Join(", ", new[] { (1, "Domain"), (2, "Private"), (4, "Public") }.Where(p => (flags & p.Item1) != 0).Select(p => p.Item2));
    private static string ResolveName(string name)
    {
        if (!name.StartsWith('@')) return name;
        var buffer = stackalloc char[2048];
        return SHLoadIndirectString(name, buffer, 2048, 0) >= 0 ? new string(buffer) : name;
    }
    private static T WithPolicy<T>(Func<nint, T> operation)
    {
        var initialized = CoInitializeEx(0, 0);
        if (initialized < 0 && initialized != unchecked((int)0x80010106)) Check(initialized);
        try
        {
            var clsid = new Guid("E2B3C97F-6AE1-41AC-817A-F6F92166D7DD");
            var iid = new Guid("98325047-C671-4174-8D81-DEFCD3F03186");
            Check(CoCreateInstance(in clsid, 0, 1, in iid, out var policy));
            try { return operation(policy); }
            finally { Release(policy); }
        }
        finally { if (initialized >= 0) CoUninitialize(); }
    }
    private static nint* Table(nint instance) => *(nint**)instance;
    private static void Release(nint instance) { if (instance != 0) ((delegate* unmanaged[Stdcall]<nint, uint>)Table(instance)[2])(instance); }
    private static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }
    private static int Int(nint instance, int slot) { int value = 0; Check(((delegate* unmanaged[Stdcall]<nint, int*, int>)Table(instance)[slot])(instance, &value)); return value; }
    private static bool Bool(nint instance, int slot) { short value = 0; Check(((delegate* unmanaged[Stdcall]<nint, short*, int>)Table(instance)[slot])(instance, &value)); return value != 0; }
    private static int ProfileInt(nint instance, int slot, int profile) { int value = 0; Check(((delegate* unmanaged[Stdcall]<nint, int, int*, int>)Table(instance)[slot])(instance, profile, &value)); return value; }
    private static bool ProfileBool(nint instance, int slot, int profile) { short value = 0; Check(((delegate* unmanaged[Stdcall]<nint, int, short*, int>)Table(instance)[slot])(instance, profile, &value)); return value != 0; }
    private static nint Pointer(nint instance, int slot) { nint value = 0; Check(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Table(instance)[slot])(instance, &value)); return value; }
    private static string Text(nint instance, int slot)
    {
        var value = Pointer(instance, slot);
        try { return value == 0 ? "" : Marshal.PtrToStringBSTR(value); }
        finally { if (value != 0) Marshal.FreeBSTR(value); }
    }
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct Variant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public nint Value; }
    [LibraryImport("ole32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int CoInitializeEx(nint reserved, uint flags);
    [LibraryImport("ole32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void CoUninitialize();
    [LibraryImport("ole32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);
    [LibraryImport("oleaut32.dll"), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int VariantClear(ref Variant variant);
    [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16), DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int SHLoadIndirectString(string source, char* output, uint count, nint reserved);
}
