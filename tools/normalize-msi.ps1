# Replaces the nondeterministic MSI identities and timestamps that WiX emits.
# The cabinet is updated through Windows Installer's logical stream API. The
# remaining fixed-length fields are rewritten without changing the MSI layout.
param(
    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path $_ -PathType Leaf })]
    [string]$Path,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$')]
    [string]$Version
)

$ErrorActionPreference = 'Stop'

if (-not ('ThisIsMyPC.FixedLengthByteRewriter' -as [type])) {
    Add-Type -TypeDefinition @'
using System;

namespace ThisIsMyPC
{
    public static class FixedLengthByteRewriter
    {
        public static int Replace(byte[] data, byte[] oldValue, byte[] newValue)
        {
            if (data == null || oldValue == null || newValue == null)
                throw new ArgumentNullException();
            if (oldValue.Length == 0 || oldValue.Length != newValue.Length)
                throw new ArgumentException("Replacement values must have the same nonzero length.");

            int replacements = 0;
            for (int offset = 0; offset <= data.Length - oldValue.Length; offset++)
            {
                if (data[offset] != oldValue[0])
                    continue;

                int index = 1;
                while (index < oldValue.Length && data[offset + index] == oldValue[index])
                    index++;
                if (index != oldValue.Length)
                    continue;

                Buffer.BlockCopy(newValue, 0, data, offset, newValue.Length);
                replacements++;
                offset += oldValue.Length - 1;
            }
            return replacements;
        }

        public static void NormalizeCompoundFileRootTimestamps(byte[] data, long fileTime)
        {
            byte[] signature = { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 };
            if (data.Length < 512)
                throw new ArgumentException("The file is not an OLE compound document.");
            for (int index = 0; index < signature.Length; index++)
                if (data[index] != signature[index])
                    throw new ArgumentException("The file is not an OLE compound document.");
            if (ReadUInt16(data, 0x1C) != 0xFFFE)
                throw new ArgumentException("The compound document byte order is unsupported.");

            int sectorShift = ReadUInt16(data, 0x1E);
            if (sectorShift != 9 && sectorShift != 12)
                throw new ArgumentException("The compound document sector size is unsupported.");
            int sectorSize = 1 << sectorShift;
            int directorySector = ReadInt32(data, 0x30);
            long rootOffset64 = ((long)directorySector + 1) * sectorSize;
            if (directorySector < 0 || rootOffset64 < 0 || rootOffset64 + 128 > data.Length)
                throw new ArgumentException("The compound document root directory is invalid.");

            int rootOffset = checked((int)rootOffset64);
            if (data[rootOffset + 66] != 5)
                throw new ArgumentException("The compound document root entry is missing.");
            WriteInt64(data, rootOffset + 100, fileTime);
            WriteInt64(data, rootOffset + 108, fileTime);
        }

        public static int NormalizeEmbeddedCabinetTimestamps(byte[] data, ushort dosDate, ushort dosTime)
        {
            int cabinets = 0;
            for (int cabinetOffset = 0; cabinetOffset <= data.Length - 36; cabinetOffset++)
            {
                if (data[cabinetOffset] != (byte)'M' || data[cabinetOffset + 1] != (byte)'S' ||
                    data[cabinetOffset + 2] != (byte)'C' || data[cabinetOffset + 3] != (byte)'F')
                    continue;
                if (data[cabinetOffset + 24] != 3 || data[cabinetOffset + 25] != 1)
                    continue;

                uint cabinetLength = ReadUInt32(data, cabinetOffset + 8);
                uint filesOffset = ReadUInt32(data, cabinetOffset + 16);
                ushort folderCount = ReadUInt16(data, cabinetOffset + 26);
                ushort fileCount = ReadUInt16(data, cabinetOffset + 28);
                long cabinetEnd64 = (long)cabinetOffset + cabinetLength;
                if (cabinetLength < 36 || cabinetEnd64 > data.Length || filesOffset < 36 ||
                    filesOffset >= cabinetLength || folderCount == 0 || fileCount == 0)
                    continue;

                int cabinetEnd = checked((int)cabinetEnd64);
                int fileOffset = checked(cabinetOffset + (int)filesOffset);
                bool valid = true;
                for (int index = 0; index < fileCount; index++)
                {
                    if (fileOffset > cabinetEnd - 17)
                    {
                        valid = false;
                        break;
                    }
                    int nameEnd = fileOffset + 16;
                    while (nameEnd < cabinetEnd && data[nameEnd] != 0)
                        nameEnd++;
                    if (nameEnd == cabinetEnd)
                    {
                        valid = false;
                        break;
                    }
                    fileOffset = nameEnd + 1;
                }
                if (!valid)
                    continue;

                fileOffset = checked(cabinetOffset + (int)filesOffset);
                for (int index = 0; index < fileCount; index++)
                {
                    WriteUInt16(data, fileOffset + 10, dosDate);
                    WriteUInt16(data, fileOffset + 12, dosTime);
                    fileOffset += 16;
                    while (data[fileOffset++] != 0) { }
                }
                cabinets++;
                cabinetOffset = cabinetEnd - 1;
            }
            return cabinets;
        }

        private static ushort ReadUInt16(byte[] data, int offset)
        {
            return (ushort)(data[offset] | data[offset + 1] << 8);
        }

        private static uint ReadUInt32(byte[] data, int offset)
        {
            return (uint)(data[offset] | data[offset + 1] << 8 |
                data[offset + 2] << 16 | data[offset + 3] << 24);
        }

        private static int ReadInt32(byte[] data, int offset)
        {
            return unchecked((int)ReadUInt32(data, offset));
        }

        private static void WriteUInt16(byte[] data, int offset, ushort value)
        {
            data[offset] = (byte)value;
            data[offset + 1] = (byte)(value >> 8);
        }

        private static void WriteInt64(byte[] data, int offset, long value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            Buffer.BlockCopy(bytes, 0, data, offset, bytes.Length);
        }
    }
}
'@
}

