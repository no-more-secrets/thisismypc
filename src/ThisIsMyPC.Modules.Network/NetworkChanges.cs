using System.Net;
using System.Net.Sockets;
using ThisIsMyPC.Core.Changes;

namespace ThisIsMyPC.Modules.Network;

/// <summary>Shared UI and broker schema. Never accepts arbitrary commands or registry paths.</summary>
public static class NetworkChanges
{
    public const string ModuleName = "Network & Firewall";
    public const string Automatic = "<automatic>";

    public static bool TryDns(string text, out string normalized)
    {
        normalized = Automatic;
        if (text == Automatic || string.IsNullOrWhiteSpace(text)) return true;
        var values = text.Split([' ', ',', ';', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (values.Length is 0 or > 8) return false;
        var addresses = new List<string>();
        foreach (var value in values)
        {
            if (!IPAddress.TryParse(value, out var address) || address.AddressFamily != AddressFamily.InterNetwork
                || !string.Equals(value, address.ToString(), StringComparison.Ordinal)
                || address.Equals(IPAddress.Any) || address.Equals(IPAddress.Broadcast)
                || address.GetAddressBytes()[0] >= 224) return false;
            addresses.Add(address.ToString());
        }
        normalized = string.Join(",", addresses.Distinct(StringComparer.Ordinal));
        return true;
    }

    public static bool Allows(ChangeDescriptor change)
    {
        if (change.ModuleId != ModuleName || change.ValueType != ChangeValueType.Network_Setting
            || change.Category != ChangeCategory.Modify || change.Enforcement is not null
            || change.RestartRequirement != RestartRequirement.None
            || change.SettingId != change.SystemLocation || change.AfterValue is null) return false;
        var parts = change.SettingId.Split('/');
        if (parts.Length != 2) return false;
        if (parts[0] == "firewall")
            return parts[1] is "1" or "2" or "4" && State(change.BeforeValue) && State(change.AfterValue);
        if (!Guid.TryParseExact(parts[1], "D", out var id) || id == Guid.Empty) return false;
        return parts[0] switch
        {
            "adapter" => State(change.BeforeValue) && State(change.AfterValue),
            "dns" => change.BeforeValue.Length <= 2048 && change.AfterValue.Length <= 2048
                && TryDns(change.BeforeValue, out _) && TryDns(change.AfterValue, out _),
            _ => false,
        };
    }

    public static ChangeDescriptor Create(string target, string name, string before, string after)
    {
        var change = new ChangeDescriptor
        {
            ModuleId = ModuleName, SettingId = target, SystemLocation = target, DisplayName = name,
            BeforeValue = before, AfterValue = after, BeforeDisplay = Display(before), AfterDisplay = Display(after),
            ValueType = ChangeValueType.Network_Setting, Category = ChangeCategory.Modify,
        };
        if (!Allows(change)) throw new ArgumentException("Invalid network change.", nameof(target));
        return change;
    }

    public static string Display(string value) => value switch
    {
        "true" => "Enabled", "false" => "Disabled", Automatic => "Automatic", _ => value,
    };
    private static bool State(string value) => value is "true" or "false";
}
