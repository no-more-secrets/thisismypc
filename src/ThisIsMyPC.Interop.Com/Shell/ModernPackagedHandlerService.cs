using System.Runtime.InteropServices;
using ThisIsMyPC.Core.Results;
using Windows.ApplicationModel.AppExtensions;
using Windows.Foundation.Collections;

namespace ThisIsMyPC.Interop.Com.Shell;

public sealed class ModernPackagedHandlerService : IModernPackagedHandlerService
{
    public OperationResult<IReadOnlyList<ModernPackagedEntry>> EnumerateModernHandlers()
    {
        try
        {
            var entries = Task.Run(EnumerateAsync).GetAwaiter().GetResult();
            return OperationResult<IReadOnlyList<ModernPackagedEntry>>.Success(entries);
        }
        catch (COMException ex)
        {
            return OperationResult<IReadOnlyList<ModernPackagedEntry>>.Failure(
                $"WinRT AppExtensionCatalog failed: {ex.Message}",
                ErrorCategory.ServiceUnavailable, ex);
        }
        catch (Exception ex)
        {
            return OperationResult<IReadOnlyList<ModernPackagedEntry>>.Failure(
                $"Modern handler enumeration failed: {ex.Message}",
                ErrorCategory.ServiceUnavailable, ex);
        }
    }

    private static async Task<IReadOnlyList<ModernPackagedEntry>> EnumerateAsync()
    {
        var catalog = AppExtensionCatalog.Open("windows.fileExplorerContextMenus");
        var extensions = await catalog.FindAllAsync();
        var entries = new List<ModernPackagedEntry>();

        foreach (var ext in extensions)
        {
            var entry = await ParseExtensionAsync(ext);
            if (entry is not null)
                entries.Add(entry);
        }

        return entries;
    }

    private static async Task<ModernPackagedEntry?> ParseExtensionAsync(AppExtension ext)
    {
        try
        {
            var pkg = ext.Package;
            var packageFamilyName = pkg.Id.FamilyName;
            var packageDisplayName = pkg.DisplayName;
            var publisherDisplayName = pkg.PublisherDisplayName;

            // Extract extension properties for CLSID, ItemTypes, verb info
            var props = await ext.GetExtensionPropertiesAsync();

            var clsid = ExtractClsid(props);
            if (clsid is null)
                return null; // No CLSID = not a usable handler registration

            var itemTypes = ExtractItemTypes(props);
            var verbId = ext.Id;
            var iconPath = ExtractIconPath(props);
            var installSource = pkg.SignatureKind switch
            {
                Windows.ApplicationModel.PackageSignatureKind.Store => "Microsoft Store",
                Windows.ApplicationModel.PackageSignatureKind.Developer => "Sideloaded",
                Windows.ApplicationModel.PackageSignatureKind.Enterprise => "Enterprise",
                Windows.ApplicationModel.PackageSignatureKind.System => "System",
                _ => null,
            };

            return new ModernPackagedEntry(
                Clsid: clsid,
                HandlerName: ext.DisplayName,
                PackageFamilyName: packageFamilyName,
                PackageDisplayName: packageDisplayName,
                PublisherDisplayName: publisherDisplayName,
                ItemTypes: itemTypes,
                VerbId: verbId,
                IconPath: iconPath,
                InstallSource: installSource);
        }
        catch (Exception ex)
        {
            // Individual extension parsing failure should not block enumeration
            System.Diagnostics.Debug.WriteLine(
                $"[ModernPackagedHandlerService] Failed to parse extension '{ext.DisplayName}': {ex.Message}");
            return null;
        }
    }

    private static string? ExtractClsid(IPropertySet? props)
    {
        if (props is null)
            return null;

        // The CLSID is typically nested under a "Verb" property set
        if (props.TryGetValue("Verb", out var verbObj) && verbObj is IPropertySet verbSet)
        {
            if (verbSet.TryGetValue("Clsid", out var clsidObj) && clsidObj is string clsidStr)
                return NormalizeClsid(clsidStr);
            if (verbSet.TryGetValue("Id", out var idObj) && idObj is string idStr && IsClsidFormat(idStr))
                return NormalizeClsid(idStr);
        }

        // Direct CLSID property
        if (props.TryGetValue("Clsid", out var directClsid) && directClsid is string directStr)
            return NormalizeClsid(directStr);

        return null;
    }

    private static IReadOnlyList<string>? ExtractItemTypes(IPropertySet? props)
    {
        if (props is null)
            return null;

        var types = new List<string>();

        if (props.TryGetValue("ItemType", out var itemTypeObj))
        {
            switch (itemTypeObj)
            {
                case string singleType:
                    types.Add(singleType);
                    break;
                case IPropertySet itemTypeSet:
                    foreach (var kvp in itemTypeSet)
                    {
                        if (kvp.Value is string typeStr)
                            types.Add(typeStr);
                        else if (kvp.Value is IPropertySet innerSet &&
                                 innerSet.TryGetValue("Type", out var typeVal) &&
                                 typeVal is string innerTypeStr)
                            types.Add(innerTypeStr);
                    }
                    break;
            }
        }

        return types.Count > 0 ? types : null;
    }

    private static string? ExtractIconPath(IPropertySet? props)
    {
        if (props is null)
            return null;

        if (props.TryGetValue("Icon", out var iconObj) && iconObj is string iconStr)
            return iconStr;

        return null;
    }

    private static string NormalizeClsid(string clsid)
    {
        // Ensure CLSID is wrapped in braces
        if (!clsid.StartsWith('{'))
            clsid = "{" + clsid + "}";
        return clsid;
    }

    private static bool IsClsidFormat(string value)
    {
        // Check if string looks like a GUID/CLSID
        var stripped = value.Trim('{', '}');
        return Guid.TryParse(stripped, out _);
    }
}
