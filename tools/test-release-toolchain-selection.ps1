$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ReleaseToolchain.psm1') -Force
$version = (Get-Content (Join-Path $PSScriptRoot 'reproducible-build-environment.json') -Raw | ConvertFrom-Json).visualStudioVersion
$pinned = [pscustomobject]@{
    installationVersion = $version; isComplete = $true; isLaunchable = $true
    productId = 'Microsoft.VisualStudio.Product.BuildTools'; installationPath = 'C:\Pinned'
}
$newer = [pscustomobject]@{
    installationVersion = '99.0.0.0'; isComplete = $true; isLaunchable = $true
    productId = 'Microsoft.VisualStudio.Product.Community'; installationPath = 'C:\Newer'
}
$sameVersionIde = [pscustomobject]@{
    installationVersion = $version; isComplete = $true; isLaunchable = $true
    productId = 'Microsoft.VisualStudio.Product.Community'; installationPath = 'C:\IDE'
}
$incomplete = [pscustomobject]@{
    installationVersion = $version; isComplete = $false; isLaunchable = $false
    productId = 'Microsoft.VisualStudio.Product.BuildTools'; installationPath = 'C:\Incomplete'
}
foreach ($instances in @(@($newer, $pinned), @($sameVersionIde, $pinned), @($incomplete, $newer, $pinned))) {
    if ((Select-PinnedVisualStudio -Installations $instances -Version $version).installationPath -ne 'C:\Pinned') {
        throw 'Selection did not preserve the exact complete Build Tools installation.'
    }
}
foreach ($instances in @(@($newer), @($incomplete), @())) {
    $refused = $false
    try { Select-PinnedVisualStudio -Installations $instances -Version $version | Out-Null }
    catch { $refused = $true }
    if (-not $refused) { throw 'Missing or incomplete pinned tools must fail closed.' }
}
Write-Host 'Release toolchain selection passed: newer IDE, same-version IDE, incomplete instance, and absent pin.'
