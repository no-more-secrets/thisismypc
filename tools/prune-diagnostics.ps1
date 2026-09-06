# Removes stale diagnostic build copies. Keeps reports, screenshots, and annotations.
[CmdletBinding(SupportsShouldProcess)]
param([ValidateRange(1,8760)][int]$RetentionHours = 24)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
$root = Join-Path $repo 'artifacts\diagnostics'
if (-not (Test-Path -LiteralPath $root)) { return }
# Refuse linked ancestors and descendants before any recursive removal.
foreach ($path in @($repo, (Join-Path $repo 'artifacts'), $root)) {
    if ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked root: $path" }
}
$tracked = @(git -C $repo ls-files -- artifacts/diagnostics)
if ($LASTEXITCODE -ne 0) { throw 'Cannot check tracked files.' }
$cutoff = [DateTime]::UtcNow.AddHours(-$RetentionHours)
$reclaimed = 0L
foreach ($run in Get-ChildItem -LiteralPath $root -Directory -Force) {
    if ($run.Attributes -band [IO.FileAttributes]::ReparsePoint) { continue }
    $entries = @(Get-ChildItem -LiteralPath $run.FullName -Recurse -Force)
    if ($entries | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { continue }
    $latest = ($entries + $run | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1).LastWriteTimeUtc
    if ($latest -ge $cutoff) { continue }
    $targets = @($entries | Where-Object { $_.PSIsContainer -and $_.Name -in 'bin','obj' } | Sort-Object { $_.FullName.Length })
    foreach ($target in $targets) {
        if (-not (Test-Path -LiteralPath $target.FullName)) { continue }
        $full = [IO.Path]::GetFullPath($target.FullName)
        if (-not $full.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Outside diagnostic root: $full" }
        $relative = $full.Substring($repo.Length + 1).Replace('\','/') + '/'
        if ($tracked | Where-Object { $_.StartsWith($relative, [StringComparison]::OrdinalIgnoreCase) }) { throw "Tracked content: $full" }
        $files = @(Get-ChildItem -LiteralPath $full -Recurse -Force -File)
        $bytes = ($files | Measure-Object Length -Sum).Sum
        if ($PSCmdlet.ShouldProcess($full, "Delete stale build output ($bytes bytes)")) {
            try {
                Remove-Item -LiteralPath $full -Recurse -Force -ErrorAction Stop
                $reclaimed += $bytes
                Write-Output "Removed $full"
            } catch { Write-Warning "Cleanup incomplete for ${full}: $_" }
        }
    }
}
Write-Output ('Removed complete build folders totaling {0:N2} GiB.' -f ($reclaimed / 1GB))
