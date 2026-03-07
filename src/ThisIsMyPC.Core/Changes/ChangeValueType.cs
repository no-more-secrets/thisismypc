namespace ThisIsMyPC.Core.Changes;

#pragma warning disable CA1707 // Underscores are intentional — type-prefixed discriminator naming
public enum ChangeValueType
{
    Registry_DWord,
    Registry_String,
    Registry_Binary,
    Registry_MultiString,
    Registry_ExpandString,
    Service_StartType,
    ScheduledTask_State,
    PowerPlan_Setting,
    Environment_Variable,
    File_Content
}
