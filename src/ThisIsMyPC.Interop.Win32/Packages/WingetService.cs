using System.Diagnostics;
using System.Text;
using ThisIsMyPC.Core.Packages;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Interop.Win32.Packages;

/// <summary>
/// Runs winget.exe with explicit argument lists (no shell, no injection surface).
/// Flag recipe follows CTT winutil (MIT): silent, agreements pre-accepted,
/// interactivity disabled. Installed-state and update detection parse winget's
/// localized fixed-width tables by header column offsets (see ParseTableCells).
/// </summary>
public sealed class WingetService : IWingetService
{
    // Keep only the last lines of process output for error messages; winget
    // renders progress spinners that make full output enormous.
    private const int OutputTailLines = 12;

    // The version probe gates module availability during app startup and must
    // never hang the shell; export feeds the scan. Installs get no artificial
    // deadline; a large package on a slow line legitimately takes a long time.
    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ListTimeout = TimeSpan.FromSeconds(90);

    // winget upgrade refreshes sources over the network; far slower than list.
    private static readonly TimeSpan UpgradeListTimeout = TimeSpan.FromMinutes(3);

    // Generous; a large package on a slow line is legitimate; but bounded:
    // a hung silent installer must not wedge the apply pipeline forever.
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromMinutes(30);

    public async Task<OperationResult<string>> GetVersionAsync(CancellationToken cancellationToken = default)
    {
        var run = await RunWingetAsync(["--version"], cancellationToken, VersionTimeout).ConfigureAwait(false);
        if (!run.IsSuccess)
            return OperationResult<string>.Failure(run.ErrorMessage!, run.ErrorCategory!.Value);

        var (exitCode, output) = run.Value;
        if (exitCode != 0)
        {
            return OperationResult<string>.Failure(
                FormatExitError("winget --version", exitCode, output),
                ErrorCategory.ServiceUnavailable);
        }

        return OperationResult<string>.Success(output.Trim());
    }

    public async Task<OperationResult<IReadOnlyList<InstalledWingetPackage>>> ListInstalledAsync(
        CancellationToken cancellationToken = default)
    {
        // winget list reads Add/Remove Programs plus the source cache, so it sees
        // installs winget export cannot map to a source (and it answers from
        // local data in seconds instead of hitting the network).
        var run = await RunWingetAsync(
            ["list", "--accept-source-agreements", "--disable-interactivity"],
            cancellationToken, ListTimeout).ConfigureAwait(false);
        if (!run.IsSuccess)
        {
            return OperationResult<IReadOnlyList<InstalledWingetPackage>>.Failure(
                run.ErrorMessage!, run.ErrorCategory!.Value);
        }

        var (exitCode, output) = run.Value;
        var packages = ParseListTable(output);

        // Non-zero with rows is the benign "some sources grumbled" case; non-zero
        // AND empty means the listing as a whole failed. Never present that as
        // "nothing installed".
        if (exitCode != 0 && packages.Count == 0)
        {
            return OperationResult<IReadOnlyList<InstalledWingetPackage>>.Failure(
                FormatExitError("winget list", exitCode, output),
                ErrorCategory.ServiceUnavailable);
        }

        return OperationResult<IReadOnlyList<InstalledWingetPackage>>.Success(packages);
    }

    // winget's "no installed package found matching input criteria"; the
    // normal everything-is-current outcome of "winget upgrade", not an error.
    private const int NoPackagesFoundExitCode = unchecked((int)0x8A150014);

    // "installed version is already the latest"; the package updated itself
    // between staging and Apply; done is done. NoPackagesFound is benign for a
    // targeted upgrade too: it means the package was uninstalled after staging,
    // so no update is pending; failing would strand the action in the queue
    // over a state the user created deliberately. The row rights itself on the
    // next scan.
    private const int UpdateNotApplicableExitCode = unchecked((int)0x8A15002B);

    public async Task<OperationResult<IReadOnlyList<UpgradableWingetPackage>>> ListUpgradableAsync(
        CancellationToken cancellationToken = default)
    {
        var run = await RunWingetAsync(
            ["upgrade", "--accept-source-agreements", "--disable-interactivity"],
            cancellationToken, UpgradeListTimeout).ConfigureAwait(false);
        if (!run.IsSuccess)
        {
            return OperationResult<IReadOnlyList<UpgradableWingetPackage>>.Failure(
                run.ErrorMessage!, run.ErrorCategory!.Value);
        }

        var (exitCode, output) = run.Value;
        if (exitCode == NoPackagesFoundExitCode)
        {
            return OperationResult<IReadOnlyList<UpgradableWingetPackage>>.Success([]);
        }

        var packages = ParseUpgradeTable(output);
        if (exitCode != 0 && packages.Count == 0)
        {
            return OperationResult<IReadOnlyList<UpgradableWingetPackage>>.Failure(
                FormatExitError("winget upgrade", exitCode, output),
                ErrorCategory.ServiceUnavailable);
        }

        return OperationResult<IReadOnlyList<UpgradableWingetPackage>>.Success(packages);
    }

