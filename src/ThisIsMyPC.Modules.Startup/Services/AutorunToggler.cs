using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;
using ThisIsMyPC.Modules.Startup.Models;

namespace ThisIsMyPC.Modules.Startup.Services;

/// <summary>
/// Enables and disables autostart items the way Autoruns does, so the two
/// tools read each other's state: registry values and keys move into an
/// AutorunsDisabled sibling, startup files into an AutorunsDisabled
/// subfolder, services and drivers get Start=4 with the old Start kept in an
/// AutorunsDisabled value, and tasks flip through the scheduler.
/// </summary>
/// <remarks>
/// Every move is idempotent (an item already at its destination is success)
/// and never overwrites. When the live item and its parked twin both exist,
/// the program re-registered itself after the user switched it off: with a
/// snapshot of the live copy in hand, switching off purges that copy and
/// leaves the parked twin as the record of the user's choice; enabling puts
/// the copy back from the snapshot. Without a snapshot the twin case refuses,
/// because the pending pipeline's undo could not bring the copy back.
/// </remarks>
public sealed class AutorunToggler
{
    public const string ServiceStartValue = "Start";
    public const int ServiceStartDisabled = 4;

    private readonly IRegistryService _registry;
    private readonly IStartupFolderService _folders;
    private readonly IScheduledTaskService _tasks;

    public AutorunToggler(IRegistryService registry, IStartupFolderService folders, IScheduledTaskService tasks)
    {
        _registry = registry;
        _folders = folders;
        _tasks = tasks;
    }

    public OperationResult<bool> Apply(AutorunTarget target, bool enable, AutorunSnapshot? snapshot = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.Kind switch
        {
            AutorunItemKind.RegistryValue => MoveValue(target, enable, snapshot),
            AutorunItemKind.RegistryKey => MoveKey(target, enable, snapshot),
            AutorunItemKind.StartupFile => MoveFile(target, enable, snapshot),
            AutorunItemKind.ScheduledTask => _tasks.SetEnabled(target.Location, enable),
            AutorunItemKind.Service => SetServiceStart(target, enable),
            _ => OperationResult<bool>.Failure($"Unknown autorun kind {target.Kind}", ErrorCategory.NotFound),
        };
    }

    private static string TwinMessage(string destination)
        => $"{destination} already exists, so the move would overwrite it. Scan again so the re-registered copy can be removed instead.";

    // ---- Registry values ----

    private OperationResult<bool> MoveValue(AutorunTarget target, bool enable, AutorunSnapshot? snapshot)
    {
        var live = target.Location;
        var parked = target.DisabledContainer;
        var atLive = _registry.ValueExists(live, target.Name) is { IsSuccess: true, Value: true };
        var atParked = _registry.ValueExists(parked, target.Name) is { IsSuccess: true, Value: true };

        if (snapshot is { Kind: AutorunItemKind.RegistryValue, Values.Count: 1 })
        {
            // Re-registered twin: purge the live copy, or put it back from the snapshot.
            if (!enable)
                return atLive ? _registry.DeleteValue(live, target.Name) : OperationResult<bool>.Success(true);
            return atLive ? OperationResult<bool>.Success(true) : _registry.WriteValue(live, target.Name, snapshot.Values[0].Value);
        }

        var (fromKey, toKey, atSource, atDestination) = enable ? (parked, live, atParked, atLive) : (live, parked, atLive, atParked);
        if (!atSource)
        {
            return atDestination
                ? OperationResult<bool>.Success(true)
                : OperationResult<bool>.Failure($@"{fromKey}\{target.Name} is no longer there.", ErrorCategory.NotFound);
        }
        if (atDestination)
            return OperationResult<bool>.Failure(TwinMessage($@"{toKey}\{target.Name}"), ErrorCategory.ServiceUnavailable);

        var read = _registry.ReadValue(fromKey, target.Name);
        if (!read.IsSuccess || read.Value is null)
            return OperationResult<bool>.Failure(read.ErrorMessage ?? "Read failed", read.ErrorCategory ?? ErrorCategory.ServiceUnavailable);

        var write = _registry.WriteValue(toKey, target.Name, read.Value);
        if (!write.IsSuccess)
            return write;

        return _registry.DeleteValue(fromKey, target.Name);
    }

    // ---- Registry keys ----

