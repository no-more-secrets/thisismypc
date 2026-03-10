using ThisIsMyPC.Core.Results;
using ThisIsMyPC.Interop.Com.Shell;

namespace ThisIsMyPC.Modules.Shell.Tests.Fakes;

public sealed class FakeModernPackagedHandlerService : IModernPackagedHandlerService
{
    private readonly List<ModernPackagedEntry> _entries = [];
    private bool _shouldFail;

    public FakeModernPackagedHandlerService AddHandler(
        string clsid,
        string handlerName,
        string packageFamilyName,
        string packageDisplayName = "Test Package",
        string publisherDisplayName = "Test Publisher",
        IReadOnlyList<string>? itemTypes = null,
        string? verbId = null,
        string? iconPath = null,
        string? installSource = null)
    {
        _entries.Add(new ModernPackagedEntry(
            Clsid: clsid,
            HandlerName: handlerName,
            PackageFamilyName: packageFamilyName,
            PackageDisplayName: packageDisplayName,
            PublisherDisplayName: publisherDisplayName,
            ItemTypes: itemTypes,
            VerbId: verbId,
            IconPath: iconPath,
            InstallSource: installSource));
        return this;
    }

    public void SetFailure() => _shouldFail = true;

    public OperationResult<IReadOnlyList<ModernPackagedEntry>> EnumerateModernHandlers()
    {
        return _shouldFail
            ? OperationResult<IReadOnlyList<ModernPackagedEntry>>.Failure(
                "Simulated failure", ErrorCategory.ServiceUnavailable)
            : OperationResult<IReadOnlyList<ModernPackagedEntry>>.Success(_entries);
    }
}