    public Task<OperationResult<bool>> UpgradeAsync(
        string packageId, CancellationToken cancellationToken = default) =>
        RunPackageOperationAsync(
            packageId,
            ["upgrade", "--id", packageId, "--exact",
             "--accept-package-agreements", "--accept-source-agreements",
             "--silent", "--disable-interactivity"],
            $"winget upgrade {packageId}",
            cancellationToken,
            benignExitCodes: [NoPackagesFoundExitCode, UpdateNotApplicableExitCode]);

    public Task<OperationResult<bool>> InstallAsync(
        string packageId, WingetSource source, CancellationToken cancellationToken = default) =>
        RunPackageOperationAsync(
            packageId,
            ["install", "--id", packageId, "--exact", "--source", SourceName(source),
             "--accept-package-agreements", "--accept-source-agreements",
             "--silent", "--disable-interactivity"],
            $"winget install {packageId}",
            cancellationToken);

    public Task<OperationResult<bool>> UninstallAsync(
        string packageId, WingetSource source, CancellationToken cancellationToken = default) =>
        RunPackageOperationAsync(
            packageId,
            ["uninstall", "--id", packageId, "--exact", "--source", SourceName(source),
             "--silent", "--disable-interactivity"],
            $"winget uninstall {packageId}",
            cancellationToken);

    private async Task<OperationResult<bool>> RunPackageOperationAsync(
        string packageId, string[] arguments, string operationName, CancellationToken cancellationToken,
        int[]? benignExitCodes = null)
    {
        var idError = ValidatePackageId(packageId);
        if (idError is not null)
            return OperationResult<bool>.Failure(idError, ErrorCategory.NotFound);

        var run = await RunWingetAsync(arguments, cancellationToken, OperationTimeout).ConfigureAwait(false);
        if (!run.IsSuccess)
            return OperationResult<bool>.Failure(run.ErrorMessage!, run.ErrorCategory!.Value);

        var (exitCode, output) = run.Value;
        if (exitCode != 0 && benignExitCodes?.Contains(exitCode) != true)
        {
            return OperationResult<bool>.Failure(
                FormatExitError(operationName, exitCode, output),
                ErrorCategory.ServiceUnavailable);
        }

        return OperationResult<bool>.Success(true);
    }

    private static string SourceName(WingetSource source) =>
        source == WingetSource.MsStore ? "msstore" : "winget";

    private static string? ValidatePackageId(string packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            return "Package id is empty.";
        if (packageId.StartsWith('-'))
            return $"Package id '{packageId}' is not valid.";
        if (packageId.Any(c => char.IsControl(c) || char.IsWhiteSpace(c)))
            return $"Package id '{packageId}' is not valid.";
        return null;
    }

    /// <summary>
    /// Parses the fixed-width table of <c>winget upgrade</c> (Name, Id, Version,
    /// Available, Source). The "require explicit targeting" section repeats
    /// header and separator and is parsed the same way. Exposed for tests.
    /// </summary>
    public static IReadOnlyList<UpgradableWingetPackage> ParseUpgradeTable(string output)
    {
        var packages = new List<UpgradableWingetPackage>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var cells in ParseTableCells(output, count => count == 5))
        {
            var id = cells[1];
            if (id.Length == 0 || cells[2].Length == 0 || cells[3].Length == 0)
                continue;
            if (!IsUsableId(id) || !seen.Add(id))
                continue;

            packages.Add(new UpgradableWingetPackage(
                PackageId: id,
                Name: cells[0],
                InstalledVersion: cells[2],
                AvailableVersion: cells[3]));
        }

