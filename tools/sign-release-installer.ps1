param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })]
    [string]$AssetDirectory,

    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })]
    [string]$StagingDirectory,

    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$InstallerStub,

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

    [string]$Authors = 'NMS',

    [string]$ESignerUsername = $env:ESIGNER_USERNAME,

    [ValidateNotNullOrEmpty()]
    [string]$SigningDescription = 'ThisIsMyPC',

    [string]$TimestampUrl = 'http://ts.ssl.com'
)

$ErrorActionPreference = 'Stop'

# A CI runner supplies the password only to this short signing process. Convert
# it immediately and remove it from the environment before starting a child.
$securePassword = $null
if (Test-Path Env:ESIGNER_PASSWORD) {
    $securePassword = ConvertTo-SecureString $env:ESIGNER_PASSWORD -AsPlainText -Force
    Remove-Item Env:ESIGNER_PASSWORD
}
if ([string]::IsNullOrWhiteSpace($ESignerUsername)) {
    $ESignerUsername = Read-Host 'SSL.com eSigner username'
}
if ([string]::IsNullOrWhiteSpace($ESignerUsername)) {
    throw 'SSL.com eSigner username is required.'
}

$assetRoot = (Resolve-Path -LiteralPath $AssetDirectory).Path
$stagingRoot = (Resolve-Path -LiteralPath $StagingDirectory).Path
$stubPath = (Resolve-Path -LiteralPath $InstallerStub).Path
$installerAsset = Join-Path $assetRoot "ThisIsMyPC-Installer-$Version.exe"
if (-not (Test-Path -LiteralPath $installerAsset -PathType Leaf)) {
    throw "Unsigned release installer is missing: $installerAsset"
}
if ((Get-AuthenticodeSignature -LiteralPath $installerAsset).Status -ne 'NotSigned') {
    throw 'Release installer must be unsigned before the eSigner signing step.'
}

& (Join-Path $PSScriptRoot 'test-esigner-signing-environment.ps1') `
    -CodeSignToolArchive $CodeSignToolArchive

$thumbprint = $SignThumbprint.ToUpperInvariant()
$certificates = @(Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Thumbprint -eq $thumbprint })
if ($certificates.Count -ne 1) {
    throw "Expected one certificate with thumbprint $thumbprint in Cert:\CurrentUser\My, found $($certificates.Count). Is eSigner CKA loaded?"
}
$certificate = $certificates[0]
if (-not $certificate.HasPrivateKey) {
    throw 'Certificate found but its eSigner CKA private key is not reachable.'
}
$signerName = $certificate.GetNameInfo(
    [Security.Cryptography.X509Certificates.X509NameType]::SimpleName,
    $false)
if ($signerName -ne 'No More Secrets, LLC') {
    throw "Refusing unexpected signing identity: $signerName"
}
$ekuExtension = $certificate.Extensions |
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
if ($now -lt $certificate.NotBefore -or $now -gt $certificate.NotAfter) {
    throw 'Selected signing certificate is not currently valid.'
}

$powerShell = Join-Path ([Environment]::SystemDirectory) 'WindowsPowerShell\v1.0\powershell.exe'
$programFiles = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)
$dotnet = Join-Path $programFiles 'dotnet\dotnet.exe'
foreach ($hostTool in @(
    @{ Path = $powerShell; Signer = 'Microsoft Windows' },
    @{ Path = $dotnet; Signer = '.NET' }
)) {
    if (-not (Test-Path -LiteralPath $hostTool.Path -PathType Leaf)) {
        throw "Required signing host is missing: $($hostTool.Path)"
    }
    $hostSignature = Get-AuthenticodeSignature -LiteralPath $hostTool.Path
    $hostSigner = if ($null -ne $hostSignature.SignerCertificate) {
        $hostSignature.SignerCertificate.GetNameInfo(
            [Security.Cryptography.X509Certificates.X509NameType]::SimpleName,
            $false)
    }
    if ($hostSignature.Status -ne 'Valid' -or $hostSigner -cne $hostTool.Signer) {
        throw "Refusing untrusted signing host $($hostTool.Path): $hostSigner"
    }
}
$buildEnvironment = Get-Content -LiteralPath `
    (Join-Path $PSScriptRoot 'reproducible-build-environment.json') -Raw | ConvertFrom-Json
