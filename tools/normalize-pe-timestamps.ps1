# Removes wall-clock timestamps emitted by the Windows native linker. This is
# applied before signing so the distributed PE image itself is reproducible.
param(
    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path $_ -PathType Leaf })]
    [string]$Path
)

$ErrorActionPreference = 'Stop'
$resolvedPath = (Resolve-Path $Path).Path
$bytes = [IO.File]::ReadAllBytes($resolvedPath)
if ($bytes.Length -lt 64 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) {
    throw 'Input is not a DOS/PE image.'
}
$peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
if ($peOffset -lt 0 -or $peOffset + 24 -gt $bytes.Length -or
    $bytes[$peOffset] -ne 0x50 -or $bytes[$peOffset + 1] -ne 0x45 -or
    $bytes[$peOffset + 2] -ne 0 -or $bytes[$peOffset + 3] -ne 0) {
    throw 'Input has an invalid PE header.'
}

$sectionCount = [BitConverter]::ToUInt16($bytes, $peOffset + 6)
$optionalHeaderSize = [BitConverter]::ToUInt16($bytes, $peOffset + 20)
$optionalHeaderOffset = $peOffset + 24
$optionalHeaderEnd = $optionalHeaderOffset + $optionalHeaderSize
$sectionTableOffset = $optionalHeaderEnd
if ($optionalHeaderEnd -gt $bytes.Length -or
    $sectionTableOffset + (40 * $sectionCount) -gt $bytes.Length) {
    throw 'Input has truncated PE headers.'
}
$magic = [BitConverter]::ToUInt16($bytes, $optionalHeaderOffset)
switch ($magic) {
    0x10B {
        $numberOfDirectoriesOffset = $optionalHeaderOffset + 92
        $directoriesOffset = $optionalHeaderOffset + 96
    }
    0x20B {
        $numberOfDirectoriesOffset = $optionalHeaderOffset + 108
        $directoriesOffset = $optionalHeaderOffset + 112
    }
    default { throw "Unsupported PE optional-header magic 0x$($magic.ToString('X4'))." }
}
if ($numberOfDirectoriesOffset + 4 -gt $optionalHeaderEnd -or
    $optionalHeaderOffset + 68 -gt $optionalHeaderEnd) {
    throw 'Input has an incomplete PE optional header.'
}

function Convert-RvaToFileOffset([uint32]$Rva, [uint32]$Size) {
    for ($index = 0; $index -lt $sectionCount; $index++) {
        $sectionOffset = $sectionTableOffset + (40 * $index)
        $virtualSize = [BitConverter]::ToUInt32($bytes, $sectionOffset + 8)
        $virtualAddress = [BitConverter]::ToUInt32($bytes, $sectionOffset + 12)
        $rawSize = [BitConverter]::ToUInt32($bytes, $sectionOffset + 16)
        $rawOffset = [BitConverter]::ToUInt32($bytes, $sectionOffset + 20)
        $mappedSize = [Math]::Max([uint64]$virtualSize, [uint64]$rawSize)
        $rvaEnd = [uint64]$Rva + $Size
        if ($Rva -ge $virtualAddress -and
            $rvaEnd -le ([uint64]$virtualAddress + $mappedSize)) {
            $fileOffset = [uint64]$rawOffset + ($Rva - $virtualAddress)
            if ($fileOffset + $Size -gt $bytes.Length) {
                throw 'PE data directory maps outside the file.'
            }
            return [int]$fileOffset
        }
    }
    throw "PE RVA 0x$($Rva.ToString('X8')) does not map to a section."
}

# COFF TimeDateStamp and optional-header checksum.
[Array]::Clear($bytes, $peOffset + 8, 4)
[Array]::Clear($bytes, $optionalHeaderOffset + 64, 4)

# IMAGE_DEBUG_DIRECTORY entries each repeat the linker timestamp.
$directoryCount = [BitConverter]::ToUInt32($bytes, $numberOfDirectoriesOffset)
if ($directoryCount -gt 6) {
    $debugDirectoryEntry = $directoriesOffset + (6 * 8)
    if ($debugDirectoryEntry + 8 -gt $optionalHeaderEnd) {
        throw 'Input declares a Debug directory outside its optional header.'
    }
    $debugRva = [BitConverter]::ToUInt32($bytes, $debugDirectoryEntry)
    $debugSize = [BitConverter]::ToUInt32($bytes, $debugDirectoryEntry + 4)
    if (($debugRva -eq 0) -ne ($debugSize -eq 0)) {
        throw 'PE Debug directory has only one of RVA and size set.'
    }
    if ($debugRva -ne 0) {
        if (($debugSize % 28) -ne 0) {
            throw 'PE Debug directory size is not a sequence of IMAGE_DEBUG_DIRECTORY entries.'
        }
        $debugOffset = Convert-RvaToFileOffset $debugRva $debugSize
        for ($offset = $debugOffset; $offset -lt $debugOffset + $debugSize; $offset += 28) {
            [Array]::Clear($bytes, $offset + 4, 4)
        }
    }
}

$temporaryPath = "$resolvedPath.deterministic.tmp"
try {
    [IO.File]::WriteAllBytes($temporaryPath, $bytes)
    Move-Item -LiteralPath $temporaryPath -Destination $resolvedPath -Force
} finally {
    if (Test-Path $temporaryPath) { Remove-Item -LiteralPath $temporaryPath -Force }
}
Write-Host "$(Split-Path $resolvedPath -Leaf): PE timestamps normalized"
