using System.Text.Json;
using ThisIsMyPC.Core.Changes;
using ThisIsMyPC.Core.Hardware;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Modules.Hardware.Cooling;

public sealed record FanControlSavedProfile(string Name, byte[] Bytes, FanControlProfileDocument Document);

/// <summary>Reads and changes profiles beside the detected FanControl installation, without elevation.</summary>
public sealed class FanControlProfileStore(IHardwareFactsProvider facts, IComparedFileDeletionService? deletion = null)
{
    public const string Missing = "<missing>";
    public const string SettingPrefix = "fancontrol-profile:";
    private readonly IHardwareFactsProvider _facts = facts ?? throw new ArgumentNullException(nameof(facts));

    public static bool IsProfileChange(ChangeDescriptor change) => change.ModuleId == CoolingModule.ModuleName
        && change.SettingId.StartsWith(SettingPrefix, StringComparison.Ordinal)
        && change.ValueType == ChangeValueType.File_Content && change.Enforcement is null;

    public async Task<OperationResult<IReadOnlyList<string>>> ListAsync()
    {
        try
        {
            var directory = await DirectoryAsync().ConfigureAwait(false);
            IReadOnlyList<string> names = await Task.Run(() => Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
                .Where(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
                .Select(Path.GetFileName).OfType<string>().Order(StringComparer.OrdinalIgnoreCase).ToArray()).ConfigureAwait(false);
            return OperationResult<IReadOnlyList<string>>.Success(names);
        }
        catch (Exception ex) when (Expected(ex)) { return Fail<IReadOnlyList<string>>(ex); }
    }

    public async Task<OperationResult<FanControlSavedProfile>> ReadAsync(string name)
    {
        try
        {
            var path = ProfilePath(await DirectoryAsync().ConfigureAwait(false), name);
            var bytes = await Task.Run(() => ReadLocked(path)).ConfigureAwait(false);
            return OperationResult<FanControlSavedProfile>.Success(new(name, bytes, FanControlProfileDocument.Parse(bytes)));
        }
        catch (Exception ex) when (Expected(ex)) { return Fail<FanControlSavedProfile>(ex); }
    }

    /// <summary>Captures exact bytes for undo. Copies never overwrite an existing profile.</summary>
    public async Task<OperationResult<ChangeGroup>> BuildChangeAsync(FanControlSavedProfile source, string name, bool overwriteSource = false)
    {
        ArgumentNullException.ThrowIfNull(source);
        try
        {
            var directory = await DirectoryAsync().ConfigureAwait(false);
            var sourcePath = ProfilePath(directory, source.Name);
            var target = ProfilePath(directory, name);
            var after = source.Document.Serialize();
            var same = string.Equals(sourcePath, target, StringComparison.OrdinalIgnoreCase);
            if (same && !overwriteSource)
                throw new IOException("Choose a new profile name, or allow replacing the selected profile.");
            var before = await Task.Run(() =>
            {
                var live = ReadLocked(sourcePath);
                if (!live.AsSpan().SequenceEqual(source.Bytes))
                    throw new IOException("FanControl changed the selected profile. Reload it before saving.");
                if (!same && File.Exists(target))
                    throw new IOException("That profile name already exists. Choose a new name.");
                return same ? Convert.ToBase64String(live) : Missing;
            }).ConfigureAwait(false);
            if (same && after.AsSpan().SequenceEqual(source.Bytes))
                throw new IOException("There are no profile edits to save.");
            return OperationResult<ChangeGroup>.Success(FanControlProfileChangeFactory.Create(name, target, before, after));
        }
        catch (Exception ex) when (Expected(ex)) { return Fail<ChangeGroup>(ex); }
    }

    /// <summary>Applies the descriptor as supplied. The history pipeline swaps values for undo.</summary>
    public async Task<OperationResult<bool>> ApplyAsync(ChangeDescriptor change)
    {
        try
        {
            if (!IsProfileChange(change))
                throw new FormatException("This is not a FanControl profile change.");
            var name = change.SettingId[SettingPrefix.Length..];
            var path = ProfilePath(await DirectoryAsync().ConfigureAwait(false), name);
            if (!string.Equals(path, change.SystemLocation, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The FanControl installation changed. Stage this profile again.");
            var before = Decode(change.BeforeValue);
            var after = Decode(change.AfterValue);
            if (before is null && after is null)
                throw new FormatException("The profile change has no content.");
            if (after is null)
                return deletion is null
                    ? OperationResult<bool>.Failure("Safe profile deletion is unavailable.", ErrorCategory.ServiceUnavailable)
                    : await Task.Run(() => deletion.DeleteIfMatches(path, before!)).ConfigureAwait(false);
            if (after is not null) FanControlProfileDocument.Parse(after);
            await Task.Run(() => WriteCompared(path, before, after)).ConfigureAwait(false);
            return OperationResult<bool>.Success(true);
        }
        catch (Exception ex) when (Expected(ex)) { return Fail<bool>(ex); }
    }

    private async Task<string> DirectoryAsync()
    {
        var snapshot = await _facts.GetAsync().ConfigureAwait(false);
        var executable = snapshot.LaunchPathOf(CompanionApp.FanControl);
        if (string.IsNullOrWhiteSpace(executable) || !Path.IsPathFullyQualified(executable)
            || !string.Equals(Path.GetFileName(executable), "FanControl.exe", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Install FanControl and save a configuration there before editing profiles here.");
        var directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(executable))!, "Configurations");
        CheckAncestors(directory);
        return directory;
    }

    private static string ProfilePath(string directory, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120 || name != Path.GetFileName(name)
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith(' ') || name.EndsWith('.')
            || !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Use a profile name ending in .json, without folders or special filename characters.");
        var stem = Path.GetFileNameWithoutExtension(name).Split('.')[0];
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
                && char.IsDigit(stem[3])))
            throw new FormatException("Choose a different profile name.");
        var path = Path.GetFullPath(Path.Combine(directory, name));
        CheckAncestors(path, allowMissingLeaf: true);
        return path;
    }

    private static void CheckAncestors(string path, bool allowMissingLeaf = false)
    {
        var current = path;
        var leaf = true;
        while (!string.IsNullOrEmpty(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Linked FanControl folders and profiles cannot be edited here.");
            }
            catch (FileNotFoundException) when (leaf && allowMissingLeaf) { }
            leaf = false;
            current = Path.GetDirectoryName(current);
        }
    }

    private static byte[] ReadLocked(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return ReadBounded(stream);
    }

    private static byte[] ReadBounded(FileStream stream)
    {
        if (stream.Length > FanControlProfileDocument.MaximumBytes)
            throw new IOException("The profile is too large to edit.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static byte[]? Decode(string? value)
    {
        if (value == Missing) return null;
        if (value is null || value.Length > FanControlProfileDocument.MaximumBytes * 2)
            throw new FormatException("The saved profile content is invalid.");
        var bytes = Convert.FromBase64String(value);
        if (bytes.Length > FanControlProfileDocument.MaximumBytes)
            throw new FormatException("The saved profile content is too large.");
        return bytes;
    }

    private static void WriteCompared(string path, byte[]? before, byte[]? after)
    {
        CheckAncestors(path, allowMissingLeaf: true);
        if (before is null)
        {
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var created = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    created.Write(after!);
                    created.Flush(flushToDisk: true);
                }
                File.Move(temporary, path, overwrite: false);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return;
        }
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var actual = ReadBounded(stream);
            if (!actual.AsSpan().SequenceEqual(before))
                throw new IOException("The profile changed since this edit was saved or staged. Reload it before trying again.");
            if (after is not null)
            {
                try
                {
                    stream.Position = 0;
                    stream.Write(after);
                    stream.SetLength(after.Length);
                    stream.Flush(flushToDisk: true);
                }
                catch (IOException)
                {
                    stream.Position = 0;
                    stream.Write(actual);
                    stream.SetLength(actual.Length);
                    stream.Flush(flushToDisk: true);
                    throw;
                }
                return;
            }
        }
    }

    private static bool Expected(Exception ex) => ex is IOException or UnauthorizedAccessException or FormatException
        or JsonException or InvalidOperationException or ArgumentException or OverflowException;
    private static OperationResult<T> Fail<T>(Exception ex) => OperationResult<T>.Failure(ex.Message,
        ex is UnauthorizedAccessException ? ErrorCategory.AccessDenied : ErrorCategory.ServiceUnavailable, ex);
}
