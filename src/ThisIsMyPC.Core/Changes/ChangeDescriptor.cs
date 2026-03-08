namespace ThisIsMyPC.Core.Changes;

public record ChangeDescriptor
{
    public required string ModuleId { get; init; }
    public required string SettingId { get; init; }
    public required string DisplayName { get; init; }
    public required string SystemLocation { get; init; }
    public required string BeforeValue { get; init; }
    public required string? AfterValue { get; init; }
    public required string BeforeDisplay { get; init; }
    public required string? AfterDisplay { get; init; }
    public required ChangeValueType ValueType { get; init; }
    public ChangeCategory Category { get; init; }
    public RestartRequirement RestartRequirement { get; init; }
}