        return packages.AsReadOnly();
    }

    /// <summary>
    /// Parses the fixed-width table of <c>winget list</c> (Name, Id, Version,
    /// then Available and/or Source depending on the build). Rows whose id is
    /// truncated or is an unmatchable ARP identifier are skipped. Exposed for tests.
    /// </summary>
    public static IReadOnlyList<InstalledWingetPackage> ParseListTable(string output)
    {
        var packages = new List<InstalledWingetPackage>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var cells in ParseTableCells(output, count => count is 3 or 4 or 5))
        {
            var name = cells[0];
            var id = cells[1];
            if (id.Length == 0 || cells[2].Length == 0)
                continue;

            // Rows winget could not correlate to a source carry raw ARP ids
            // ("ARP\Machine\X86\Google Chrome") no install can target; keep the
            // display name so those installs still match the catalog by name.
            var usableId = IsUsableId(id);
            if (!usableId && name.Length == 0)
                continue;
            if (!seen.Add(usableId ? id : $"name:{name}"))
                continue;

            packages.Add(new InstalledWingetPackage(
                PackageId: usableId ? id : string.Empty,
                Version: cells[2],
                Name: name.Length > 0 ? name : null));
        }

        return packages.AsReadOnly();
    }

    /// <summary>
    /// Shared core for winget's localized fixed-width tables: winget always
    /// emits columns in a fixed order and only the header words are localized,
    /// so the header-word start offsets on the line above each all-dashes
    /// separator give the boundaries to slice every following row by. Token
    /// counting cannot work; versions can contain spaces ("&lt; 3.21.0",
    /// "6.6.11 (23272)"). Rows are returned raw; callers validate cells, which
    /// also discards blank lines, trailers, and prose between sections.
    /// </summary>
    private static List<string[]> ParseTableCells(
        string output, Func<int, bool> acceptColumnCount)
    {
        var rows = new List<string[]>();
        string? previousLine = null;
        int[]? columns = null;
        var previousWasEmitted = false;

        foreach (var rawLine in output.Split('\n'))
        {
            // Progress rendering leaves carriage returns and backspaces behind;
            // keep only what would remain on the console line. No Trim; the
            // leading indentation is part of the column layout.
            var line = rawLine.Replace("\b", "", StringComparison.Ordinal).TrimEnd('\r');
            var lastReturn = line.LastIndexOf('\r');
            if (lastReturn >= 0)
                line = line[(lastReturn + 1)..];

            var trimmed = line.Trim();
            if (trimmed.Length >= 5 && trimmed.All(c => c == '-'))
            {
                // The line above a separator is a header, never data. A second
                // section's header was already sliced under the previous
                // section's columns; take it back.
                if (previousWasEmitted)
                    rows.RemoveAt(rows.Count - 1);

                var starts = previousLine is null ? null : FindColumnStarts(previousLine);
                columns = starts is not null && acceptColumnCount(starts.Length) ? starts : null;
                previousLine = line;
                previousWasEmitted = false;
                continue;
            }

            previousLine = line;
            previousWasEmitted = false;
            if (columns is null)
                continue;

            var cells = new string[columns.Length];
            for (var i = 0; i < columns.Length; i++)
                cells[i] = Slice(line, columns[i], i + 1 < columns.Length ? columns[i + 1] : int.MaxValue);
            rows.Add(cells);
            previousWasEmitted = true;
        }

        return rows;
    }

    /// <summary>
    /// A console-width-truncated id (ellipsis) cannot be targeted; an id with
    /// interior whitespace is either a misaligned row (wide glyphs in the name
    /// shift every column) or a fragment of a trailer/prose line.
    /// </summary>
    private static bool IsUsableId(string id) =>
        !id.Contains('…', StringComparison.Ordinal) && ValidatePackageId(id) is null;

    /// <summary>Start offsets of the header words; null when the line has none.</summary>
    private static int[]? FindColumnStarts(string header)
    {
        var starts = new List<int>();
        for (var i = 0; i < header.Length; i++)
        {
            if (header[i] != ' ' && (i == 0 || header[i - 1] == ' '))
                starts.Add(i);
        }

        return starts.Count > 0 ? [.. starts] : null;
    }

    private static string Slice(string line, int start, int end)
    {
        if (start >= line.Length)
            return string.Empty;
        return line[start..Math.Min(end, line.Length)].Trim();
    }

    private static async Task<OperationResult<(int ExitCode, string Output)>> RunWingetAsync(
        string[] arguments, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ResolveWingetPath(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return OperationResult<(int, string)>.Failure(
                    "winget could not be started.", ErrorCategory.ServiceUnavailable);
            }

            using var timeoutCts = timeout is { } limit
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : null;
            timeoutCts?.CancelAfter(timeout!.Value);
            var effectiveToken = timeoutCts?.Token ?? cancellationToken;

            var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

            try
            {
                await process.WaitForExitAsync(effectiveToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) { }

                // A timeout is a winget failure, not a caller cancellation; only
                // the caller's own token propagates as OperationCanceledException.
                if (!cancellationToken.IsCancellationRequested)
                {
                    return OperationResult<(int, string)>.Failure(
                        $"winget did not respond within {timeout!.Value.TotalSeconds:F0} seconds and was terminated.",
                        ErrorCategory.ServiceUnavailable);
                }

                throw;
            }

            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            var combined = string.IsNullOrWhiteSpace(stderr) ? stdout : $"{stdout}\n{stderr}";

            return OperationResult<(int, string)>.Success((process.ExitCode, combined));
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return OperationResult<(int, string)>.Failure(
                "winget is not available. Install 'App Installer' from the Microsoft Store.",
                ErrorCategory.ServiceUnavailable, ex);
        }
    }

    private static string ResolveWingetPath()
    {
        // The per-user app execution alias survives elevation because the token
        // is the same user's; fall back to PATH resolution via CreateProcess.
        var alias = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "WindowsApps", "winget.exe");
        return File.Exists(alias) ? alias : "winget.exe";
    }

    private static string FormatExitError(string operationName, int exitCode, string output)
    {
        var tail = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Length > 0 && !line.All(c => c is '-' or '\\' or '|' or '/' or ' '))
            .TakeLast(OutputTailLines)
            .ToList();

        var detail = tail.Count > 0 ? $" {string.Join(" ", tail)}" : string.Empty;
        return $"{operationName} failed (0x{exitCode:X8}).{detail}";
    }
}
