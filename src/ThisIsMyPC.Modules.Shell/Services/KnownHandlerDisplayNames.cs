namespace ThisIsMyPC.Modules.Shell.Services;

/// <summary>
/// Maps known COM handler CLSIDs to their user-visible context menu text.
/// The registry names (e.g., "Shell extensions for sharing") are developer-facing;
/// this map provides the text users actually see in Explorer's right-click menu.
/// </summary>
internal static class KnownHandlerDisplayNames
{
    private static readonly Dictionary<string, string> DisplayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // Critical, always visible
        ["{09799AFB-AD67-11d1-ABCD-00C04FC30936}"] = "Open with",
        ["{7BA4C740-9E81-11CF-99D3-00AA004AE837}"] = "Send to",
        ["{D969A300-E7FF-11d0-A93B-00A0C90F2719}"] = "New >",
        ["{f3d06e7c-1e45-4a26-847e-f9fcdee59be0}"] = "Copy as path",
        ["{00021401-0000-0000-C000-000000000046}"] = "Shortcut (.lnk) handler",
        ["{85cfccaf-2d14-42b6-80b6-f40f65d016e7}"] = "Symlink handler",
        ["{f81e9010-6ea4-11ce-a7ff-00aa003ca9f6}"] = "Give access to",
        ["{a2a9545d-a0c2-42b4-9708-a0b2badd77c8}"] = "Pin to Start",
        ["{90AA3A4E-1CBA-4233-B8BB-535773D48449}"] = "Pin to taskbar",
        ["{A470F8CF-A1E8-4f65-8335-227475AA5C46}"] = "Encrypt / Decrypt (EFS)",

        // System features
        ["{470C0EBD-5D73-4d58-9CED-E91E22E23282}"] = "Pin to Start (apps)",
        ["{7AD84985-87B4-4a16-BE58-8B72A5B390F7}"] = "Cast to Device",
        ["{474C98EE-CF3D-41f5-80E3-4AAB0AB04301}"] = "Make available offline",
        ["{1d27f844-3a1f-4410-85ac-14651078412d}"] = "Troubleshoot compatibility",
        ["{b8cdcb65-b1bf-4b42-9428-1dfdb7ee92af}"] = "Extract All (ZIP)",
        ["{EE07CEF5-3441-4CFB-870A-4002C724783A}"] = "Extract All (archives)",
        ["{7444C717-39BF-11D1-8CD9-00C04FC29D45}"] = "Certificate handler",
        ["{FBF23B40-E3F0-101B-8488-00AA003E56F8}"] = "Internet Shortcut handler",
        ["{E61BF828-5E63-4287-BEF1-60B1A4FDE0E3}"] = "Work Folders sync",
        ["{CB3D0F55-BC2C-4C1A-85ED-23ED75B5106B}"] = "OneDrive sync overlay",
        ["{09A47860-11B0-4DA5-AFA5-26D86198A780}"] = "Scan with Microsoft Defender",
        ["{e2bf9676-5f8f-435c-97eb-11607a5bedf7}"] = "Share",
        ["{596AB062-B4D2-4215-9F74-E9109B0A8153}"] = "Restore previous versions",
        ["{0bf754aa-c967-445c-ab3d-d8fda9bae7ef}"] = "Desktop slideshow",
        ["{2854F705-3548-414C-A113-93E27C808C85}"] = "Enhanced storage",
        ["{1a184871-359e-4f67-aad9-5b9905d62232}"] = "Install / Preview font",
        ["{FFE2A43C-56B9-4bf5-9A79-CC6D4285608A}"] = "Rotate image",
        ["{3dad6c5d-2167-4cae-9914-f99e41c12cfa}"] = "Include in library",
        ["{0af96ede-aebf-41ed-a1c8-cf7a685505b6}"] = "Library folder menu",
        ["{37ea3a21-7493-4208-a011-7f9ea79ce9f5}"] = "Open file location",
        ["{F9A7AB61-C0BC-490e-A7FE-BFF26B327A3F}"] = "OpenSearch results",
        ["{fbeb8a05-beee-4442-804e-409d6c4515e9}"] = "CD/DVD burning",
        ["{CF67796C-F57F-45f8-92FB-AD698826C602}"] = "Contacts handler",
        ["{16C2C29D-0E5F-45f3-A445-03E03F587B7D}"] = "Contact groups handler",
        ["{2206CDB2-19C1-11D1-89E0-00C04FD7A829}"] = "Data links (UDL)",
        ["{D6791A63-E7E2-4fee-BF52-5DED8E86E9B8}"] = "Portable device menu",
        ["{77597368-7b15-11d0-a0c2-080036af3f03}"] = "Printer management",

        // Third-party: use the visible menu text where known
        ["{D3F9A525-8824-497A-BE36-B23E22F141FC}"] = "Change Attributes",
        ["{A6595CD1-BF77-430A-A452-18696685F7C7}"] = "Convert to Adobe PDF",
        ["{2A118EB5-5797-4F5E-8B3D-F4ECBA3C98E4}"] = "Adobe Creative Cloud sync",

        // PowerToys
        ["{84D68575-E186-46AD-B0CB-BAEB45EE29C0}"] = "What's using this file? (PowerToys)",
        ["{51B4D7E5-7568-4234-B4BB-47FB3C016A69}"] = "Resize pictures (PowerToys)",
        ["{0440049F-D1DC-4E46-B27B-98393D79486B}"] = "PowerRename",
    };

    /// <summary>
    /// Returns a user-visible display name for the given CLSID, or null if no mapping exists.
    /// </summary>
    public static string? GetDisplayName(string clsid) =>
        DisplayNames.GetValueOrDefault(clsid);
}