$actualSdk = (& $dotnet --version).Trim()
if ($LASTEXITCODE -ne 0 -or $actualSdk -ne $buildEnvironment.dotnetSdk) {
    throw "Expected .NET SDK $($buildEnvironment.dotnetSdk); found $actualSdk."
}
if (-not $securePassword) {
    $securePassword = Read-Host 'SSL.com eSigner account password' -AsSecureString
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) `
    ('thisismypc-signing-' + [guid]::NewGuid().ToString('N'))
$signedOutput = Join-Path $temporaryRoot 'velopack'
$passwordFile = Join-Path $temporaryRoot 'password.clixml'
$configurationFile = Join-Path $temporaryRoot 'signing.json'
$auditFile = Join-Path $temporaryRoot 'signed-files.txt'
$unsignedInstaller = Join-Path $temporaryRoot 'unsigned-installer.exe'
$wrapper = Join-Path $PSScriptRoot 'invoke-release-signing-batch.ps1'

try {
    New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
    New-Item -ItemType Directory -Path $signedOutput | Out-Null
    Copy-Item -LiteralPath $installerAsset -Destination $unsignedInstaller
    $securePassword | Export-Clixml -LiteralPath $passwordFile
    @{
        auditFile = $auditFile
        codeSignToolArchive = (Resolve-Path -LiteralPath $CodeSignToolArchive).Path
        credentialId = $ESignerCredentialId
        passwordFile = $passwordFile
        signingDescription = $SigningDescription
        thumbprint = $thumbprint
        timestampUrl = $TimestampUrl
        username = $ESignerUsername
    } | ConvertTo-Json | Set-Content -LiteralPath $configurationFile -Encoding UTF8

    # Velopack creates two privileged helpers in addition to copying our app
    # and service. Its callback scans and signs the exact bytes it will pack.
    $signTemplate = "`"$powerShell`" -NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$wrapper`" -ConfigurationFile `"$configurationFile`" -VelopackCallback {{file...}}"
    $signExclude = '(?i)^(?!.*(?:ThisIsMyPC(?:\..+)?\.(?:exe|dll)|Squirrel\.exe)$).*'
    Write-Host 'Repacking while scanning and signing every first-party installed executable...'
    & $dotnet tool run vpk -- pack `
        --packId ThisIsMyPC `
        --packVersion $Version `
        --packDir $stagingRoot `
        --mainExe ThisIsMyPC.App.exe `
        --packTitle ThisIsMyPC `
        --packAuthors $Authors `
        --msi --instLocation PerMachine `
        --noPortable `
        --outputDir $signedOutput `
        --signTemplate $signTemplate `
        --signExclude $signExclude `
        --signParallel 100
    if ($LASTEXITCODE -ne 0) { throw 'Signed vpk pack failed.' }

    $setupExe = @(Get-ChildItem -LiteralPath $signedOutput -Filter '*-Setup.exe')
    if ($setupExe.Count -gt 0) { $setupExe | Remove-Item -Force }
    $assetsJson = Join-Path $signedOutput 'assets.win.json'
    if (Test-Path -LiteralPath $assetsJson) {
        $assets = Get-Content -LiteralPath $assetsJson -Raw | ConvertFrom-Json
        @($assets | Where-Object { $_.Type -ne 'Installer' }) |
            ConvertTo-Json -Compress |
            Set-Content -LiteralPath $assetsJson -Encoding UTF8
    }

    $signedMsi = Join-Path $signedOutput 'ThisIsMyPC-win.msi'
    if (-not (Test-Path -LiteralPath $signedMsi -PathType Leaf)) {
        throw 'Signed pack did not produce ThisIsMyPC-win.msi.'
    }
    & (Join-Path $PSScriptRoot 'normalize-msi.ps1') -Path $signedMsi -Version $Version
    & $wrapper -ConfigurationFile $configurationFile -Container -InputFile $signedMsi

    foreach ($item in Get-ChildItem -LiteralPath $signedOutput -Force) {
        Copy-Item -LiteralPath $item.FullName -Destination $assetRoot -Recurse -Force
    }

    Import-Module (Join-Path $PSScriptRoot 'InstallerBundle.psm1') -Force
    Add-InstallerPayload -StubPath $stubPath -PayloadPath $signedMsi -OutputPath $installerAsset | Out-Null
    & $wrapper -ConfigurationFile $configurationFile -Container -InputFile $installerAsset

    Write-Host 'Comparing the complete signed release with the preserved unsigned build...'
    & (Join-Path $PSScriptRoot 'compare-reproducible-installer.ps1') `
        -ReleasedInstaller $installerAsset `
        -LocalInstaller $unsignedInstaller

    $signedObjects = @(Get-Content -LiteralPath $auditFile | Sort-Object -Unique)
    Write-Host "Signing completed: $($signedObjects.Count) objects, approximately $($signedObjects.Count) SSL.com signing credits."
    $signedObjects | ForEach-Object { Write-Host "  $(Split-Path $_ -Leaf)" }

    Write-Host 'Writing SHA256SUMS after signing...'
    & (Join-Path $PSScriptRoot 'new-release-manifest.ps1') -AssetDirectory $assetRoot
}
finally {
    $securePassword = $null
    if (Test-Path -LiteralPath $temporaryRoot -PathType Container) {
        $resolvedTemporary = [IO.Path]::GetFullPath($temporaryRoot)
        $systemTemporary = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if (-not $resolvedTemporary.StartsWith($systemTemporary, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Refusing to remove an unexpected signing directory.'
        }
        Remove-Item -LiteralPath $resolvedTemporary -Recurse -Force
    }
}
