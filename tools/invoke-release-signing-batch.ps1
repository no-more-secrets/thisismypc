param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$ConfigurationFile,

    [Parameter(Mandatory, ValueFromRemainingArguments)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string[]]$InputFile,

    [switch]$Container,

    [switch]$VelopackCallback
)

$ErrorActionPreference = 'Stop'
$configuration = Get-Content -LiteralPath $ConfigurationFile -Raw | ConvertFrom-Json
$password = Import-Clixml -LiteralPath $configuration.passwordFile
$inputPaths = @($InputFile | ForEach-Object { (Resolve-Path -LiteralPath $_).Path })

if ($VelopackCallback) {
    foreach ($path in $inputPaths) {
        $name = Split-Path $path -Leaf
        if ($name -like '*-Setup.exe' -or $name -eq 'ThisIsMyPC-win.msi') { continue }
        if ($name -ne 'Squirrel.exe' -and $name -notmatch '^ThisIsMyPC(?:\..+)?\.(?:exe|dll)$') {
            throw "Velopack requested signing for an unexpected file: $path"
        }
    }
    # Velopack invokes the callback for Setup.exe and the MSI regardless of
    # signExclude. Setup.exe is discarded. The MSI is normalized and signed
    # separately after Velopack finishes, so both are intentional no-ops here.
    $inputPaths = @($inputPaths | Where-Object {
        $name = Split-Path $_ -Leaf
        $name -notlike '*-Setup.exe' -and $name -ne 'ThisIsMyPC-win.msi'
    })
    if ($inputPaths.Count -eq 0) { return }
}

foreach ($path in $inputPaths) {
    $name = Split-Path $path -Leaf
    $isInstalledExecutable = $name -in @('Update.exe', 'Squirrel.exe') -or
        $name -match '^ThisIsMyPC(?:\..+)?\.(?:exe|dll)$'
    $isContainer = $Container -and ($name -eq 'ThisIsMyPC-win.msi' -or $name -match '^ThisIsMyPC-Installer-.+\.exe$')
    if (-not $isInstalledExecutable -and -not $isContainer) {
        throw "Refusing to sign an unexpected release file: $path"
    }
    if ((Get-AuthenticodeSignature -LiteralPath $path).Status -ne 'NotSigned') {
        throw "Refusing to add another signature to $path."
    }
}

& (Join-Path $PSScriptRoot 'invoke-esigner-malware-scan.ps1') `
    -InputFile $inputPaths `
    -CodeSignToolArchive $configuration.codeSignToolArchive `
    -CredentialId $configuration.credentialId `
    -ProgramName $configuration.signingDescription `
    -Username $configuration.username `
    -Password $password

$manifest = Get-Content -LiteralPath `
    (Join-Path $PSScriptRoot 'reproducible-build-environment.json') -Raw | ConvertFrom-Json
$programFilesX86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)
$signTool = Join-Path $programFilesX86 `
    "Windows Kits\10\bin\$($manifest.windowsSdkVersion)\x64\signtool.exe"
if (-not (Test-Path -LiteralPath $signTool -PathType Leaf)) {
    throw "Pinned signtool.exe is missing: $signTool"
}
$actualHash = (Get-FileHash -LiteralPath $signTool -Algorithm SHA256).Hash
if ($actualHash -ne $manifest.signToolSha256) {
    throw "signtool.exe hash is $actualHash, expected $($manifest.signToolSha256)."
}

& $signTool sign /fd sha256 /tr $configuration.timestampUrl /td sha256 `
    /d $configuration.signingDescription /sha1 $configuration.thumbprint @inputPaths
if ($LASTEXITCODE -ne 0) { throw 'signtool failed on a release signing batch.' }

foreach ($path in $inputPaths) {
    & $signTool verify /pa /all $path
    if ($LASTEXITCODE -ne 0) { throw "signtool verification failed: $path" }
    $signature = Get-AuthenticodeSignature -LiteralPath $path
    if ($signature.Status -ne 'Valid' -or
        $signature.SignerCertificate.Thumbprint -ne $configuration.thumbprint -or
        $null -eq $signature.TimeStamperCertificate) {
        throw "Release signature or timestamp validation failed: $path"
    }
    Add-Content -LiteralPath $configuration.auditFile -Value $path -Encoding UTF8
}
