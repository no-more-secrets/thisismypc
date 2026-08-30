using System.Diagnostics;
using System.Text;
using System.Text.Json;
using ThisIsMyPC.Core.Packages;
using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Interop.Win32.Packages;

/// <summary>
/// Runs winget.exe with explicit argument lists (no shell, no injection surface).
/// Flag recipe follows CTT winutil (MIT): silent, agreements pre-accepted,
/// interactivity disabled. Installed-state detection uses <c>winget export</c>
/// because its JSON output is stable, unlike the localized fixed-width tables
/// of <c>winget list</c>.
/// </summary>
public sealed class WingetService : IWingetService
{
    // Keep only the last lines of process output for error messages — winget
    // renders progress spinners that make full output enormous.
    private const int OutputTailLines = 12;

    // The version probe gates module availability during app startup and must
    // never hang the shell; export feeds the scan. Installs get no artificial
    // deadline — a large package on a slow line legitimately takes a long time.
    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan ExportTimeout = TimeSpan.FromMinutes(3);

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
        var exportPath = Path.Combine(
            Path.GetTempPath(), $"tipc-winget-export-{Guid.NewGuid():N}.json");

        try
        {
            var run = await RunWingetAsync(
                ["export", "-o", exportPath, "--accept-source-agreements", "--disable-interactivity"],
                cancellationToken, ExportTimeout).ConfigureAwait(false);
            if (!run.IsSuccess)
            {
                return OperationResult<IReadOnlyList<InstalledWingetPackage>>.Failure(
                    run.ErrorMessage!, run.ErrorCategory!.Value);
            }

            // winget export exits non-zero when some installed packages have no
            // known source, but still writes every package it could resolve —
            // a usable file trumps the exit code.
            if (!File.Exists(exportPath))
            {
                return OperationResult<IReadOnlyList<InstalledWingetPackage>>.Failure(
                    FormatExitError("winget export", run.Value.ExitCode, run.Value.Output),
                    ErrorCategory.ServiceUnavailable);
            }

            var packages = ParseExportFile(exportPath);

            // A non-zero exit with a populated file is the normal "some packages
            // have no known source" case. Non-zero AND empty means the export as
            // a whole failed (e.g. every source unreachable) — report that rather
            // than presenting "nothing installed" as known state.
            if (run.Value.ExitCode != 0 && packages.Count == 0)
            {
                return OperationResult<IReadOnlyList<InstalledWingetPackage>>.Failure(
                    FormatExitError("winget export", run.Value.ExitCode, run.Value.Output),
                    ErrorCategory.ServiceUnavailable);
            }

            return OperationResult<IReadOnlyList<InstalledWingetPackage>>.Success(packages);
        }
        catch (JsonException ex)
        {
            return OperationResult<IReadOnlyList<InstalledWingetPackage>>.Failure(
                $"Could not parse winget export output: {ex.Message}",
                ErrorCategory.ServiceUnavailable, ex);
        }
        finally
        {
            try
            {
                File.Delete(exportPath);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

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
        string packageId, string[] arguments, string operationName, CancellationToken cancellationToken)
    {
        var idError = ValidatePackageId(packageId);
        if (idError is not null)
            return OperationResult<bool>.Failure(idError, ErrorCategory.NotFound);

        var run = await RunWingetAsync(arguments, cancellationToken).ConfigureAwait(false);
        if (!run.IsSuccess)
            return OperationResult<bool>.Failure(run.ErrorMessage!, run.ErrorCategory!.Value);

        var (exitCode, output) = run.Value;
        if (exitCode != 0)
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

    private static IReadOnlyList<InstalledWingetPackage> ParseExportFile(string path)
    {
        using var stream = File.OpenRead(path);
        return ParseExport(stream);
    }

    /// <summary>Parses a <c>winget export</c> JSON document. Exposed for tests.</summary>
    public static IReadOnlyList<InstalledWingetPackage> ParseExport(Stream stream)
    {
        using var document = JsonDocument.Parse(stream);

        var packages = new List<InstalledWingetPackage>();
        if (document.RootElement.TryGetProperty("Sources", out var sources)
            && sources.ValueKind == JsonValueKind.Array)
        {
            foreach (var sourceElement in sources.EnumerateArray())
            {
                if (!sourceElement.TryGetProperty("Packages", out var packageArray)
                    || packageArray.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var package in packageArray.EnumerateArray())
                {
                    if (!package.TryGetProperty("PackageIdentifier", out var id)
                        || id.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    string? version = null;
                    if (package.TryGetProperty("Version", out var versionElement)
                        && versionElement.ValueKind == JsonValueKind.String)
                    {
                        version = versionElement.GetString();
                    }

                    packages.Add(new InstalledWingetPackage(id.GetString()!, version));
                }
            }
        }

        return packages.AsReadOnly();
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

                // A timeout is a winget failure, not a caller cancellation — only
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
