using System.Runtime.InteropServices;
using System.Xml.Linq;
using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Core.Services;

namespace ThisIsMyPC.Interop.Com.Tasks;

/// <summary>
/// ITaskService COM wrapper via raw vtable calls (NativeAOT-safe, no COM
/// wrappers; same style as ContextMenuProbe/StartupFolderService).
///
/// Deliberately minimal interface surface: task metadata (author, description,
/// triggers) is read from IRegisteredTask::get_Xml and parsed with XDocument
/// instead of walking ITaskDefinition/IRegistrationInfo/ITrigger vtables.
/// </summary>
public sealed partial class ScheduledTaskService : IScheduledTaskService
{
    // IUnknown vtable indices
    private const int VtblQueryInterface = 0;
    private const int VtblRelease = 2;

    // All Task Scheduler interfaces derive from IDispatch: IUnknown(0-2) + IDispatch(3-6).
    // Indices below follow taskschd.idl method order after slot 6.
    private const int VtblTaskServiceGetFolder = 7;
    private const int VtblTaskServiceConnect = 10;

    private const int VtblFolderGetFolders = 10;
    private const int VtblFolderGetTask = 13;
    private const int VtblFolderGetTasks = 14;

    private const int VtblCollectionGetCount = 7;   // ITaskFolderCollection / IRegisteredTaskCollection
    private const int VtblCollectionGetItem = 8;    // VARIANT index, 1-based

    private const int VtblTaskGetName = 7;
    private const int VtblTaskGetPath = 8;
    private const int VtblTaskGetEnabled = 10;
    private const int VtblTaskPutEnabled = 11;
    private const int VtblTaskGetLastRunTime = 15;
    private const int VtblTaskGetLastTaskResult = 16;
    private const int VtblTaskGetXml = 20;

    private const uint CLSCTX_INPROC_SERVER = 1;
    private const uint COINIT_APARTMENTTHREADED = 0x2;
    private const int TASK_ENUM_HIDDEN = 1;
    private const short VARIANT_TRUE = -1;
    private const ushort VT_EMPTY = 0;
    private const ushort VT_I4 = 3;

    private const int E_ACCESSDENIED = unchecked((int)0x80070005);
    private const int HR_FILE_NOT_FOUND = unchecked((int)0x80070002);
    private const int HR_PATH_NOT_FOUND = unchecked((int)0x80070003);

    private static readonly Guid CLSID_TaskScheduler = new("0F87369F-A4E5-4CFC-BD3E-73E6154572DD");
    private static readonly Guid IID_ITaskService = new("2FABA4C7-4DA9-4013-9697-20CC3FD40F85");

    [StructLayout(LayoutKind.Sequential)]
    private struct Variant
    {
        public ushort vt;
        public ushort reserved1;
        public ushort reserved2;
        public ushort reserved3;
        public long value;
    }

    public OperationResult<IReadOnlyList<ScheduledTaskInfo>> EnumerateAll() =>
        WithRootFolder<IReadOnlyList<ScheduledTaskInfo>>((root, _) =>
        {
            var tasks = new List<ScheduledTaskInfo>();
            WalkFolder(root, tasks);
            return OperationResult<IReadOnlyList<ScheduledTaskInfo>>.Success(tasks);
        });

    public OperationResult<ScheduledTaskInfo> Query(string taskPath) =>
        WithRootFolder((root, _) =>
        {
            var (hr, pTask) = GetTaskFromFolder(root, taskPath);
            if (hr < 0 || pTask == 0)
                return MapHResult<ScheduledTaskInfo>(hr, taskPath, "query");
            try
            {
                var info = ReadTask(pTask);
                return info is null
                    ? OperationResult<ScheduledTaskInfo>.Failure($"Failed to read task '{taskPath}'.", ErrorCategory.ServiceUnavailable)
                    : OperationResult<ScheduledTaskInfo>.Success(info);
            }
            finally
            {
                Release(pTask);
            }
        });

