namespace ThisIsMyPC.Core.Changes;

public enum ChangeCategory
{
    Enable,
    Disable,
    Modify,
    Create,
    Delete,
    /// <summary>Windows reverted a ThisIsMyPC-applied setting (drift, 28-3); system-initiated, not user-initiated.</summary>
    SystemReversion
}
