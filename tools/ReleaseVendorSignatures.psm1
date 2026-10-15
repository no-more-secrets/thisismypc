# Preserve only the app-local Microsoft CRT. Other release PE files are signed by us.
function Get-ReleaseVendorRelativePaths {
    @(
        'lighting-engine/msvcp140.dll'
        'lighting-engine/msvcp140_codecvt_ids.dll'
        'lighting-engine/vcruntime140.dll'
        'lighting-engine/vcruntime140_1.dll'
    )
}

function Test-ReleaseVendorPath([string]$RelativePath) {
    $RelativePath.Replace('\', '/') -in @(Get-ReleaseVendorRelativePaths)
}

function Assert-ReleaseVendorSignature([string]$Path) {
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    $publisher = if ($null -ne $signature.SignerCertificate) {
        $signature.SignerCertificate.GetNameInfo(
            [Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false)
    }
    if ($signature.Status -ne 'Valid' -or $signature.SignatureType -ne 'Authenticode' -or
        $publisher -cnotin @('Microsoft Corporation', 'Microsoft Windows Software Compatibility Publisher') -or
        $null -eq $signature.TimeStamperCertificate) {
        throw "Vendor runtime requires a valid timestamped Microsoft signature: $Path"
    }
}

function Get-ReleaseVendorFiles([string]$Root) {
    foreach ($relative in Get-ReleaseVendorRelativePaths) {
        $path = Join-Path $Root $relative
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue }
        Assert-ReleaseVendorSignature $path
        [pscustomobject]@{
            relativePath = $relative
            sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        }
    }
}

function Test-PreservedReleaseVendorFile([string]$Path, [object[]]$Records) {
    $normalized = [IO.Path]::GetFullPath($Path).Replace('\', '/')
    foreach ($record in $Records) {
        if ($null -eq $record -or -not (Test-ReleaseVendorPath $record.relativePath) -or
            $record.sha256 -notmatch '^[0-9A-Fa-f]{64}$') {
            throw 'Invalid preserved vendor runtime record.'
        }
        if (-not $normalized.EndsWith('/' + $record.relativePath.Replace('\', '/'),
                [StringComparison]::OrdinalIgnoreCase)) { continue }
        if ((Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $record.sha256) {
            throw "Vendor runtime changed after staging: $Path"
        }
        Assert-ReleaseVendorSignature $Path
        return $true
    }
    foreach ($relative in Get-ReleaseVendorRelativePaths) {
        if ($normalized.EndsWith('/' + $relative, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Vendor runtime was not validated in staging: $Path"
        }
    }
    return $false
}

Export-ModuleMember -Function Get-ReleaseVendorRelativePaths, Test-ReleaseVendorPath,
    Assert-ReleaseVendorSignature, Get-ReleaseVendorFiles, Test-PreservedReleaseVendorFile
