using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Policies;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Interop.Com.Policies;

/// <summary>Uses the Windows Group Policy editor API, including its revision and extension bookkeeping.</summary>
public sealed class LocalPolicyService(IRegistryService registry) : ILocalPolicyService
{
    public OperationResult<bool> Apply(string location, ChangeValueType type, LocalPolicyValue before, LocalPolicyValue after)
    {
        OperationResult<bool>? result = null;
        var thread = new Thread(() => result = ApplyOnThread(location, type, before, after));
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return result!;
    }

    private OperationResult<bool> ApplyOnThread(string location, ChangeValueType type, LocalPolicyValue before, LocalPolicyValue after)
    {
        try
        {
            using var session = new LocalMachinePolicySession(location.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase));
            return LocalPolicyTransaction.Apply(session, registry, location, type, before, after);
        }
        catch (Exception ex) { return OperationResult<bool>.Failure("Local policy could not be saved: " + ex.Message, ErrorCategory.ServiceUnavailable, ex); }
    }
}

/// <summary>NativeAOT-safe IGroupPolicyObject and IGroupPolicyObject2 calls for machine and current-account policies.</summary>
internal sealed unsafe partial class LocalMachinePolicySession : ILocalPolicySession
{
    private readonly string _path;
    private readonly bool _user;
    private nint _policy;
    private nint _critical;
    private bool _initialized;
    private RegistryKey? _root;
    private LocalPolicySaveGuard? _guard;

    internal LocalMachinePolicySession(bool user = false)
    {
        _user = user;
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value ?? throw new InvalidOperationException("The current account is unavailable.");
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        _path = user ? Path.Combine(system, "GroupPolicyUsers", sid, "User", "Registry.pol")
            : Path.Combine(system, "GroupPolicy", "Machine", "Registry.pol");
        try
        {
            var hr = CoInitializeEx(0, 2);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);
            _initialized = true;
            _critical = EnterCriticalPolicySection(user ? 0 : 1);
            if (_critical == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            _guard = new(ReadBytes());
            var clsid = new Guid("EA502722-A23D-11d1-A7D3-0000F87571E3");
            var iid = new Guid(user ? "7E37D5E7-263D-45CF-842B-96A95C63E46C" : "EA502723-A23D-11d1-A7D3-0000F87571E3");
            Check(CoCreateInstance(in clsid, 0, 1, in iid, out _policy));
            if (user)
            {
                // IGroupPolicyObject2 appends OpenLocalMachineGPOForPrincipal after the 21 base slots.
                fixed (char* principal = sid)
                    Check(((delegate* unmanaged[Stdcall]<nint, char*, uint, int>)Table[21])(_policy, principal, 1));
            }
            else Check(((delegate* unmanaged[Stdcall]<nint, uint, int>)Table[5])(_policy, 1));
            nint key = 0;
            Check(((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Table[15])(_policy, user ? 1u : 2u, &key)); // GetRegistryKey, user or machine
            _root = RegistryKey.FromHandle(new SafeRegistryHandle(key, true), RegistryView.Registry64);
            EnsureUnchanged();
        }
        catch { Dispose(); throw; }
    }

    private nint* Table => *(nint**)_policy;
    private static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }

    public PolicySourceSnapshot ReadSource()
    {
        var bytes = ReadBytes();
        return new(_user ? "Account policy" : "Local computer policy", _user ? PolicyScope.User : PolicyScope.Machine,
            bytes is null ? PolicyFileStatus.Missing : PolicyFileStatus.Loaded, bytes is null ? [] : RegistryPolicyFile.Parse(bytes))
            { IsAccountPolicy = _user };
    }

    public void WriteSaved(string location, ChangeValueType type, LocalPolicyValue value)
    {
        EnsureUnchanged();
        _guard!.Stage(location, type, value);
        var split = location.LastIndexOf('\\');
        var keyPath = location[5..split];
        var name = location[(split + 1)..];
        using var key = value.Saved is not null || value.Delete ? _root!.CreateSubKey(keyPath, true) : _root!.OpenSubKey(keyPath, true);
        if (key is null) return;
        key.DeleteValue(name, false);
        key.DeleteValue("**del." + name, false);
        if (value.Delete) key.SetValue("**del." + name, " ", RegistryValueKind.String);
        else if (value.Saved is not null)
        {
            if (LocalPolicyValue.RegistryType(type) == ChangeValueType.Registry_String)
                key.SetValue(name, value.Saved, RegistryValueKind.String);
            else key.SetValue(name, int.Parse(value.Saved, System.Globalization.CultureInfo.InvariantCulture), RegistryValueKind.DWord);
        }
    }

    public void Save()
    {
        EnsureUnchanged();
        var extension = new Guid("35378EAC-683F-11D2-A89A-00C04FBBCFA2");
        var editor = new Guid("0F6B957D-509E-11D1-A7CC-0000F87571E3");
        // Save updates Registry.pol, gpt.ini and registry-extension registration together.
        var hr = ((delegate* unmanaged[Stdcall]<nint, int, int, Guid*, Guid*, int>)Table[7])(_policy, _user ? 0 : 1, HasValues(_root!) ? 1 : 0, &extension, &editor);
        var actual = ReadBytes();
        // A failed Save can still write. Only adopt a complete, verified result;
        // otherwise the loaded hive is stale and must never be used to overwrite it.
        if (!SameBytes(_guard!.Expected, actual)) _guard.AcceptSaved(actual);
        Check(hr);
    }

    private static bool HasValues(RegistryKey key)
    {
        if (key.ValueCount != 0) return true;
        foreach (var name in key.GetSubKeyNames())
        {
            using var child = key.OpenSubKey(name);
            if (child is not null && HasValues(child)) return true;
        }
        return false;
    }

    private void EnsureUnchanged()
    {
        var current = ReadBytes();
        if (!SameBytes(_guard!.Expected, current))
            throw new InvalidOperationException("Local policy changed in another program. Refresh before applying.");
    }

    private static bool SameBytes(byte[]? expected, byte[]? actual) => (expected is null) == (actual is null)
        && (expected is null || expected.AsSpan().SequenceEqual(actual));

    private byte[]? ReadBytes()
    {
        try
        {
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > RegistryPolicyFile.MaximumFileBytes) throw new InvalidDataException("Local policy exceeds the supported size.");
            var bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            return bytes;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public void Dispose()
    {
        _root?.Dispose(); _root = null;
        if (_policy != 0) { ((delegate* unmanaged[Stdcall]<nint, uint>)Table[2])(_policy); _policy = 0; }
        if (_critical != 0) { LeaveCriticalPolicySection(_critical); _critical = 0; }
        if (_initialized) { CoUninitialize(); _initialized = false; }
    }

    [LibraryImport("ole32.dll")] private static partial int CoInitializeEx(nint reserved, uint flags);
    [LibraryImport("ole32.dll")] private static partial void CoUninitialize();
    [LibraryImport("ole32.dll")] private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);
    [LibraryImport("userenv.dll", SetLastError = true)] private static partial nint EnterCriticalPolicySection(int machine);
    [LibraryImport("userenv.dll")] private static partial int LeaveCriticalPolicySection(nint section);
}
