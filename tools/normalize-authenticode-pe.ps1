# Writes the canonical unsigned form of a PE image without modifying the input.
# The Authenticode certificate table must be absent or be a valid, aligned table
# at end of file. The PE checksum and Security data-directory entry are zeroed
# because Authenticode excludes both fields from its image digest.
param(
    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path $_ -PathType Leaf })]
    [string]$Path,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$inputPath = (Resolve-Path $Path).Path
$outputFullPath = [IO.Path]::GetFullPath($OutputPath)
if ($inputPath -eq $outputFullPath) {
    throw 'OutputPath must differ from Path; the signed input is never modified.'
}
$outputDirectory = Split-Path $outputFullPath -Parent
if (-not (Test-Path $outputDirectory -PathType Container)) {
    throw "Output directory does not exist: $outputDirectory"
}

$bytes = [IO.File]::ReadAllBytes($inputPath)
if ($bytes.Length -lt 64 -or $bytes[0] -ne 0x4D -or $bytes[1] -ne 0x5A) {
    throw 'Input is not a DOS/PE image.'
}
$peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
if ($peOffset -lt 0 -or $peOffset + 24 -gt $bytes.Length -or
    $bytes[$peOffset] -ne 0x50 -or $bytes[$peOffset + 1] -ne 0x45 -or
    $bytes[$peOffset + 2] -ne 0 -or $bytes[$peOffset + 3] -ne 0) {
    throw 'Input has an invalid PE header.'
}

$optionalHeaderSize = [BitConverter]::ToUInt16($bytes, $peOffset + 20)
$sectionCount = [BitConverter]::ToUInt16($bytes, $peOffset + 6)
$optionalHeaderOffset = $peOffset + 24
$optionalHeaderEnd = $optionalHeaderOffset + $optionalHeaderSize
$sectionTableOffset = $optionalHeaderEnd
if ($optionalHeaderEnd -gt $bytes.Length -or
    $sectionTableOffset + (40 * $sectionCount) -gt $bytes.Length) {
    throw 'Input has a truncated PE optional header.'
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
$checksumOffset = $optionalHeaderOffset + 64
if ($checksumOffset + 4 -gt $optionalHeaderEnd -or
    $numberOfDirectoriesOffset + 4 -gt $optionalHeaderEnd) {
    throw 'Input has an incomplete PE optional header.'
}

$directoryCount = [BitConverter]::ToUInt32($bytes, $numberOfDirectoriesOffset)
$certificateOffset = [uint32]0
$certificateSize = [uint32]0
$securityDirectoryOffset = $directoriesOffset + (4 * 8)
if ($directoryCount -gt 4) {
    if ($securityDirectoryOffset + 8 -gt $optionalHeaderEnd) {
        throw 'Input declares a Security directory outside its optional header.'
    }
    $certificateOffset = [BitConverter]::ToUInt32($bytes, $securityDirectoryOffset)
    $certificateSize = [BitConverter]::ToUInt32($bytes, $securityDirectoryOffset + 4)
}

if (($certificateOffset -eq 0) -ne ($certificateSize -eq 0)) {
    throw 'PE Security directory has only one of file offset and size set.'
}

$canonicalLength = $bytes.Length
if ($certificateOffset -ne 0) {
    if (($certificateOffset % 8) -ne 0) {
        throw 'Authenticode certificate table is not aligned to eight bytes.'
    }
    $lastSectionByte = [uint64]0
    for ($index = 0; $index -lt $sectionCount; $index++) {
        $sectionOffset = $sectionTableOffset + (40 * $index)
        $rawSize = [BitConverter]::ToUInt32($bytes, $sectionOffset + 16)
        $rawOffset = [BitConverter]::ToUInt32($bytes, $sectionOffset + 20)
        $rawEnd = [uint64]$rawOffset + $rawSize
        if ($rawEnd -gt $bytes.Length) { throw 'PE section raw data extends beyond the file.' }
        if ($rawEnd -gt $lastSectionByte) { $lastSectionByte = $rawEnd }
    }
    $headersEnd = [uint64]$sectionTableOffset + (40 * $sectionCount)
    if ($lastSectionByte -lt $headersEnd) { $lastSectionByte = $headersEnd }
    $certificateEnd = [uint64]$certificateOffset + $certificateSize
    if ($certificateOffset -lt $lastSectionByte -or $certificateEnd -ne $bytes.Length) {
        throw 'Authenticode certificate table must be a terminal table with no overlay.'
    }

    $cursor = [uint64]$certificateOffset
    while ($cursor -lt $certificateEnd) {
        if ($certificateEnd - $cursor -lt 8) {
            throw 'Authenticode certificate table ends with an incomplete WIN_CERTIFICATE header.'
        }
        $certificateLength = [BitConverter]::ToUInt32($bytes, [int]$cursor)
        if ($certificateLength -lt 8) {
            throw 'Authenticode certificate table contains an invalid WIN_CERTIFICATE length.'
        }
        $revision = [BitConverter]::ToUInt16($bytes, [int]$cursor + 4)
        $certificateType = [BitConverter]::ToUInt16($bytes, [int]$cursor + 6)
        if ($revision -ne 0x0200 -or $certificateType -ne 0x0002) {
            throw 'Authenticode table contains a record other than revision 2 PKCS SignedData.'
        }
        $alignedLength = ([uint64]$certificateLength + 7) -band (-bnot [uint64]7)
        if ($cursor + $alignedLength -gt $certificateEnd) {
            throw 'Authenticode certificate table contains a truncated WIN_CERTIFICATE record.'
        }
        $cursor += $alignedLength
    }
    if ($cursor -ne $certificateEnd) {
        throw 'Authenticode certificate records do not exactly fill the Security directory.'
    }
    $canonicalLength = [int]$certificateOffset
}

[Array]::Clear($bytes, $checksumOffset, 4)
if ($directoryCount -gt 4) {
    [Array]::Clear($bytes, $securityDirectoryOffset, 8)
}
$canonical = New-Object byte[] $canonicalLength
[Buffer]::BlockCopy($bytes, 0, $canonical, 0, $canonicalLength)
[IO.File]::WriteAllBytes($outputFullPath, $canonical)

$hash = (Get-FileHash -LiteralPath $outputFullPath -Algorithm SHA256).Hash
Write-Host "Canonical PE: $outputFullPath"
Write-Host "SHA256: $hash"
if ($certificateOffset -eq 0) {
    Write-Host 'Authenticode certificate table: absent'
} else {
    Write-Host "Authenticode certificate table: removed $certificateSize bytes"
}
