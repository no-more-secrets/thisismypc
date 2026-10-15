param([string]$RuntimeDirectory = (Join-Path $PSScriptRoot '../artifacts/lighting-engine/Release'))
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'ReleaseVendorSignatures.psm1') -Force
$testRoot = Join-Path $PSScriptRoot '../artifacts/diagnostics/release-vendor-signatures'
$runRoot = [IO.Path]::GetFullPath((Join-Path $testRoot ([guid]::NewGuid().ToString('N'))))
$staging = Join-Path $runRoot 'staging'
$callback = Join-Path $runRoot 'PreprocessPackDirWin'
New-Item -ItemType Directory -Force (Join-Path $staging 'lighting-engine'),
    (Join-Path $callback 'lighting-engine') | Out-Null

function Assert-Refused([scriptblock]$Action, [string]$ExpectedMessage) {
    try { & $Action | Out-Null }
    catch {
        if ($_.Exception.Message -notlike $ExpectedMessage) { throw }
        return
    }
    throw "Expected refusal: $ExpectedMessage"
}

foreach ($relative in Get-ReleaseVendorRelativePaths) {
    $source = Join-Path $RuntimeDirectory (Split-Path $relative -Leaf)
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Test runtime missing: $source" }
    Copy-Item -LiteralPath $source -Destination (Join-Path $staging $relative)
    Copy-Item -LiteralPath $source -Destination (Join-Path $callback $relative)
}
$records = @(Get-ReleaseVendorFiles $staging)
if ($records.Count -ne 4) { throw 'Expected four validated Microsoft CRT files.' }
$configuration = Join-Path $runRoot 'signing.json'
$audit = Join-Path $runRoot 'signed-files.txt'
@{ preservedVendorFiles = $records; passwordFile = (Join-Path $runRoot 'must-not-load-password');
    auditFile = $audit } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $configuration
$files = @($records | ForEach-Object { Join-Path $callback $_.relativePath })
& (Join-Path $PSScriptRoot 'invoke-release-signing-batch.ps1') `
    -ConfigurationFile $configuration -VelopackCallback -InputFile $files
foreach ($file in $files) {
    if (-not (Test-PreservedReleaseVendorFile $file $records)) { throw 'Vendor callback changed the runtime.' }
}
if (Test-Path -LiteralPath $audit) { throw 'Preserved runtimes must not consume signing credits.' }

$runtime = $files[0]
$badRecords = @($records | ForEach-Object { [pscustomobject]@{
    relativePath = $_.relativePath; sha256 = ('0' * 64)
} })
Assert-Refused { Test-PreservedReleaseVendorFile $runtime $badRecords } '*changed after staging*'
Assert-Refused { Test-PreservedReleaseVendorFile $runtime @() } '*not validated in staging*'
Assert-Refused { Test-PreservedReleaseVendorFile $runtime @([pscustomobject]@{
    relativePath = 'ThisIsMyPC.App.exe'; sha256 = $records[0].sha256
}) } '*Invalid preserved vendor runtime record*'

$renamed = Join-Path $callback 'ThisIsMyPC.App.exe'
Copy-Item -LiteralPath $runtime -Destination $renamed
Assert-Refused {
    & (Join-Path $PSScriptRoot 'invoke-release-signing-batch.ps1') `
        -ConfigurationFile $configuration -VelopackCallback -InputFile @($runtime, $renamed)
} '*Refusing to add another signature*'
Remove-Item -LiteralPath $renamed

$firstParty = Join-Path $callback 'ThisIsMyPC.Test.exe'
Copy-Item -LiteralPath (Join-Path $RuntimeDirectory 'ThisIsMyPC-LightingEngine.exe') -Destination $firstParty
Assert-Refused {
    & (Join-Path $PSScriptRoot 'invoke-release-signing-batch.ps1') `
        -ConfigurationFile $configuration -VelopackCallback -InputFile @($runtime, $firstParty)
} '*must-not-load-password*'
Remove-Item -LiteralPath $firstParty

$windowsHost = Join-Path ([Environment]::SystemDirectory) 'WindowsPowerShell/v1.0/powershell.exe'
Assert-Refused { Assert-ReleaseVendorSignature $windowsHost } '*valid timestamped Microsoft signature*'

$unsigned = Join-Path $runRoot 'unsigned.dll'
[IO.File]::WriteAllText($unsigned, 'not a signed runtime')
Assert-Refused { Assert-ReleaseVendorSignature $unsigned } '*valid timestamped Microsoft signature*'

# The comparison must retain the vendor certificate bytes, not just its PE contents.
$ast = [Management.Automation.Language.Parser]::ParseFile(
    (Join-Path $PSScriptRoot 'compare-reproducible-installer.ps1'), [ref]$null, [ref]$null)
foreach ($name in @('Get-RelativeInventory', 'Get-CanonicalRecords')) {
    $definition = $ast.Find({ param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $true)
    . ([scriptblock]::Create($definition.Extent.Text))
}
$before = @(Get-CanonicalRecords $callback $runRoot $false | Where-Object { $_ -like 'lighting-engine/*' })
$trusted = @(Get-CanonicalRecords $callback $runRoot $true)
if (($before -join "`n") -cne ($trusted -join "`n")) { throw 'Trusted vendor comparison changed file bytes.' }
$bytes = [IO.File]::ReadAllBytes($runtime)
$peOffset = [BitConverter]::ToInt32($bytes, 0x3c)
$optionalHeader = $peOffset + 24
if ([BitConverter]::ToUInt16($bytes, $optionalHeader) -ne 0x20b) { throw 'Expected an x64 CRT fixture.' }
$certificateOffset = [BitConverter]::ToUInt32($bytes, $optionalHeader + 112 + 4 * 8)
if ($certificateOffset -eq 0 -or $certificateOffset + 8 -ge $bytes.Length) { throw 'CRT fixture has no certificate.' }
$bytes[$certificateOffset + 8] = $bytes[$certificateOffset + 8] -bxor 1
[IO.File]::WriteAllBytes($runtime, $bytes)
$after = @(Get-CanonicalRecords $callback $runRoot $false | Where-Object { $_ -like 'lighting-engine/*' })
if (($before -join "`n") -ceq ($after -join "`n")) { throw 'Vendor certificate changes escaped comparison.' }
Assert-Refused { Get-CanonicalRecords $callback $runRoot $true } '*valid timestamped Microsoft signature*'
$changedRecords = @($records | ForEach-Object { [pscustomobject]@{
    relativePath = $_.relativePath; sha256 = (Get-FileHash (Join-Path $callback $_.relativePath)).Hash
} })
Assert-Refused { Test-PreservedReleaseVendorFile $runtime $changedRecords } '*valid timestamped Microsoft signature*'
Write-Host 'Vendor signing regression checks passed: exact preservation, batch filtering, trust rejection, and full-byte comparison.'
