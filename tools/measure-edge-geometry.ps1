
# Measures edge geometry of MainWindow screenshots (dark theme) so page parity
# is verified in pixels, never by reasoning about XAML.
# Usage: .\tools\measure-edge-geometry.ps1 (Get-ChildItem artifacts\ui-shots\walkthrough\*.png).FullName | Format-Table -AutoSize
# Expected on every page: ContentL 25, ContentR 23, LaneFrom 10, ContentT 17 (+/-3 for glyphs);
# ContentR minus LaneTo is the content-to-thumb gap, ~10px by design.
# Base #1a1a2e window bg, Raised #242438 card bg, Outline #3f3f5a border.
Add-Type -AssemblyName System.Drawing

function Test-Bg($c) {
    # Within 3/channel of Base, Raised, or Outline counts as background.
    foreach ($t in @(@(26,26,46), @(36,36,56), @(63,63,90))) {
        if ([Math]::Abs($c.R - $t[0]) -le 3 -and [Math]::Abs($c.G - $t[1]) -le 3 -and [Math]::Abs($c.B - $t[2]) -le 3) { return $true }
    }
    return $false
}
function Test-Base($c) { ($c.R -eq 26) -and ($c.G -eq 26) -and ($c.B -eq 46) }

function Measure-Shot($path) {
    $bmp = [System.Drawing.Bitmap]::FromFile($path)
    try {
        $w = $bmp.Width; $h = $bmp.Height
        $cardTop = -1
        for ($y = 60; $y -lt 300; $y++) {
            if (-not (Test-Base $bmp.GetPixel(700, $y))) {
                $run = 0
                for ($k = $y; $k -lt [Math]::Min($y + 60, $h); $k++) {
                    if (-not (Test-Base $bmp.GetPixel(700, $k))) { $run++ } else { break }
                }
                if ($run -ge 60) { $cardTop = $y; break }
            }
        }
        $rowY = $cardTop + 300
        $cardRight = -1
        for ($x = $w - 1; $x -gt 400; $x--) {
            if (-not (Test-Base $bmp.GetPixel($x, $rowY))) { $cardRight = $x; break }
        }
        $cardLeft = -1
        for ($x = 205; $x -lt 700; $x++) {
            if (-not (Test-Base $bmp.GetPixel($x, $rowY))) { $cardLeft = $x; break }
        }
        $cardBottom = -1
        for ($y = $rowY; $y -lt $h; $y++) {
            if (Test-Base $bmp.GetPixel(700, $y)) { $cardBottom = $y - 1; break }
        }

        # X extremes: rows well clear of the rounded corners; columns clear of the 1px border.
        # The last 18px before the card's right border are treated as the scrollbar lane and
        # tracked separately, so a scrollbar thumb never poses as content (content sits at
        # 23 from the border by design, safely outside the zone).
        $minX = -1; $maxX = -1; $laneMin = -1; $laneMax = -1
        for ($y = $cardTop + 14; $y -lt $cardBottom - 14; $y += 2) {
            for ($x = $cardLeft + 3; $x -lt $cardRight - 2; $x++) {
                if (Test-Bg $bmp.GetPixel($x, $y)) { continue }
                if ($x -ge $cardRight - 18) {
                    if ($laneMin -lt 0 -or $x -lt $laneMin) { $laneMin = $x }
                    if ($x -gt $laneMax) { $laneMax = $x }
                    continue
                }
                if ($minX -lt 0 -or $x -lt $minX) { $minX = $x }
                if ($x -gt $maxX) { $maxX = $x }
            }
        }
        # Y extremes: columns clear of the corners.
        $minY = -1; $maxY = -1
        for ($y = $cardTop + 3; $y -lt $cardBottom - 2; $y++) {
            for ($x = $cardLeft + 14; $x -lt $cardRight - 14; $x += 2) {
                if (Test-Bg $bmp.GetPixel($x, $y)) { continue }
                if ($minY -lt 0) { $minY = $y }
                $maxY = $y
                break
            }
        }

        [PSCustomObject]@{
            Name     = [System.IO.Path]::GetFileNameWithoutExtension($path)
            CardL    = $cardLeft; CardR = $cardRight; CardT = $cardTop; CardB = $cardBottom
            ContentL = $minX - $cardLeft
            ContentR = $cardRight - $maxX
            ContentT = $minY - $cardTop
            ContentB = $cardBottom - $maxY
            LaneFrom = if ($laneMin -ge 0) { $cardRight - $laneMax } else { -1 }
            LaneTo   = if ($laneMin -ge 0) { $cardRight - $laneMin } else { -1 }
        }
    }
    finally { $bmp.Dispose() }
}

# Flatten: an array passed as one positional argument measures per element.
foreach ($s in @($args | ForEach-Object { $_ })) { Measure-Shot $s }