    public unsafe OperationResult<bool> SetEnabled(string taskPath, bool enabled) =>
        WithRootFolder((root, _) =>
        {
            var (hr, pTask) = GetTaskFromFolder(root, taskPath);
            if (hr < 0 || pTask == 0)
                return MapHResult<bool>(hr, taskPath, "open");
            try
            {
                var vtable = *(nint**)pTask;
                var putEnabledFn = (delegate* unmanaged[Stdcall]<nint, short, int>)vtable[VtblTaskPutEnabled];
                hr = putEnabledFn(pTask, enabled ? VARIANT_TRUE : (short)0);
                if (hr < 0)
                    return MapHResult<bool>(hr, taskPath, enabled ? "enable" : "disable");
                return OperationResult<bool>.Success(true);
            }
            finally
            {
                Release(pTask);
            }
        });

    /// <summary>Connects to the Task Scheduler and hands the root folder to the action, balancing COM init and releases.</summary>
    private unsafe OperationResult<T> WithRootFolder<T>(Func<nint, nint, OperationResult<T>> action)
    {
        nint pService = 0;
        nint pRoot = 0;
        var needUninit = false;

        try
        {
            // The Variant* calli signatures below encode the x64 convention for
            // by-value VARIANT parameters (16-byte aggregates pass by reference).
            // ARM64/x86 pass them differently and would corrupt arguments.
            if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
            {
                return OperationResult<T>.Failure(
                    $"Task Scheduler interop is not supported on {RuntimeInformation.ProcessArchitecture} yet.",
                    ErrorCategory.ServiceUnavailable);
            }

            needUninit = CoInitializeEx(0, COINIT_APARTMENTTHREADED) >= 0;

            var iid = IID_ITaskService;
            var hr = CoCreateInstance(in CLSID_TaskScheduler, 0, CLSCTX_INPROC_SERVER, in iid, out pService);
            if (hr < 0)
                return OperationResult<T>.Failure($"Task Scheduler service unavailable (0x{hr:X8}).", ErrorCategory.ServiceUnavailable);

            var vtable = *(nint**)pService;
            var connectFn = (delegate* unmanaged[Stdcall]<nint, Variant*, Variant*, Variant*, Variant*, int>)vtable[VtblTaskServiceConnect];
            var empty = default(Variant);
            empty.vt = VT_EMPTY;
            var e1 = empty; var e2 = empty; var e3 = empty; var e4 = empty;
            hr = connectFn(pService, &e1, &e2, &e3, &e4);
            if (hr < 0)
                return OperationResult<T>.Failure($"Failed to connect to Task Scheduler (0x{hr:X8}).", ErrorCategory.ServiceUnavailable);

            var rootPath = Marshal.StringToBSTR("\\");
            try
            {
                var getFolderFn = (delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)vtable[VtblTaskServiceGetFolder];
                nint root;
                hr = getFolderFn(pService, rootPath, &root);
                if (hr < 0 || root == 0)
                    return OperationResult<T>.Failure($"Failed to open Task Scheduler root folder (0x{hr:X8}).", ErrorCategory.ServiceUnavailable);
                pRoot = root;
            }
            finally
            {
                Marshal.FreeBSTR(rootPath);
            }

            return action(pRoot, pService);
        }
        catch (Exception ex)
        {
            return OperationResult<T>.Failure($"Unexpected Task Scheduler error: {ex.Message}", ErrorCategory.ServiceUnavailable, ex);
        }
        finally
        {
            if (pRoot != 0)
                Release(pRoot);
            if (pService != 0)
                Release(pService);
            if (needUninit)
                CoUninitialize();
        }
    }

    private unsafe void WalkFolder(nint pFolder, List<ScheduledTaskInfo> results)
    {
        var vtable = *(nint**)pFolder;

        // Tasks in this folder (including hidden)
        var getTasksFn = (delegate* unmanaged[Stdcall]<nint, int, nint*, int>)vtable[VtblFolderGetTasks];
        nint pTasks;
        if (getTasksFn(pFolder, TASK_ENUM_HIDDEN, &pTasks) >= 0 && pTasks != 0)
        {
            try
            {
                ForEachCollectionItem(pTasks, pItem =>
                {
                    var info = ReadTask(pItem);
                    if (info is not null)
                        results.Add(info);
                });
            }
            finally
            {
                Release(pTasks);
            }
        }

        // Recurse subfolders
        var getFoldersFn = (delegate* unmanaged[Stdcall]<nint, int, nint*, int>)vtable[VtblFolderGetFolders];
        nint pFolders;
        if (getFoldersFn(pFolder, 0, &pFolders) >= 0 && pFolders != 0)
        {
            try
            {
                ForEachCollectionItem(pFolders, pSub => WalkFolder(pSub, results));
            }
            finally
            {
                Release(pFolders);
            }
        }
    }

