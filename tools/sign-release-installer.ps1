param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })]
    [string]$AssetDirectory,

    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9A-Fa-f]{40}$')]
    [string]$SignThumbprint,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F-]{36}$')]
    [string]$ESignerCredentialId,

    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$CodeSignToolArchive,

    [string]$ESignerUsername = $env:ESIGNER_USERNAME,

    [ValidateNotNullOrEmpty()]
    [string]$SigningDescription = 'ThisIsMyPC',

    [string]$TimestampUrl = 'http://ts.ssl.com'
)

$ErrorActionPreference = 'Stop'

# A CI runner supplies the password only to this short signing process. Convert
# it immediately and remove it from the environment before starting any child.
$securePassword = $null
if (Test-Path Env:ESIGNER_PASSWORD) {
    $securePassword = ConvertTo-SecureString $env:ESIGNER_PASSWORD -AsPlainText -Force
    Remove-Item Env:ESIGNER_PASSWORD
}

$assetRoot = (Resolve-Path -LiteralPath $AssetDirectory).Path
$installerAsset = Join-Path $assetRoot "ThisIsMyPC-Installer-$Version.exe"
if (-not (Test-Path -LiteralPath $installerAsset -PathType Leaf)) {
    throw "Release installer is missing: $installerAsset"
}
if ((Get-AuthenticodeSignature -LiteralPath $installerAsset).Status -ne 'NotSigned') {
    throw 'Release installer must be unsigned before the eSigner signing step.'
}
if (((Get-Item -LiteralPath $installerAsset).Length % 8) -ne 0) {
    throw 'Unsigned installer length is not eight-byte aligned; Authenticode would insert non-certificate padding.'
}

& (Join-Path $PSScriptRoot 'test-esigner-signing-environment.ps1') `
    -CodeSignToolArchive $CodeSignToolArchive

$certificates = @(
    Get-ChildItem Cert:\CurrentUser\My |
        Where-Object { $_.Thumbprint -eq $SignThumbprint.ToUpperInvariant() }
)
if ($certificates.Count -ne 1) {
    throw "Expected one certificate with thumbprint $SignThumbprint in Cert:\CurrentUser\My, found $($certificates.Count). Is eSigner CKA loaded?"
}
$signerCertificate = $certificates[0]
if (-not $signerCertificate.HasPrivateKey) {
    throw 'Certificate found but its eSigner CKA private key is not reachable.'
}
$signerName = $signerCertificate.GetNameInfo(
    [Security.Cryptography.X509Certificates.X509NameType]::SimpleName,
    $false)
if ($signerName -ne 'No More Secrets, LLC') {
    throw "Refusing unexpected signing identity: $signerName"
}
$ekuExtension = $signerCertificate.Extensions |
    Where-Object { $_.Oid.Value -eq '2.5.29.37' } |
    Select-Object -First 1
if (-not $ekuExtension) {
    throw 'Signing certificate has no Enhanced Key Usage extension.'
}
$enhancedKeyUsages = [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]$ekuExtension
if ($enhancedKeyUsages.EnhancedKeyUsages.Value -notcontains '1.3.6.1.5.5.7.3.3') {
    throw 'Selected certificate is not authorized for code signing.'
}
$now = Get-Date
if ($now -lt $signerCertificate.NotBefore -or $now -gt $signerCertificate.NotAfter) {
    throw 'Selected signing certificate is not currently valid.'
}

$buildEnvironment = Get-Content -LiteralPath `
    (Join-Path $PSScriptRoot 'reproducible-build-environment.json') -Raw | ConvertFrom-Json
$signtool = Join-Path ${env:ProgramFiles(x86)} `
    "Windows Kits\10\bin\$($buildEnvironment.windowsSdkVersion)\x64\signtool.exe"
if (-not (Test-Path -LiteralPath $signtool -PathType Leaf)) {
    throw "Pinned signtool.exe is missing: $signtool"
}
$signToolHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $signtool).Hash
if ($signToolHash -ne $buildEnvironment.signToolSha256) {
    throw "signtool.exe hash is $signToolHash, expected $($buildEnvironment.signToolSha256)."
}

$unsignedInstallerCopy = Join-Path ([IO.Path]::GetTempPath()) `
    ("thisismypc-unsigned-" + [guid]::NewGuid().ToString('N') + '.exe')
Copy-Item -LiteralPath $installerAsset -Destination $unsignedInstallerCopy
try {
    Write-Host "Submitting $(Split-Path $installerAsset -Leaf) for the required SSL.com malware scan..."
    & (Join-Path $PSScriptRoot 'invoke-esigner-malware-scan.ps1') `
        -InputFile $installerAsset `
        -CodeSignToolArchive $CodeSignToolArchive `
        -CredentialId $ESignerCredentialId `
        -ProgramName $SigningDescription `
        -Username $ESignerUsername `
        -Password $securePassword

    Write-Host "Signing $(Split-Path $installerAsset -Leaf)..."
    & $signtool sign /fd sha256 /tr $TimestampUrl /td sha256 `
        /d $SigningDescription /sha1 $SignThumbprint $installerAsset
    if ($LASTEXITCODE -ne 0) { throw 'signtool failed on the installer exe' }

    Write-Host 'Verifying the downloadable installer Authenticode signature and timestamp...'
    & $signtool verify /pa /all /v $installerAsset
    if ($LASTEXITCODE -ne 0) { throw 'signtool verification failed on the installer exe' }
    $signature = Get-AuthenticodeSignature -LiteralPath $installerAsset
    if ($signature.Status -ne 'Valid') {
        throw "Installer signature status is $($signature.Status), not Valid."
    }
    if ($signature.SignerCertificate.Thumbprint -ne $SignThumbprint.ToUpperInvariant()) {
        throw "Installer was signed by unexpected certificate $($signature.SignerCertificate.Thumbprint)."
    }
    if (-not $signature.TimeStamperCertificate) {
        throw 'Installer signature has no RFC 3161 timestamp.'
    }

    Write-Host 'Proving that Authenticode removal recovers the exact unsigned installer...'
    & (Join-Path $PSScriptRoot 'compare-reproducible-installer.ps1') `
        -ReleasedInstaller $installerAsset `
        -LocalInstaller $unsignedInstallerCopy

    Write-Host 'Writing SHA256SUMS after signing...'
    & (Join-Path $PSScriptRoot 'new-release-manifest.ps1') -AssetDirectory $assetRoot
}
finally {
    $securePassword = $null
    if (Test-Path -LiteralPath $unsignedInstallerCopy) {
        Remove-Item -LiteralPath $unsignedInstallerCopy -Force
    }
}
