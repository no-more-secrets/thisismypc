$script:FooterMagic = [Text.Encoding]::ASCII.GetBytes('TIPC-MSI-PAYLOAD')
$script:FooterSize = 72

function Get-PeCertificateTable {
    param([Parameter(Mandatory)][byte[]]$Bytes)

    if ($Bytes.Length -lt 64 -or $Bytes[0] -ne 0x4D -or $Bytes[1] -ne 0x5A) {
        throw 'Installer bundle is not a DOS/PE image.'
    }
    $peOffset = [BitConverter]::ToInt32($Bytes, 0x3C)
    if ($peOffset -lt 0 -or $peOffset + 24 -gt $Bytes.Length -or
        $Bytes[$peOffset] -ne 0x50 -or $Bytes[$peOffset + 1] -ne 0x45 -or
        $Bytes[$peOffset + 2] -ne 0 -or $Bytes[$peOffset + 3] -ne 0) {
        throw 'Installer bundle has an invalid PE header.'
    }

    $optionalHeaderOffset = $peOffset + 24
    $optionalHeaderSize = [BitConverter]::ToUInt16($Bytes, $peOffset + 20)
    $optionalHeaderEnd = $optionalHeaderOffset + $optionalHeaderSize
    if ($optionalHeaderEnd -gt $Bytes.Length) {
        throw 'Installer bundle has a truncated PE optional header.'
    }
    $magic = [BitConverter]::ToUInt16($Bytes, $optionalHeaderOffset)
    switch ($magic) {
        0x10B { $directoryCountOffset = $optionalHeaderOffset + 92; $directoriesOffset = $optionalHeaderOffset + 96 }
        0x20B { $directoryCountOffset = $optionalHeaderOffset + 108; $directoriesOffset = $optionalHeaderOffset + 112 }
        default { throw 'Installer bundle has an unsupported PE optional header.' }
    }
    if ($directoryCountOffset + 4 -gt $optionalHeaderEnd) {
        throw 'Installer bundle has an incomplete PE optional header.'
    }

    $certificateOffset = [uint32]0
    $certificateSize = [uint32]0
    if ([BitConverter]::ToUInt32($Bytes, $directoryCountOffset) -gt 4) {
        $securityDirectoryOffset = $directoriesOffset + 32
        if ($securityDirectoryOffset + 8 -gt $optionalHeaderEnd) {
            throw 'Installer bundle has an invalid Security directory.'
        }
        $certificateOffset = [BitConverter]::ToUInt32($Bytes, $securityDirectoryOffset)
        $certificateSize = [BitConverter]::ToUInt32($Bytes, $securityDirectoryOffset + 4)
    }
    if (($certificateOffset -eq 0) -ne ($certificateSize -eq 0)) {
        throw 'Installer bundle has an incomplete Security directory.'
    }
    if ($certificateOffset -ne 0 -and ([uint64]$certificateOffset + $certificateSize) -ne $Bytes.Length) {
        throw 'Installer bundle certificate table is not terminal.'
    }

    [pscustomobject]@{
        Offset = [int64]$certificateOffset
        Size = [int64]$certificateSize
        ContentEnd = if ($certificateOffset -eq 0) { [int64]$Bytes.Length } else { [int64]$certificateOffset }
    }
}

function Get-InstallerBundleInfo {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)

    $resolved = (Resolve-Path -LiteralPath $Path).Path
    $bytes = [IO.File]::ReadAllBytes($resolved)
    $certificate = Get-PeCertificateTable -Bytes $bytes
    if ($certificate.ContentEnd -lt $script:FooterSize) {
        throw 'Installer bundle is too short to contain its payload footer.'
    }
    $footerOffset = $certificate.ContentEnd - $script:FooterSize
    for ($index = 0; $index -lt $script:FooterMagic.Length; $index++) {
        if ($bytes[$footerOffset + $index] -ne $script:FooterMagic[$index]) {
            throw 'Installer bundle payload footer is missing.'
        }
    }
    $version = [BitConverter]::ToUInt32($bytes, $footerOffset + 16)
    $reserved = [BitConverter]::ToUInt32($bytes, $footerOffset + 20)
    if ($version -ne 1 -or $reserved -ne 0) {
        throw 'Installer bundle payload footer version is unsupported.'
    }
    $payloadOffset = [BitConverter]::ToUInt64($bytes, $footerOffset + 24)
    $payloadLength = [BitConverter]::ToUInt64($bytes, $footerOffset + 32)
    if ($payloadOffset -gt [uint64]$footerOffset -or
        $payloadLength -gt [uint64]$footerOffset - $payloadOffset) {
        throw 'Installer bundle payload range is invalid.'
    }
    $paddingStart = [int64]$payloadOffset + [int64]$payloadLength
    if ($footerOffset - $paddingStart -gt 7) {
        throw 'Installer bundle has excessive payload padding.'
    }
    for ($offset = $paddingStart; $offset -lt $footerOffset; $offset++) {
        if ($bytes[$offset] -ne 0) { throw 'Installer bundle payload padding is not zero.' }
    }

    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $actualHashBytes = $sha.ComputeHash($bytes, [int]$payloadOffset, [int]$payloadLength)
    }
    finally {
        $sha.Dispose()
    }
    $expectedHashBytes = New-Object byte[] 32
    [Buffer]::BlockCopy($bytes, $footerOffset + 40, $expectedHashBytes, 0, 32)
    $difference = 0
    for ($index = 0; $index -lt $actualHashBytes.Length; $index++) {
        $difference = $difference -bor ($actualHashBytes[$index] -bxor $expectedHashBytes[$index])
    }
    if ($difference -ne 0) {
        throw 'Installer bundle MSI payload hash does not match its footer.'
    }

    [pscustomobject]@{
        Path = $resolved
        StubLength = [int64]$payloadOffset
        PayloadOffset = [int64]$payloadOffset
        PayloadLength = [int64]$payloadLength
        PayloadSha256 = -join ($actualHashBytes | ForEach-Object { $_.ToString('X2') })
        FooterOffset = [int64]$footerOffset
        CertificateOffset = $certificate.Offset
        CertificateSize = $certificate.Size
    }
}

