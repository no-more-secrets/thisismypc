namespace ThisIsMyPC.Interop.Win32.Registry;

public sealed record StaticVerbEntry(
    string VerbName,
    string RegistryPath,
    string Scope,
    string? MuiVerb,
    string? Icon,
    string? Position,
    bool IsExtended,
    string? CommandLine,
    string? DelegateExecuteClsid,
    bool HasDropTarget,
    bool IsLegacyDisabled,
    string? AppliesTo,
    bool HasLuaShield,
    bool IsProgrammaticAccessOnly);
