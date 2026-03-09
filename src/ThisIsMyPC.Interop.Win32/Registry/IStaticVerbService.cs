using ThisIsMyPC.Core.Results;

namespace ThisIsMyPC.Interop.Win32.Registry;

public interface IStaticVerbService
{
    OperationResult<IReadOnlyList<StaticVerbEntry>> EnumerateStaticVerbs();
}
