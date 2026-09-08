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
Import-Module Microsoft.PowerShell.Utility -ErrorAction Stop
Import-Module Microsoft.PowerShell.Security -ErrorAction Stop
$configuration = Get-Content -LiteralPath $ConfigurationFile -Raw | ConvertFrom-Json
$password = Import-Clixml -LiteralPath $configuration.passwordFile
$inputPaths = @($InputFile | ForEach-Object { (Resolve-Path -LiteralPath $_).Path })

if ($VelopackCallback) {
    foreach ($path in $inputPaths) {
        $name = Split-Path $path -Leaf
        if ($name -like '*-Setup.exe' -or $name -eq 'ThisIsMyPC-win.msi') { continue }
        if ([IO.Path]::GetExtension($name) -notin '.exe', '.dll') {
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
    $isInstalledExecutable = [IO.Path]::GetExtension($name) -in '.exe', '.dll'
    $isContainer = $Container -and ($name -eq 'ThisIsMyPC-win.msi' -or $name -match '^ThisIsMyPC-Installer-.+\.exe$')
    if (-not $isInstalledExecutable -and -not $isContainer) {
        throw "Refusing to sign an unexpected release file: $path"
    }
    if ((Get-AuthenticodeSignature -LiteralPath $path).Status -ne 'NotSigned') {
        throw "Refusing to add another signature to $path."
    }
}

$isMsiContainer = $Container -and $inputPaths.Count -eq 1 -and
    [IO.Path]::GetExtension($inputPaths[0]) -eq '.msi'
if (-not $isMsiContainer) {
    & (Join-Path $PSScriptRoot 'invoke-esigner-malware-scan.ps1') `
        -InputFile $inputPaths `
        -CodeSignToolArchive $configuration.codeSignToolArchive `
        -CredentialId $configuration.credentialId `
        -ProgramName $configuration.signingDescription `
        -Username $configuration.username `
        -Password $password
}

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

if ($isMsiContainer) {
    & (Join-Path $PSScriptRoot 'invoke-esigner-msi-sign.ps1') `
        -InputFile $inputPaths[0] `
        -CodeSignToolArchive $configuration.codeSignToolArchive `
        -CredentialId $configuration.credentialId `
        -ProgramName $configuration.signingDescription `
        -Username $configuration.username `
        -Password $password
}
else {
    $unsignedHashes = @{}
    $backupPaths = @{}
    $backupRoot = Join-Path ([IO.Path]::GetTempPath()) `
        ("thisismypc-signing-backup-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $backupRoot | Out-Null
    for ($index = 0; $index -lt $inputPaths.Count; $index++) {
        $path = $inputPaths[$index]
        $unsignedHashes[$path] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        $backupPath = Join-Path $backupRoot ("$index-" + (Split-Path $path -Leaf))
        Copy-Item -LiteralPath $path -Destination $backupPath
        $backupPaths[$path] = $backupPath
    }

    # SSL.com's scan endpoint can report approval before CKA can read it. Retry only
    # the exact unchanged unsigned input. This prevents accidental double signing.
    try {
        $approvalRetryDelays = @(5, 15, 30)
        $signingAttempt = 0
        while ($true) {
            $signingAttempt++
            $savedErrorActionPreference = $ErrorActionPreference
            try {
                # Windows PowerShell converts native stderr into error records. Continue
                # long enough to capture SignTool's text and inspect its exit code.
                $ErrorActionPreference = 'Continue'
                $signArguments = @(
                    'sign',
                    '/fd', 'sha256',
                    '/tr', $configuration.timestampUrl,
                    '/td', 'sha256',
                    '/d', $configuration.signingDescription,
                    '/sha1', $configuration.thumbprint
                )
                $signArguments += $inputPaths
                $signingOutput = @(& $signTool @signArguments 2>&1)
                $signingExitCode = $LASTEXITCODE
            }
            finally {
                $ErrorActionPreference = $savedErrorActionPreference
            }
            $signingOutput | ForEach-Object { Write-Host $_ }

            if ($signingExitCode -eq 0) { break }

            # SignTool can prepare an MSI for signing before CKA rejects the digest.
            # Restore every input so another attempt signs the scanned bytes again.
            foreach ($path in $inputPaths) {
                $backupPath = $backupPaths[$path]
                $backupHash = (Get-FileHash -LiteralPath $backupPath -Algorithm SHA256).Hash
                if ($backupHash -ne $unsignedHashes[$path]) {
                    throw "Unsigned signing backup changed: $backupPath"
                }
                Copy-Item -LiteralPath $backupPath -Destination $path -Force
                $restoredHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
                if ($restoredHash -ne $unsignedHashes[$path] -or
                    (Get-AuthenticodeSignature -LiteralPath $path).Status -ne 'NotSigned') {
                    throw "Could not restore the unsigned release file after SignTool failed: $path"
                }
            }

            $approvalPending = ($signingOutput -join "`n") -match `
                'hash needs to be scanned first before submitting for signing:'
            $retryIndex = $signingAttempt - 1
            if (-not $approvalPending -or $retryIndex -ge $approvalRetryDelays.Count) {
                throw 'signtool failed on a release signing batch.'
            }

            $delay = $approvalRetryDelays[$retryIndex]
            Write-Host "eSigner approval is not visible to CKA yet. Retrying the restored file in $delay seconds."
            Start-Sleep -Seconds $delay
        }
    }
    finally {
        if (Test-Path -LiteralPath $backupRoot) {
            Remove-Item -LiteralPath $backupRoot -Recurse -Force
        }
    }
}

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
