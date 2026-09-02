# Stamps the Win32 version resource of an exe with a language. The C# compiler
# always writes the block as Language Neutral (translation 0000/04B0) and no
# MSBuild property changes that, so Explorer's Details tab reads "Language
# Neutral". This rewrites the block in place: the StringFileInfo key and the
# VarFileInfo translation take the language id, and the resource is re-added
# under that language. Run before Authenticode signing; it changes the file.
param(
    [Parameter(Mandatory = $true)]
    [string]$Path,

    # 0x0409 = English (United States).
    [int]$LanguageId = 0x0409
)

$ErrorActionPreference = 'Stop'
$Path = (Resolve-Path $Path).Path

Add-Type -Namespace TipcTools -Name VersionResource -MemberDefinition @'
[DllImport("version.dll", CharSet = CharSet.Unicode, SetLastError = true)]
public static extern int GetFileVersionInfoSizeW(string file, out int handle);
[DllImport("version.dll", CharSet = CharSet.Unicode, SetLastError = true)]
public static extern bool GetFileVersionInfoW(string file, int handle, int len, byte[] data);
[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
public static extern IntPtr BeginUpdateResourceW(string file, bool deleteExisting);
[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
public static extern bool UpdateResourceW(IntPtr update, IntPtr type, IntPtr name, ushort language, byte[] data, int len);
[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
public static extern bool EndUpdateResourceW(IntPtr update, bool discard);
'@

function Find-Utf16([byte[]]$haystack, [string]$text) {
    $needle = [System.Text.Encoding]::Unicode.GetBytes($text)
    for ($i = 0; $i -le $haystack.Length - $needle.Length; $i += 2) {
        $match = $true
        for ($j = 0; $j -lt $needle.Length; $j++) {
            if ($haystack[$i + $j] -ne $needle[$j]) { $match = $false; break }
        }
        if ($match) { return $i }
    }
    return -1
}

$size = [TipcTools.VersionResource]::GetFileVersionInfoSizeW($Path, [ref]0)
if ($size -le 0) { throw "No version resource in $Path" }
$buffer = New-Object byte[] $size
if (-not [TipcTools.VersionResource]::GetFileVersionInfoW($Path, 0, $size, $buffer)) { throw "GetFileVersionInfo failed for $Path" }

# VS_VERSIONINFO.wLength is the first WORD; the rest of the buffer is scratch.
$length = [BitConverter]::ToUInt16($buffer, 0)
$block = New-Object byte[] $length
[Array]::Copy($buffer, $block, $length)

$langHex = $LanguageId.ToString('x4')
$oldKey = Find-Utf16 $block '000004b0'
if ($oldKey -lt 0) { throw 'The version block is not the language-neutral form the compiler writes; nothing changed.' }
$newKey = [System.Text.Encoding]::Unicode.GetBytes($langHex + '04b0')
[Array]::Copy($newKey, 0, $block, $oldKey, $newKey.Length)

$translation = Find-Utf16 $block 'Translation'
if ($translation -lt 0) { throw 'VarFileInfo/Translation not found; nothing changed.' }
$valueOffset = $translation + ('Translation'.Length + 1) * 2
$valueOffset = ($valueOffset + 3) -band (-bnot 3)
$block[$valueOffset] = [byte]($LanguageId -band 0xFF)
$block[$valueOffset + 1] = [byte](($LanguageId -shr 8) -band 0xFF)

$RT_VERSION = [IntPtr]16
$VS_VERSION_INFO = [IntPtr]1
$update = [TipcTools.VersionResource]::BeginUpdateResourceW($Path, $false)
if ($update -eq [IntPtr]::Zero) { throw "BeginUpdateResource failed for $Path" }
$ok = [TipcTools.VersionResource]::UpdateResourceW($update, $RT_VERSION, $VS_VERSION_INFO, 0, $null, 0) -and
      [TipcTools.VersionResource]::UpdateResourceW($update, $RT_VERSION, $VS_VERSION_INFO, [uint16]$LanguageId, $block, $block.Length)
if (-not $ok) {
    [TipcTools.VersionResource]::EndUpdateResourceW($update, $true) | Out-Null
    throw "UpdateResource failed for $Path"
}
if (-not [TipcTools.VersionResource]::EndUpdateResourceW($update, $false)) { throw "EndUpdateResource failed for $Path" }

$info = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($Path)
Write-Host "$(Split-Path $Path -Leaf): language now '$($info.Language)'"