    private OperationResult<bool> MoveKey(AutorunTarget target, bool enable, AutorunSnapshot? snapshot)
    {
        var live = target.EnabledPath;
        var parked = target.DisabledPath!;
        var atLive = _registry.KeyExists(live) is { IsSuccess: true, Value: true };
        var atParked = _registry.KeyExists(parked) is { IsSuccess: true, Value: true };

        if (snapshot is { Kind: AutorunItemKind.RegistryKey })
        {
            if (!enable)
                return atLive ? _registry.DeleteKey(live, recursive: true) : OperationResult<bool>.Success(true);
            return atLive ? OperationResult<bool>.Success(true) : WriteTree(live, snapshot.Values);
        }

        var (from, to, atSource, atDestination) = enable ? (parked, live, atParked, atLive) : (live, parked, atLive, atParked);
        if (!atSource)
        {
            return atDestination
                ? OperationResult<bool>.Success(true)
                : OperationResult<bool>.Failure($"{from} is no longer there.", ErrorCategory.NotFound);
        }
        if (atDestination)
            return OperationResult<bool>.Failure(TwinMessage(to), ErrorCategory.ServiceUnavailable);

        var copy = CopyTree(from, to);
        if (!copy.IsSuccess)
            return copy;

        return _registry.DeleteKey(from, recursive: true);
    }

    /// <summary>Copies every value (with its type) and subkey; the source stays until the copy is complete.</summary>
    private OperationResult<bool> CopyTree(string from, string to)
    {
        var create = _registry.CreateKey(to);
        if (!create.IsSuccess)
            return create;

        if (_registry.EnumerateValues(from) is { IsSuccess: true, Value: { } names })
        {
            foreach (var name in names)
            {
                var read = _registry.ReadValue(from, name);
                if (!read.IsSuccess || read.Value is null)
                    return OperationResult<bool>.Failure(read.ErrorMessage ?? $@"Could not read {from}\{name}", read.ErrorCategory ?? ErrorCategory.ServiceUnavailable);
                var write = _registry.WriteValue(to, name, read.Value);
                if (!write.IsSuccess)
                    return write;
            }
        }

        if (_registry.EnumerateSubKeys(from) is { IsSuccess: true, Value: { } subKeys })
        {
            foreach (var subKey in subKeys)
            {
                var nested = CopyTree($@"{from}\{subKey}", $@"{to}\{subKey}");
                if (!nested.IsSuccess)
                    return nested;
            }
        }

        return OperationResult<bool>.Success(true);
    }

    /// <summary>Materializes a snapshotted key tree at <paramref name="root"/>.</summary>
    private OperationResult<bool> WriteTree(string root, IReadOnlyList<AutorunSnapshotValue> values)
    {
        var create = _registry.CreateKey(root);
        if (!create.IsSuccess)
            return create;
        foreach (var value in values)
        {
            var keyPath = value.SubPath.Length == 0 ? root : $@"{root}\{value.SubPath}";
            var write = _registry.WriteValue(keyPath, value.Name, value.Value);
            if (!write.IsSuccess)
                return write;
        }
        return OperationResult<bool>.Success(true);
    }

    // ---- Startup files ----

    private OperationResult<bool> MoveFile(AutorunTarget target, bool enable, AutorunSnapshot? snapshot)
    {
        var live = target.EnabledPath;
        var parked = target.DisabledPath!;

        if (snapshot is { Kind: AutorunItemKind.StartupFile, FileBase64: { } base64 })
        {
            return enable
                ? _folders.Restore(live, Convert.FromBase64String(base64))
                : _folders.Delete(live);
        }

        return enable ? _folders.Move(parked, live) : _folders.Move(live, parked);
    }

    // ---- Services and drivers ----

    /// <summary>
    /// The service key is Location\Name. Disable parks the current Start in the
    /// AutorunsDisabled value first, so enable can put it back; a service that is
    /// already Start=4 without that value was disabled by something else, and
    /// both directions refuse rather than record a change with no reverse.
    /// </summary>
    private OperationResult<bool> SetServiceStart(AutorunTarget target, bool enable)
    {
        var key = target.EnabledPath;
        var start = _registry.ReadDWord(key, ServiceStartValue);
        if (!start.IsSuccess)
            return OperationResult<bool>.Failure($"{target.Name} has no Start value; it is not a service or driver.", ErrorCategory.NotFound);

        var saved = _registry.ReadDWord(key, AutorunTarget.DisabledName);
        if (start.Value == ServiceStartDisabled && !saved.IsSuccess)
        {
            return OperationResult<bool>.Failure(
                $"{target.Name} is already disabled by something other than Autoruns or this app, so its old start type is unknown. Set it in Windows Services.",
                ErrorCategory.NotFound);
        }

        if (!enable)
        {
            if (start.Value == ServiceStartDisabled)
                return OperationResult<bool>.Success(true);
            var keep = _registry.WriteDWord(key, AutorunTarget.DisabledName, start.Value);
            if (!keep.IsSuccess)
                return keep;
            return _registry.WriteDWord(key, ServiceStartValue, ServiceStartDisabled);
        }

        if (!saved.IsSuccess)
            return OperationResult<bool>.Success(true); // not disabled at all

        var restore = _registry.WriteDWord(key, ServiceStartValue, saved.Value);
        if (!restore.IsSuccess)
            return restore;
        return _registry.DeleteValue(key, AutorunTarget.DisabledName);
    }
}