function New-DeterministicGuid([string]$purpose) {
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        $seed = [Text.Encoding]::UTF8.GetBytes("ThisIsMyPC|$purpose|$Version|win-x64")
        $hash = $sha256.ComputeHash($seed)
    } finally {
        $sha256.Dispose()
    }

    $hex = -join ($hash[0..15] | ForEach-Object { $_.ToString('X2') })
    return "{$($hex.Substring(0, 8))-$($hex.Substring(8, 4))-$($hex.Substring(12, 4))-$($hex.Substring(16, 4))-$($hex.Substring(20, 12))}"
}

function Get-ProductCode($database) {
    $view = $database.OpenView("SELECT ``Value`` FROM ``Property`` WHERE ``Property``='ProductCode'")
    $record = $null
    try {
        $view.Execute()
        $record = $view.Fetch()
        if ($null -eq $record) { throw 'MSI Property table has no ProductCode.' }
        return $record.StringData(1)
    } finally {
        if ($null -ne $record) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record) }
        if ($null -ne $view) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view) }
    }
}

$resolvedPath = (Resolve-Path $Path).Path
$deterministicProductCode = New-DeterministicGuid 'MSI ProductCode'
$deterministicPackageCode = New-DeterministicGuid 'MSI PackageCode'
$deterministicTime = [DateTime]::SpecifyKind(
    [DateTime]'2000-01-01T00:00:00',
    [DateTimeKind]::Utc)
$dosDate = [uint16](((2000 - 1980) -shl 9) -bor (1 -shl 5) -bor 1)

# An MSI is a sector-chained compound file, so an embedded cabinet cannot be
# edited by treating its physical bytes as contiguous. Extract and replace the
# logical stream through Windows Installer, then normalize the compound file.
$environmentManifest = Get-Content (Join-Path $PSScriptRoot 'reproducible-build-environment.json') -Raw |
    ConvertFrom-Json
$msiDb = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin\$($environmentManifest.windowsSdkVersion)\x86\MsiDb.exe"
if (-not (Test-Path $msiDb -PathType Leaf)) { throw "MsiDb.exe is missing: $msiDb" }
$cabinetDirectory = Join-Path ([IO.Path]::GetTempPath()) ("thisismypc-msi-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $cabinetDirectory | Out-Null
try {
    $arguments = "-d `"$resolvedPath`" -x app.cab"
    $extract = Start-Process -FilePath $msiDb -ArgumentList $arguments `
        -WorkingDirectory $cabinetDirectory -Wait -PassThru -WindowStyle Hidden
    if ($extract.ExitCode -ne 0) { throw "MsiDb failed to extract app.cab with exit code $($extract.ExitCode)." }
    $cabinetPath = Join-Path $cabinetDirectory 'app.cab'
    if (-not (Test-Path $cabinetPath -PathType Leaf)) { throw 'MSI has no app.cab stream.' }
    $cabinetBytes = [IO.File]::ReadAllBytes($cabinetPath)
    $cabinetCount = [ThisIsMyPC.FixedLengthByteRewriter]::NormalizeEmbeddedCabinetTimestamps(
        $cabinetBytes, $dosDate, [uint16]0)
    if ($cabinetCount -ne 1) { throw "Expected one cabinet image; found $cabinetCount." }
    [IO.File]::WriteAllBytes($cabinetPath, $cabinetBytes)

    $streamInstaller = New-Object -ComObject WindowsInstaller.Installer
    $streamDatabase = $null
    $streamView = $null
    $streamRecord = $null
    try {
        $streamDatabase = $streamInstaller.OpenDatabase($resolvedPath, 1)
        $streamView = $streamDatabase.OpenView(
            "SELECT ``Name``, ``Data`` FROM ``_Streams`` WHERE ``Name`` = 'app.cab'")
        $streamView.Execute()
        $streamRecord = $streamView.Fetch()
        if ($null -eq $streamRecord) { throw 'MSI has no app.cab stream row.' }
        $streamRecord.SetStream(2, $cabinetPath)
        $streamView.Modify(2, $streamRecord)
        $streamDatabase.Commit()
    } finally {
        if ($null -ne $streamRecord) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($streamRecord) }
        if ($null -ne $streamView) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($streamView) }
        if ($null -ne $streamDatabase) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($streamDatabase) }
        if ($null -ne $streamInstaller) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($streamInstaller) }
    }
} finally {
    if (Test-Path $cabinetDirectory -PathType Container) {
        Remove-Item -LiteralPath $cabinetDirectory -Recurse -Force
    }
}

