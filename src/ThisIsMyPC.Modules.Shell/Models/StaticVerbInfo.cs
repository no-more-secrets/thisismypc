namespace ThisIsMyPC.Modules.Shell.Models;

public sealed record StaticVerbInfo(
    string VerbName,
    string? MuiVerb,
    string? Icon,
    string? Position,
    bool IsExtended,
    string? CommandLine,
    string? DelegateExecuteClsid,
    bool IsLegacyDisabled,
    string? AppliesTo,
    bool HasLuaShield,
    bool IsProgrammaticAccessOnly);
