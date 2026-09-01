namespace ThisIsMyPC.Integration.Tests;

/// <summary>
/// View-model toggles stage their change after a 250 ms debounce. A fixed
/// sleep just past that overshoots on a loaded CI runner, so tests wait for
/// the observable outcome instead, with a ceiling wide enough for any runner.
/// </summary>
internal static class Debounce
{
    private const int TimeoutMs = 5000;
    private const int PollMs = 20;

    /// <summary>
    /// Polls until the condition holds or the ceiling passes. Returns either
    /// way so the caller's own assertion reports the real failure.
    /// </summary>
    public static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(TimeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                return;
            await Task.Delay(PollMs);
        }
    }

    /// <summary>
    /// For cases that assert nothing happened: the debounce window plus a
    /// margin that covers a slow runner.
    /// </summary>
    public static Task SettleAsync() => Task.Delay(1000);
}