$windowsInstaller = New-Object -ComObject WindowsInstaller.Installer
$database = $null
$summary = $null
try {
    $database = $windowsInstaller.OpenDatabase($resolvedPath, 0)
    $productCode = [string](Get-ProductCode $database)
    $productCode = $productCode.Trim()
    $summary = $windowsInstaller.SummaryInformation($resolvedPath, 0)
    $packageCode = ([string]$summary.Property(9)).Trim()
    $created = [DateTime]$summary.Property(12)
    $saved = [DateTime]$summary.Property(13)
} finally {
    if ($null -ne $summary) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($summary) }
    if ($null -ne $database) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($database) }
    if ($null -ne $windowsInstaller) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($windowsInstaller) }
}

$bytes = [IO.File]::ReadAllBytes($resolvedPath)
$ascii = [Text.Encoding]::ASCII
$productBytes = $ascii.GetBytes($productCode)
$fixedProductBytes = $ascii.GetBytes($deterministicProductCode)
$packageBytes = $ascii.GetBytes($packageCode)
$fixedPackageBytes = $ascii.GetBytes($deterministicPackageCode)
if ($productBytes.Length -ne $fixedProductBytes.Length) {
    throw "ProductCode lengths differ: $($productBytes.Length) and $($fixedProductBytes.Length)."
}
if ($packageBytes.Length -ne $fixedPackageBytes.Length) {
    throw "PackageCode lengths differ: $($packageBytes.Length) and $($fixedPackageBytes.Length)."
}
$productCount = [ThisIsMyPC.FixedLengthByteRewriter]::Replace(
    $bytes, $productBytes, $fixedProductBytes)
$packageCount = [ThisIsMyPC.FixedLengthByteRewriter]::Replace(
    $bytes, $packageBytes, $fixedPackageBytes)

$createdBytes = [BitConverter]::GetBytes($created.ToFileTime())
$savedBytes = [BitConverter]::GetBytes($saved.ToFileTime())
$fixedTimeBytes = [BitConverter]::GetBytes($deterministicTime.ToFileTimeUtc())
if ($created -eq $saved) {
    $timeCount = [ThisIsMyPC.FixedLengthByteRewriter]::Replace(
        $bytes, $createdBytes, $fixedTimeBytes)
    if ($timeCount -ne 0 -and $timeCount -ne 2) {
        throw "Expected zero or two matching MSI summary timestamps; found $timeCount."
    }
} else {
    $createdCount = [ThisIsMyPC.FixedLengthByteRewriter]::Replace(
        $bytes, $createdBytes, $fixedTimeBytes)
    $savedCount = [ThisIsMyPC.FixedLengthByteRewriter]::Replace(
        $bytes, $savedBytes, $fixedTimeBytes)
    if (($createdCount -ne 0 -and $createdCount -ne 1) -or
        ($savedCount -ne 0 -and $savedCount -ne 1)) {
        throw "Expected zero or one of each MSI summary timestamp; found $createdCount and $savedCount."
    }
}
$fixedTimeCount = [ThisIsMyPC.FixedLengthByteRewriter]::Replace(
    $bytes, $fixedTimeBytes, $fixedTimeBytes)
if ($fixedTimeCount -ne 2) {
    throw "Expected two normalized MSI summary timestamps; found $fixedTimeCount."
}

if ($productCount -ne 1) { throw "Expected one MSI ProductCode; found $productCount." }
if ($packageCount -ne 1) { throw "Expected one MSI PackageCode; found $packageCount." }

[ThisIsMyPC.FixedLengthByteRewriter]::NormalizeCompoundFileRootTimestamps(
    $bytes, $deterministicTime.ToFileTimeUtc())

$temporaryPath = "$resolvedPath.deterministic.tmp"
try {
    [IO.File]::WriteAllBytes($temporaryPath, $bytes)
    Move-Item -LiteralPath $temporaryPath -Destination $resolvedPath -Force
} finally {
    if (Test-Path $temporaryPath) { Remove-Item -LiteralPath $temporaryPath -Force }
}

Write-Host "$(Split-Path $resolvedPath -Leaf): deterministic ProductCode $deterministicProductCode"
Write-Host "$(Split-Path $resolvedPath -Leaf): deterministic PackageCode $deterministicPackageCode"