function Add-InstallerPayload {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$StubPath,
        [Parameter(Mandatory)][string]$PayloadPath,
        [Parameter(Mandatory)][string]$OutputPath
    )

    $stub = (Resolve-Path -LiteralPath $StubPath).Path
    $payload = (Resolve-Path -LiteralPath $PayloadPath).Path
    $output = [IO.Path]::GetFullPath($OutputPath)
    if ($output -eq $stub -or $output -eq $payload) { throw 'Bundle output must differ from its inputs.' }
    $outputDirectory = Split-Path $output -Parent
    if (-not (Test-Path -LiteralPath $outputDirectory -PathType Container)) {
        throw "Bundle output directory does not exist: $outputDirectory"
    }

    $stubBytes = [IO.File]::ReadAllBytes($stub)
    $certificate = Get-PeCertificateTable -Bytes $stubBytes
    if ($certificate.Size -ne 0) { throw 'Installer launcher stub must be unsigned.' }
    $payloadBytes = [IO.File]::ReadAllBytes($payload)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $payloadHash = $sha.ComputeHash($payloadBytes) } finally { $sha.Dispose() }
    $paddingLength = (8 - (($stubBytes.Length + $payloadBytes.Length + $script:FooterSize) % 8)) % 8

    $footer = New-Object byte[] $script:FooterSize
    [Buffer]::BlockCopy($script:FooterMagic, 0, $footer, 0, $script:FooterMagic.Length)
    [Buffer]::BlockCopy([BitConverter]::GetBytes([uint32]1), 0, $footer, 16, 4)
    [Buffer]::BlockCopy([BitConverter]::GetBytes([uint64]$stubBytes.Length), 0, $footer, 24, 8)
    [Buffer]::BlockCopy([BitConverter]::GetBytes([uint64]$payloadBytes.Length), 0, $footer, 32, 8)
    [Buffer]::BlockCopy($payloadHash, 0, $footer, 40, 32)

    $stream = [IO.File]::Open($output, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $stream.Write($stubBytes, 0, $stubBytes.Length)
        $stream.Write($payloadBytes, 0, $payloadBytes.Length)
        if ($paddingLength -ne 0) { $stream.Write((New-Object byte[] $paddingLength), 0, $paddingLength) }
        $stream.Write($footer, 0, $footer.Length)
    }
    finally {
        $stream.Dispose()
    }
    Get-InstallerBundleInfo -Path $output
}

function Export-InstallerPayload {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$OutputPath,
        [switch]$LauncherOnly
    )

    $info = Get-InstallerBundleInfo -Path $Path
    $output = [IO.Path]::GetFullPath($OutputPath)
    if ($output -eq $info.Path) { throw 'Bundle extraction output must differ from its input.' }
    $length = if ($LauncherOnly) { $info.StubLength } else { $info.PayloadLength }
    $offset = if ($LauncherOnly) { 0 } else { $info.PayloadOffset }
    $inputStream = [IO.File]::Open($info.Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $outputStream = [IO.File]::Open($output, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $inputStream.Position = $offset
        $buffer = New-Object byte[] 1048576
        $remaining = $length
        while ($remaining -gt 0) {
            $read = $inputStream.Read($buffer, 0, [int][Math]::Min($buffer.Length, $remaining))
            if ($read -le 0) { throw 'Installer bundle ended during extraction.' }
            $outputStream.Write($buffer, 0, $read)
            $remaining -= $read
        }
    }
    finally {
        $outputStream.Dispose()
        $inputStream.Dispose()
    }
    Get-Item -LiteralPath $output
}

Export-ModuleMember -Function Add-InstallerPayload, Export-InstallerPayload, Get-InstallerBundleInfo
