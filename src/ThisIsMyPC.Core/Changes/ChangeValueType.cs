namespace ThisIsMyPC.Core.Changes;

#pragma warning disable CA1707 // Underscores are intentional; type-prefixed discriminator naming
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
    File_Content,
    Shell_CustomVerb,
    Registry_KeyTree,

    /// <summary>
    /// Autoruns-style enable/disable of one autostart item: registry values and
    /// keys move into an AutorunsDisabled sibling, startup files into an
    /// AutorunsDisabled subfolder, services and drivers swap Start with an
    /// AutorunsDisabled value, tasks flip Enabled. Before/After are "Enabled"
    /// or "Disabled"; SystemLocation names the item (see AutorunTarget).
    /// </summary>
    Autorun_State
}
