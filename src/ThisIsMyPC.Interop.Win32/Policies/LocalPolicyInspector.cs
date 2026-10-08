using System.Collections.Immutable;
using System.Security.Principal;
using ThisIsMyPC.Core.Policies;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Interop.Win32.Policies;

/// <summary>Read-only local policy evidence for the signed-in account. Does not enumerate profiles or load hives.</summary>
public sealed class LocalPolicyInspector(IRegistryService registry)
{
    public ImmutableArray<PolicyObservation> ReadCurrent()
    {
        var sources = ReadCurrentSources();
        return PracticalPolicyCatalog.Definitions.SelectMany(definition => definition.Scopes.Select(scope =>
            PolicyStateReader.Read(definition, scope, sources,
                registry.ReadValue((scope == PolicyScope.Machine ? "HKLM\\" : "HKCU\\") + definition.KeyPath, definition.ValueName))))
            .ToImmutableArray();
    }

    /// <summary>Local GPO, then applicable group GPO, then account GPO. Domain and MDM sources are not inferred.</summary>
    public static ImmutableArray<PolicySourceSnapshot> ReadCurrentSources()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value ?? throw new InvalidOperationException("The current account SID is unavailable.");
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var result = ImmutableArray.CreateBuilder<PolicySourceSnapshot>();
        result.Add(ReadFile("Local computer policy", PolicyScope.Machine, Path.Combine(system, "GroupPolicy", "Machine", "Registry.pol")));
        result.Add(ReadFile("Local user policy", PolicyScope.User, Path.Combine(system, "GroupPolicy", "User", "Registry.pol")));
        // Groups includes deny-only administrator membership in a filtered UAC token.
        // Token elevation alone would select the wrong multiple-local-GPO branch.
        if (identity.Groups is not { } groups)
            result.Add(new("Local group policy", PolicyScope.User, PolicyFileStatus.Unreadable, [], "Account group membership is unavailable."));
        else
        {
            var admin = groups.Any(group => group.Value == "S-1-5-32-544");
            result.Add(ReadFile(admin ? "Administrators policy" : "Non-administrators policy", PolicyScope.User,
                Path.Combine(system, "GroupPolicyUsers", admin ? "S-1-5-32-544" : "S-1-5-32-545", "User", "Registry.pol")));
        }
        result.Add(ReadFile("Account policy", PolicyScope.User, Path.Combine(system, "GroupPolicyUsers", sid, "User", "Registry.pol")) with { IsAccountPolicy = true });
        return result.ToImmutable();
    }

    private static PolicySourceSnapshot ReadFile(string name, PolicyScope scope, string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > RegistryPolicyFile.MaximumFileBytes)
                return new(name, scope, PolicyFileStatus.Unreadable, [], "The local policy file exceeds the supported size.");
            var bytes = new byte[checked((int)stream.Length)];
            stream.ReadExactly(bytes);
            return new(name, scope, PolicyFileStatus.Loaded, RegistryPolicyFile.Parse(bytes));
        }
        catch (FileNotFoundException) { return new(name, scope, PolicyFileStatus.Missing, []); }
        catch (DirectoryNotFoundException) { return new(name, scope, PolicyFileStatus.Missing, []); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new(name, scope, PolicyFileStatus.Unreadable, [], ex.Message);
        }
    }
}