    /// <summary>Iterates an ITaskFolderCollection/IRegisteredTaskCollection (1-based VARIANT index) and releases each item.</summary>
    private static unsafe void ForEachCollectionItem(nint pCollection, Action<nint> visit)
    {
        var vtable = *(nint**)pCollection;
        var getCountFn = (delegate* unmanaged[Stdcall]<nint, int*, int>)vtable[VtblCollectionGetCount];
        int count;
        if (getCountFn(pCollection, &count) < 0)
            return;

        var getItemFn = (delegate* unmanaged[Stdcall]<nint, Variant*, nint*, int>)vtable[VtblCollectionGetItem];
        for (var i = 1; i <= count; i++)
        {
            var index = default(Variant);
            index.vt = VT_I4;
            index.value = i;
            nint pItem;
            if (getItemFn(pCollection, &index, &pItem) < 0 || pItem == 0)
                continue;
            try
            {
                visit(pItem);
            }
            finally
            {
                Release(pItem);
            }
        }
    }

    private static unsafe (int Hr, nint Task) GetTaskFromFolder(nint pFolder, string taskPath)
    {
        var vtable = *(nint**)pFolder;
        var getTaskFn = (delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)vtable[VtblFolderGetTask];
        var bstrPath = Marshal.StringToBSTR(taskPath);
        try
        {
            nint pTask;
            var hr = getTaskFn(pFolder, bstrPath, &pTask);
            return (hr, hr >= 0 ? pTask : 0);
        }
        finally
        {
            Marshal.FreeBSTR(bstrPath);
        }
    }

    private static unsafe ScheduledTaskInfo? ReadTask(nint pTask)
    {
        try
        {
            var vtable = *(nint**)pTask;

            var name = ReadBstr(pTask, vtable[VtblTaskGetName]);
            var path = ReadBstr(pTask, vtable[VtblTaskGetPath]);
            if (name is null || path is null)
                return null;

            short enabledRaw = VARIANT_TRUE;
            var getEnabledFn = (delegate* unmanaged[Stdcall]<nint, short*, int>)vtable[VtblTaskGetEnabled];
            getEnabledFn(pTask, &enabledRaw);

            double lastRunRaw = 0;
            var getLastRunFn = (delegate* unmanaged[Stdcall]<nint, double*, int>)vtable[VtblTaskGetLastRunTime];
            getLastRunFn(pTask, &lastRunRaw);
            // OADate 0 (1899-12-30) marks "never ran"
            DateTime? lastRun = lastRunRaw > 1 ? DateTime.FromOADate(lastRunRaw) : null;

            var lastResult = 0;
            var getLastResultFn = (delegate* unmanaged[Stdcall]<nint, int*, int>)vtable[VtblTaskGetLastTaskResult];
            getLastResultFn(pTask, &lastResult);

            var xml = ReadBstr(pTask, vtable[VtblTaskGetXml]);
            var definition = xml is null ? TaskDefinition.Empty : ParseDefinitionXml(xml);

            return new ScheduledTaskInfo(
                name, path, ResolveIndirect(definition.Author), ResolveIndirect(definition.Description), definition.TriggerTypes,
                lastRun, lastResult, enabledRaw != 0,
                definition.Command, definition.Arguments, definition.ComHandlerClsid, definition.WorkingDirectory);
        }
        catch
        {
            return null; // one unreadable task must not abort the walk
        }
    }

