param(
    [switch]$Clear
)

$ErrorActionPreference = 'Stop'
$credentialDirectory = Join-Path `
    ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) `
    'ThisIsMyPC\ReleaseSigning'
$credentialPath = Join-Path $credentialDirectory 'esigner-password.clixml'

if ($Clear) {
    if (Test-Path -LiteralPath $credentialPath) {
        Remove-Item -LiteralPath $credentialPath -Force
        Write-Host "Removed the saved eSigner password: $credentialPath"
    }
    else {
        Write-Host 'No saved eSigner password exists.'
    }
    return
}

$password = Read-Host 'SSL.com eSigner account password' -AsSecureString
if (-not $password -or $password.Length -eq 0) {
    throw 'SSL.com eSigner account password is required.'
}

New-Item -ItemType Directory -Path $credentialDirectory -Force | Out-Null
$password | Export-Clixml -LiteralPath $credentialPath
$savedPassword = Import-Clixml -LiteralPath $credentialPath
if ($savedPassword -isnot [Security.SecureString] -or $savedPassword.Length -eq 0) {
    Remove-Item -LiteralPath $credentialPath -Force -ErrorAction SilentlyContinue
    throw 'The saved eSigner password could not be verified.'
}

Write-Host "Saved with Windows user-scoped DPAPI: $credentialPath" -ForegroundColor Green
Write-Host 'Run this script with -Clear to remove it.'
