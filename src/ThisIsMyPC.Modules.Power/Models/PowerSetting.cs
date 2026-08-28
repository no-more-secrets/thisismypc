using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Modules.Power.Models;

/// <summary>
/// One individual power plan setting. Values are powrprof value indexes: range
/// settings interpret them directly (in <see cref="Units"/>), enumerated
/// settings as an index into <see cref="PossibleValues"/>.
/// </summary>
public sealed record PowerSetting
{
    public required Guid SubgroupGuid { get; init; }
    public required string SubgroupName { get; init; }
    public required Guid SettingGuid { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }

    /// <summary>Null when the per-plan value could not be read.</summary>
    public uint? AcIndex { get; init; }
    public uint? DcIndex { get; init; }

    public string? Units { get; init; }
    public required bool IsRange { get; init; }
    public uint Min { get; init; }
    public uint Max { get; init; }
    public uint Increment { get; init; } = 1;
    public IReadOnlyList<PowerPossibleValue> PossibleValues { get; init; } = [];

    public static PowerSetting FromInfo(PowerSettingInfo info) => new()
    {
        SubgroupGuid = info.SubgroupGuid,
        SubgroupName = info.SubgroupName,
        SettingGuid = info.SettingGuid,
        Name = info.Name,
        Description = info.Description,
        AcIndex = info.AcIndex,
        DcIndex = info.DcIndex,
        Units = info.Units,
        IsRange = info.IsRange,
        Min = info.Min,
        Max = info.Max,
        Increment = info.Increment,
        PossibleValues = info.PossibleValues,
    };

    /// <summary>Human rendering of a value index: possible-value label, or number + units.</summary>
    public string FormatIndex(uint index)
    {
        if (!IsRange)
        {
            var match = PossibleValues.FirstOrDefault(v => v.Index == index);
            if (match is not null)
                return match.Name;
        }
        return string.IsNullOrEmpty(Units) ? index.ToString() : $"{index} {Units}";
    }
}