    /// <summary>What the definition XML says about a task; every field null or empty when the XML does not say.</summary>
    public sealed record TaskDefinition(
        string? Author,
        string? Description,
        IReadOnlyList<string> TriggerTypes,
        string? Command,
        string? Arguments,
        string? ComHandlerClsid,
        string? WorkingDirectory = null)
    {
        public static TaskDefinition Empty { get; } = new(null, null, [], null, null, null);
    }

    public static TaskDefinition ParseDefinitionXml(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var registration = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "RegistrationInfo");
            var triggers = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Triggers")
                ?.Elements().Select(e => e.Name.LocalName).Distinct().ToList();

            var actions = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Actions");
            var exec = actions?.Elements().FirstOrDefault(e => e.Name.LocalName == "Exec");
            var comHandler = actions?.Elements().FirstOrDefault(e => e.Name.LocalName == "ComHandler");
            return new TaskDefinition(
                Child(registration, "Author"),
                Child(registration, "Description"),
                triggers ?? [],
                Child(exec, "Command"),
                Child(exec, "Arguments"),
                Child(comHandler, "ClassId"),
                Child(exec, "WorkingDirectory"));
        }
        catch (System.Xml.XmlException)
        {
            return TaskDefinition.Empty;
        }
    }

    /// <summary>
    /// Windows' own tasks carry "$(@%SystemRoot%\System32\x.dll,-103)" for
    /// author and description: a string resource, resolved the way the
    /// Task Scheduler UI does with SHLoadIndirectString. Anything else is
    /// returned as is.
    /// </summary>
    public static string? ResolveIndirect(string? text)
    {
        if (text is null || !text.StartsWith("$(@", StringComparison.Ordinal))
            return text;
        var reference = text.EndsWith(')') ? text[2..^1] : text[2..];
        var expanded = Environment.ExpandEnvironmentVariables(reference);
        var buffer = new char[1024];
        var hr = SHLoadIndirectString(expanded, buffer, buffer.Length, 0);
        if (hr < 0)
            return text;
        var length = Array.IndexOf(buffer, '\0');
        var resolved = new string(buffer, 0, length < 0 ? buffer.Length : length);
        return string.IsNullOrWhiteSpace(resolved) ? text : resolved;
    }

    private static string? Child(XElement? parent, string localName)
    {
        var value = parent?.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static unsafe string? ReadBstr(nint pInterface, nint fnPtr)
    {
        var fn = (delegate* unmanaged[Stdcall]<nint, nint*, int>)fnPtr;
        nint bstr;
        if (fn(pInterface, &bstr) < 0 || bstr == 0)
            return null;
        try
        {
            return Marshal.PtrToStringBSTR(bstr);
        }
        finally
        {
            Marshal.FreeBSTR(bstr);
        }
    }

    private static OperationResult<T> MapHResult<T>(int hr, string taskPath, string verb)
    {
        return hr switch
        {
            E_ACCESSDENIED => OperationResult<T>.Failure(
                $"Cannot {verb} task '{taskPath}': the task is protected by Windows.", ErrorCategory.AccessDenied),
            HR_FILE_NOT_FOUND or HR_PATH_NOT_FOUND => OperationResult<T>.Failure(
                $"Cannot {verb} task '{taskPath}': no task with that path exists.", ErrorCategory.NotFound),
            _ => OperationResult<T>.Failure(
                $"Cannot {verb} task '{taskPath}': HRESULT 0x{hr:X8}.", ErrorCategory.ServiceUnavailable),
        };
    }

    private static unsafe void Release(nint pUnk)
    {
        try
        {
            var vtable = *(nint**)pUnk;
            var releaseFn = (delegate* unmanaged[Stdcall]<nint, uint>)vtable[VtblRelease];
            releaseFn(pUnk);
        }
        catch
        {
            // Swallow release failures during cleanup
        }
    }

    [LibraryImport("ole32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int CoInitializeEx(nint pvReserved, uint dwCoInit);

    [LibraryImport("ole32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int CoCreateInstance(in Guid rclsid, nint pUnkOuter, uint dwClsContext, in Guid riid, out nint ppv);

    [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int SHLoadIndirectString(string pszSource, [Out] char[] pszOutBuf, int cchOutBuf, nint ppvReserved);
}
