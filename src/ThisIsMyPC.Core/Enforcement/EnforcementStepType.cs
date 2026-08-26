namespace ThisIsMyPC.Core.Enforcement;

public enum EnforcementStepType
{
    DisableService,
    EnableService,
    DisableScheduledTask,
    EnableScheduledTask,
    ClearGPCache,
    RestoreGPCache,
    PrimaryMutation,
    VerifyPostMutation,
    TransferOwnership,
    RestoreOwnership
}
