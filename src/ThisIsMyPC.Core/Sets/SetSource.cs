namespace ThisIsMyPC.Core.Sets;

public enum SetSource
{
    /// <summary>Bundled with the application.</summary>
    BuiltIn,

    /// <summary>Loaded from the user sets directory (%APPDATA%\ThisIsMyPC\sets\).</summary>
    User
}
