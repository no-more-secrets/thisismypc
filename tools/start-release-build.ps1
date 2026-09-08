[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string]$Version,

    [string]$Authors,

    [ValidateSet('Signed', 'Unsigned')]
    [string]$Mode,

    [ValidatePattern('^[0-9A-Fa-f]{40}$')]
    [string]$SignThumbprint,

    [ValidatePattern('^[0-9a-fA-F-]{36}$')]
    [string]$ESignerCredentialId,

    [string]$CodeSignToolArchive,

    [string]$ESignerUsername,

    [string]$SigningDescription,

    [string]$TimestampUrl
)

$ErrorActionPreference = 'Stop'
$Host.UI.RawUI.WindowTitle = 'ThisIsMyPC release build'

function Read-RequiredValue {
    param(
        [Parameter(Mandatory)]
        [string]$Prompt,

        [Parameter(Mandatory)]
        [scriptblock]$IsValid,

        [Parameter(Mandatory)]
        [string]$InvalidMessage
    )

    while ($true) {
        $value = Read-Host $Prompt
        if ($null -eq $value) {
            throw 'The input stream closed before the prompt was answered.'
        }
        $value = $value.Trim()
        if (& $IsValid $value) {
            return $value
        }
        Write-Warning $InvalidMessage
    }
}

function Read-DefaultValue {
    param(
        [Parameter(Mandatory)]
        [string]$Prompt,

        [Parameter(Mandatory)]
        [string]$DefaultValue
    )

    $value = Read-Host "$Prompt [$DefaultValue]"
    if ($null -eq $value) {
        throw 'The input stream closed before the prompt was answered.'
    }
    $value = $value.Trim()
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $DefaultValue
    }
    return $value
}

function ConvertTo-ReleaseVersionInput {
    param([string]$Value)

    return $Value.Trim() -replace '[\u2010-\u2015\u2212]', '-'
}

