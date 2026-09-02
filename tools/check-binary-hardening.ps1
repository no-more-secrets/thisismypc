# Reads the exploit-mitigation state straight from PE headers, so the answer
# comes from the shipped file, not from build flags. One row per file:
#   ASLR      IMAGE_DLLCHARACTERISTICS_DYNAMIC_BASE (relocatable image)
#   HighEnt   HIGH_ENTROPY_VA (64-bit ASLR address space)
#   DEP       NX_COMPAT (non-executable data pages)
#   CFG       GUARD_CF plus a load config with the CF function table
#   GS        /GS stack cookie: load config SecurityCookie set (canary on
#             native frames; managed frames have no buffer to smash)
#   CET       shadow stack compatible (IMAGE_DEBUG_TYPE_EX_DLLCHARACTERISTICS
#             CET_COMPAT), the modern return-address protection
#   SEH       NO_SEH or x64 (table-based unwinding; no SEH handler chain)
# Stack guard pages are not a file property: Windows places one below every
# thread stack regardless. Exit code 1 when any first-party file misses a
# required mitigation.
param(
    [Parameter(Mandatory = $true, ValueFromRemainingArguments = $true)]
    [string[]]$Path,

    # Names (no path) held to the full set; everything else is reported only.
    [string[]]$Require = @('ThisIsMyPC.App.exe', 'ThisIsMyPC.Service.exe', 'ThisIsMyPC-Installer.exe')
)

$ErrorActionPreference = 'Stop'

function Read-Pe([string]$file) {
    $b = [System.IO.File]::ReadAllBytes($file)
    $peOffset = [BitConverter]::ToInt32($b, 0x3C)
    if ([BitConverter]::ToUInt32($b, $peOffset) -ne 0x00004550) { throw "$file is not a PE image" }
    $coff = $peOffset + 4
    $machine = [BitConverter]::ToUInt16($b, $coff)
    $sectionCount = [BitConverter]::ToUInt16($b, $coff + 2)
    $optSize = [BitConverter]::ToUInt16($b, $coff + 16)
    $opt = $coff + 20
    $magic = [BitConverter]::ToUInt16($b, $opt)
    $pe32Plus = $magic -eq 0x20B
    $dllChars = [BitConverter]::ToUInt16($b, $opt + $(if ($pe32Plus) { 70 } else { 70 }))
    $dataDirOffset = $opt + $(if ($pe32Plus) { 112 } else { 96 })
    function Get-DataDir([int]$index) {
        $o = $dataDirOffset + $index * 8
        [pscustomobject]@{ Rva = [BitConverter]::ToUInt32($b, $o); Size = [BitConverter]::ToUInt32($b, $o + 4) }
    }
    $sections = @()
    $sectionTable = $opt + $optSize
    for ($i = 0; $i -lt $sectionCount; $i++) {
        $s = $sectionTable + $i * 40
        $sections += [pscustomobject]@{
            VirtualAddress = [BitConverter]::ToUInt32($b, $s + 12)
            VirtualSize    = [BitConverter]::ToUInt32($b, $s + 8)
            RawPointer     = [BitConverter]::ToUInt32($b, $s + 20)
            RawSize        = [BitConverter]::ToUInt32($b, $s + 16)
        }
    }
    function RvaToOffset([uint32]$rva) {
        foreach ($s in $sections) {
            if ($rva -ge $s.VirtualAddress -and $rva -lt $s.VirtualAddress + [Math]::Max($s.VirtualSize, $s.RawSize)) {
                return $rva - $s.VirtualAddress + $s.RawPointer
            }
        }
        return -1
    }

    # Load config (directory 10): SecurityCookie and GuardFlags.
    $securityCookie = 0; $guardFlags = 0
    $lc = Get-DataDir 10
    if ($lc.Rva -ne 0) {
        $o = RvaToOffset $lc.Rva
        if ($o -ge 0) {
            $lcSize = [BitConverter]::ToUInt32($b, $o)
            if ($pe32Plus) {
                if ($lcSize -ge 0x60) { $securityCookie = [BitConverter]::ToUInt64($b, $o + 0x58) }
                if ($lcSize -ge 0x94) { $guardFlags = [BitConverter]::ToUInt32($b, $o + 0x90) }
            } else {
                if ($lcSize -ge 0x44) { $securityCookie = [BitConverter]::ToUInt32($b, $o + 0x3C) }
                if ($lcSize -ge 0x5C) { $guardFlags = [BitConverter]::ToUInt32($b, $o + 0x58) }
            }
        }
    }

    # Debug directory (6): type 20 = extended DLL characteristics (CET).
    $exChars = 0
    $dbg = Get-DataDir 6
    if ($dbg.Rva -ne 0) {
        $o = RvaToOffset $dbg.Rva
        if ($o -ge 0) {
            for ($e = 0; $e -lt [int]($dbg.Size / 28); $e++) {
                $entry = $o + $e * 28
                $type = [BitConverter]::ToUInt32($b, $entry + 12)
                if ($type -eq 20) {
                    $raw = [BitConverter]::ToUInt32($b, $entry + 24)
                    if ($raw -ne 0) { $exChars = [BitConverter]::ToUInt32($b, $raw) }
                }
            }
        }
    }

    [pscustomobject]@{
        File    = Split-Path $file -Leaf
        x64     = ($machine -eq 0x8664)
        ASLR    = [bool]($dllChars -band 0x0040)
        HighEnt = [bool]($dllChars -band 0x0020)
        DEP     = [bool]($dllChars -band 0x0100)
        CFG     = ([bool]($dllChars -band 0x4000)) -and ([bool]($guardFlags -band 0x0400))
        GS      = ($securityCookie -ne 0)
        CET     = [bool]($exChars -band 0x01)
        SEH     = ([bool]($dllChars -band 0x0400)) -or ($machine -eq 0x8664)
    }
}

$rows = foreach ($p in $Path) {
    foreach ($f in (Get-ChildItem $p -File)) { Read-Pe $f.FullName }
}
$rows | Format-Table -AutoSize | Out-String -Width 200 | Write-Host

$failed = @()
foreach ($r in $rows) {
    if ($Require -notcontains $r.File) { continue }
    $missing = @('ASLR', 'HighEnt', 'DEP', 'CFG', 'GS', 'SEH') | Where-Object { -not $r.$_ }
    if ($missing) { $failed += "$($r.File): missing $($missing -join ', ')" }
}
if ($failed) {
    $failed | ForEach-Object { Write-Host "FAIL $_" }
    exit 1
}
Write-Host 'All first-party binaries carry ASLR, high-entropy VA, DEP, CFG, /GS, and table-based unwinding.'
exit 0
