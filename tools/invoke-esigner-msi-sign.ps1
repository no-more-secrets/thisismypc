param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$InputFile,

    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$CodeSignToolArchive,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F-]{36}$')]
    [string]$CredentialId,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$ProgramName,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$Username,

    [Parameter(Mandatory)]
    [Security.SecureString]$Password
)

$ErrorActionPreference = 'Stop'
Import-Module Microsoft.PowerShell.Utility -ErrorAction Stop
$manifest = Get-Content -LiteralPath `
    (Join-Path $PSScriptRoot 'esigner-signing-environment.json') -Raw | ConvertFrom-Json
$archivePath = (Resolve-Path -LiteralPath $CodeSignToolArchive).Path
$inputPath = (Resolve-Path -LiteralPath $InputFile).Path
if ([IO.Path]::GetExtension($inputPath) -ne '.msi') {
    throw "The integrated signer is restricted to MSI files: $inputPath"
}
$archiveHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $archivePath).Hash
if ($archiveHash -ne $manifest.codeSignTool.archiveSha256) {
    throw "CodeSignTool archive hash is $archiveHash, expected $($manifest.codeSignTool.archiveSha256)."
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) `
    ("thisismypc-msi-signing-" + [guid]::NewGuid().ToString('N'))
$passwordPointer = [IntPtr]::Zero
$plainPassword = $null
$arguments = $null
try {
    $toolRoot = Join-Path $temporaryRoot 'tool'
    $outputDirectory = Join-Path $temporaryRoot 'output'
    New-Item -ItemType Directory -Path $toolRoot | Out-Null
    New-Item -ItemType Directory -Path $outputDirectory | Out-Null
    Expand-Archive -LiteralPath $archivePath -DestinationPath $toolRoot
    $java = Join-Path $toolRoot $manifest.codeSignTool.javaRelativePath
    $jar = Join-Path $toolRoot $manifest.codeSignTool.jarRelativePath
    foreach ($tool in @(
        @{ Path = $java; Hash = $manifest.codeSignTool.javaSha256 },
        @{ Path = $jar; Hash = $manifest.codeSignTool.jarSha256 }
    )) {
        if (-not (Test-Path -LiteralPath $tool.Path -PathType Leaf)) {
            throw "CodeSignTool archive is missing $($tool.Path)."
        }
        $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $tool.Path).Hash
        if ($actualHash -ne $tool.Hash) {
            throw "Extracted CodeSignTool file hash differs from the pin: $($tool.Path)."
        }
    }

    $passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password)
    $plainPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer)
    if ([string]::IsNullOrWhiteSpace($plainPassword)) {
        throw 'SSL.com eSigner account password is required.'
    }

    $inputHash = (Get-FileHash -LiteralPath $inputPath -Algorithm SHA256).Hash
    $arguments = @(
        '-jar',
        $jar,
        'sign',
        "-credential_id=$CredentialId",
        "-input_file_path=$inputPath",
        '-malware_block',
        "-output_dir_path=$outputDirectory",
        "-password=$plainPassword",
        "-program_name=$ProgramName",
        "-username=$Username"
    )
    Write-Host "Scanning and signing MSI with eSigner: $(Split-Path $inputPath -Leaf)"
    Write-Host 'Enter the current eSigner OTP when CodeSignTool asks for it.'
    Push-Location $toolRoot
    try {
        & $java @arguments
        if ($LASTEXITCODE -ne 0) {
            throw "CodeSignTool MSI signing failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
    if ((Get-FileHash -LiteralPath $inputPath -Algorithm SHA256).Hash -ne $inputHash) {
        throw "CodeSignTool changed its unsigned input: $inputPath"
    }

    $signedPath = Join-Path $outputDirectory (Split-Path $inputPath -Leaf)
    if (-not (Test-Path -LiteralPath $signedPath -PathType Leaf)) {
        throw "CodeSignTool did not create the expected signed MSI: $signedPath"
    }
    Copy-Item -LiteralPath $signedPath -Destination $inputPath -Force
}
finally {
    $arguments = $null
    $plainPassword = $null
    $Password = $null
    if ($passwordPointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer)
    }
    if (Test-Path -LiteralPath $temporaryRoot) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