try {
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Read-RequiredValue `
        -Prompt 'Version, for example 1.0.0' `
        -IsValid { param($value) (ConvertTo-ReleaseVersionInput $value) -match '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$' } `
        -InvalidMessage 'Enter a semantic version such as 1.0.0 or 1.0.0-preview.1.'
    $Version = ConvertTo-ReleaseVersionInput $Version
}

if (-not $PSBoundParameters.ContainsKey('Authors')) {
    $Authors = Read-DefaultValue -Prompt 'Package author' -DefaultValue 'NMS'
}
if ([string]::IsNullOrWhiteSpace($Authors)) {
    throw 'Package author cannot be empty.'
}

if ([string]::IsNullOrWhiteSpace($Mode)) {
    $modeInput = Read-DefaultValue -Prompt 'Build mode: Signed or Unsigned' -DefaultValue 'Signed'
    $Mode = switch -Regex ($modeInput) {
        '^(?i:s|signed)$' { 'Signed'; break }
        '^(?i:u|unsigned)$' { 'Unsigned'; break }
        default { throw 'Build mode must be Signed or Unsigned.' }
    }
}

$buildParameters = @{
    Version = $Version
    Authors = $Authors
}

if ($Mode -eq 'Signed') {
    if ([string]::IsNullOrWhiteSpace($SignThumbprint)) {
        $matchingCertificates = @(
            Get-ChildItem Cert:\CurrentUser\My |
                Where-Object {
                    $_.GetNameInfo(
                        [Security.Cryptography.X509Certificates.X509NameType]::SimpleName,
                        $false) -eq 'No More Secrets, LLC'
                } |
                Sort-Object NotAfter -Descending
        )
        $defaultThumbprint = if ($matchingCertificates.Count -gt 0) {
            $matchingCertificates[0].Thumbprint
        }
        if ($defaultThumbprint) {
            $SignThumbprint = Read-DefaultValue `
                -Prompt 'Signing certificate thumbprint' `
                -DefaultValue $defaultThumbprint
        } else {
            $SignThumbprint = Read-RequiredValue `
                -Prompt 'Signing certificate thumbprint' `
                -IsValid { param($value) $value -match '^[0-9A-Fa-f]{40}$' } `
                -InvalidMessage 'Enter the 40-character code-signing certificate thumbprint.'
        }
    }
    if ($SignThumbprint -notmatch '^[0-9A-Fa-f]{40}$') {
        throw 'Signing certificate thumbprint must contain 40 hexadecimal characters.'
    }

    if (-not $PSBoundParameters.ContainsKey('ESignerUsername')) {
        if ([string]::IsNullOrWhiteSpace($env:ESIGNER_USERNAME)) {
            $ESignerUsername = Read-RequiredValue `
                -Prompt 'SSL.com username' `
                -IsValid { param($value) -not [string]::IsNullOrWhiteSpace($value) } `
                -InvalidMessage 'SSL.com username is required for a signed build.'
        } else {
            $ESignerUsername = Read-DefaultValue `
                -Prompt 'SSL.com username' `
                -DefaultValue $env:ESIGNER_USERNAME
        }
    }
    if ([string]::IsNullOrWhiteSpace($ESignerUsername)) {
        throw 'SSL.com username is required for a signed build.'
    }

    if (-not $PSBoundParameters.ContainsKey('ESignerCredentialId')) {
        $savedCredentialId = $env:ESIGNER_CREDENTIAL_ID
        if ($savedCredentialId -notmatch '^[0-9a-fA-F-]{36}$') {
            $savedCredentialId = [Environment]::GetEnvironmentVariable(
                'ESIGNER_CREDENTIAL_ID',
                [EnvironmentVariableTarget]::User)
        }
        if ($savedCredentialId -match '^[0-9a-fA-F-]{36}$') {
            $ESignerCredentialId = Read-DefaultValue `
                -Prompt 'eSigner code-signing credential ID' `
                -DefaultValue $savedCredentialId
        } else {
            $ESignerCredentialId = Read-RequiredValue `
                -Prompt 'eSigner code-signing credential ID' `
                -IsValid { param($value) $value -match '^[0-9a-fA-F-]{36}$' } `
                -InvalidMessage 'Enter the 36-character code-signing credential ID.'
        }
    }
    if ($ESignerCredentialId -notmatch '^[0-9a-fA-F-]{36}$') {
        throw 'eSigner credential ID must contain 36 hexadecimal or hyphen characters.'
    }

    if (-not $PSBoundParameters.ContainsKey('CodeSignToolArchive')) {
        $CodeSignToolArchive = Read-Host 'CodeSignTool ZIP override [automatic verified cache]'
    }
    if (-not [string]::IsNullOrWhiteSpace($CodeSignToolArchive)) {
        if (-not (Test-Path -LiteralPath $CodeSignToolArchive -PathType Leaf)) {
            throw "CodeSignTool ZIP does not exist: $CodeSignToolArchive"
        }
        $buildParameters.CodeSignToolArchive = $CodeSignToolArchive
    }

    if (-not $PSBoundParameters.ContainsKey('SigningDescription')) {
        $SigningDescription = Read-DefaultValue `
            -Prompt 'Signing description' `
            -DefaultValue 'ThisIsMyPC'
    }
    if ([string]::IsNullOrWhiteSpace($SigningDescription)) {
        throw 'Signing description cannot be empty.'
    }

    if (-not $PSBoundParameters.ContainsKey('TimestampUrl')) {
        $TimestampUrl = Read-DefaultValue `
            -Prompt 'RFC 3161 timestamp URL' `
            -DefaultValue 'http://ts.ssl.com'
    }
    $timestampUri = $null
    if (-not [Uri]::TryCreate($TimestampUrl, [UriKind]::Absolute, [ref]$timestampUri) -or
        $timestampUri.Scheme -notin 'http', 'https') {
        throw 'Timestamp URL must be an absolute HTTP or HTTPS URL.'
    }

    $buildParameters.SignThumbprint = $SignThumbprint
    $buildParameters.ESignerCredentialId = $ESignerCredentialId
    $buildParameters.ESignerUsername = $ESignerUsername
    $buildParameters.SigningDescription = $SigningDescription
    $buildParameters.TimestampUrl = $TimestampUrl
}

Write-Host ''
Write-Host "Version:     $Version"
Write-Host 'Runtime:     NativeAOT'
Write-Host "Mode:        $Mode"
Write-Host "Author:      $Authors"
if ($Mode -eq 'Signed') {
    Write-Host "Certificate: $SignThumbprint"
    Write-Host "Description: $SigningDescription"
    Write-Host "Timestamp:   $TimestampUrl"
    Write-Host "CodeSignTool: $(if ($buildParameters.ContainsKey('CodeSignToolArchive')) { $CodeSignToolArchive } else { 'automatic verified cache' })"
}
Write-Host ''

$confirmation = Read-DefaultValue -Prompt 'Start this build? Y or N' -DefaultValue 'Y'
if ($confirmation -notmatch '^(?i:y|yes)$') {
    Write-Host 'Build canceled.'
    return
}

& (Join-Path $PSScriptRoot 'build-release.ps1') @buildParameters

$repoRoot = Split-Path $PSScriptRoot -Parent
$installer = Join-Path $repoRoot "artifacts\releases\$Version\ThisIsMyPC-Installer-$Version.exe"
Write-Host ''
Write-Host "Release build completed: $installer" -ForegroundColor Green
} finally {
    Write-Host ''
    [void](Read-Host 'Press Enter to close')
}
