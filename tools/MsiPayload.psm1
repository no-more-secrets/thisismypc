function Get-MsiToolPath {
    $manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'reproducible-build-environment.json') -Raw |
        ConvertFrom-Json
    $programFilesX86 = [Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)
    $path = Join-Path $programFilesX86 "Windows Kits\10\bin\$($manifest.windowsSdkVersion)\x86\MsiDb.exe"
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Pinned MsiDb.exe is missing: $path" }
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    if ($hash -ne $manifest.msiDbSha256) { throw "MsiDb.exe hash is $hash, expected $($manifest.msiDbSha256)." }
    $path
}

function Release-ComObject {
    param($Value)
    if ($null -ne $Value) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($Value) }
}

function Get-MsiTableNames {
    param($Database)
    $view = $null
    $record = $null
    try {
        $view = $Database.OpenView('SELECT `Name` FROM `_Tables` ORDER BY `Name`')
        $view.Execute()
        $names = @()
        while ($null -ne ($record = $view.Fetch())) {
            $names += [string]$record.StringData(1)
            Release-ComObject $record
            $record = $null
        }
        $names
    }
    finally {
        Release-ComObject $record
        Release-ComObject $view
    }
}

function Import-IdtRows {
    param([Parameter(Mandatory)][string]$Path)
    $lines = @(Get-Content -LiteralPath $Path)
    if ($lines.Count -lt 3) { throw "MSI table export is incomplete: $Path" }
    $lines | Select-Object -Skip 3 | ConvertFrom-Csv -Delimiter "`t" -Header ($lines[0] -split "`t")
}

function Get-LongMsiName {
    param([AllowEmptyString()][string]$Value)
    if ($Value.Contains('|')) { return $Value.Substring($Value.IndexOf('|') + 1) }
    $Value
}

function Export-MsiLogicalContent {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Destination
    )

    $msiPath = (Resolve-Path -LiteralPath $Path).Path
    $destinationPath = [IO.Path]::GetFullPath($Destination)
    if (Test-Path -LiteralPath $destinationPath) {
        if (@(Get-ChildItem -LiteralPath $destinationPath -Force).Count -ne 0) {
            throw "MSI logical-content destination is not empty: $destinationPath"
        }
    }
    else {
        New-Item -ItemType Directory -Path $destinationPath | Out-Null
    }
    $metadataPath = Join-Path $destinationPath 'metadata'
    $payloadPath = Join-Path $destinationPath 'payload'
    New-Item -ItemType Directory -Path $metadataPath | Out-Null
    New-Item -ItemType Directory -Path $payloadPath | Out-Null

    $working = Join-Path ([IO.Path]::GetTempPath()) ('thisismypc-msi-read-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $working | Out-Null
    $installer = $null
    $database = $null
    $summary = $null
    try {
        $installer = New-Object -ComObject WindowsInstaller.Installer
        $database = $installer.OpenDatabase($msiPath, 0)
        $tableNames = @(Get-MsiTableNames $database)
        $excluded = @('File', 'MsiDigitalSignature', 'MsiDigitalSignatureEx', '_DigitalSignature', '_DigitalSignatureEx')
        foreach ($table in $tableNames) {
            if ([string]::IsNullOrWhiteSpace($table)) { continue }
            if ($excluded -contains $table) { continue }
            $tableDirectory = Join-Path $metadataPath $table
            New-Item -ItemType Directory -Path $tableDirectory | Out-Null
            $database.Export($table, $tableDirectory, "$table.idt")
        }
        foreach ($table in 'File', 'Component', 'Directory') {
            $database.Export($table, $working, "$table.idt")
        }
        $canonicalFileRows = @("File`tComponent_`tFileName`tFileSize`tVersion`tLanguage`tAttributes`tSequence")
        foreach ($row in @(Import-IdtRows (Join-Path $working 'File.idt') | Sort-Object File)) {
            $longName = Get-LongMsiName ([string]$row.FileName)
            $fileSize = if ($longName -eq 'Update.exe' -or
                $longName -match '^ThisIsMyPC(?:\..+)?\.(?:exe|dll)$') {
                '<AUTHENTICODE>'
            }
            else {
                $row.FileSize
            }
            $canonicalFileRows += @(
                $row.File,
                $row.Component_,
                $row.FileName,
                $fileSize,
                $row.Version,
                $row.Language,
                $row.Attributes,
                $row.Sequence
            ) -join "`t"
        }
        [IO.File]::WriteAllLines(
            (Join-Path $metadataPath 'File.canonical.tsv'),
            $canonicalFileRows,
            [Text.UTF8Encoding]::new($false))
        $summary = $installer.SummaryInformation($msiPath, 0)
        $summaryLines = for ($property = 1; $property -le 19; $property++) {
            $value = $summary.Property($property)
            if ($value -is [DateTime]) { $value = $value.ToUniversalTime().ToString('o') }
            "$property`t$value"
        }
        [IO.File]::WriteAllLines((Join-Path $metadataPath 'Summary.txt'), $summaryLines, [Text.UTF8Encoding]::new($false))

        $msiDb = Get-MsiToolPath
        $arguments = "-d `"$msiPath`" -x app.cab"
        $extract = Start-Process -FilePath $msiDb -ArgumentList $arguments -WorkingDirectory $working `
            -Wait -PassThru -WindowStyle Hidden
        if ($extract.ExitCode -ne 0) { throw "MsiDb failed to extract app.cab with exit code $($extract.ExitCode)." }
        $cabinet = Join-Path $working 'app.cab'
        if (-not (Test-Path -LiteralPath $cabinet -PathType Leaf)) { throw 'MSI has no embedded app.cab.' }
        $rawPayload = Join-Path $working 'cab'
        New-Item -ItemType Directory -Path $rawPayload | Out-Null
        & (Join-Path $env:SystemRoot 'System32\expand.exe') '-F:*' $cabinet $rawPayload | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "expand.exe failed with exit code $LASTEXITCODE." }

        $components = @{}
        foreach ($row in Import-IdtRows (Join-Path $working 'Component.idt')) {
            $components[[string]$row.Component] = [string]$row.Directory_
        }
        $directories = @{}
        foreach ($row in Import-IdtRows (Join-Path $working 'Directory.idt')) {
            $directories[[string]$row.Directory] = [pscustomobject]@{
                Parent = [string]$row.Directory_Parent
                Name = Get-LongMsiName ([string]$row.DefaultDir)
            }
        }
        $resolvedDirectories = @{}
        $resolvingDirectories = New-Object 'Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
        function Resolve-Directory([string]$identifier) {
            if ([string]::IsNullOrEmpty($identifier) -or $identifier -in @('TARGETDIR', 'INSTALLFOLDER')) { return '' }
            if (-not $directories.ContainsKey($identifier)) { throw "MSI component references unknown directory $identifier." }
            if ($resolvedDirectories.ContainsKey($identifier)) { return $resolvedDirectories[$identifier] }
            if (-not $resolvingDirectories.Add($identifier)) { throw "MSI directory graph contains a cycle at $identifier." }
            $entry = $directories[$identifier]
            try {
                $parent = Resolve-Directory $entry.Parent
                if ($entry.Name -in @('.', 'SourceDir')) { $result = $parent }
                else {
                    if ($entry.Name -eq '..' -or [IO.Path]::IsPathRooted($entry.Name) -or
                        $entry.Name.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0 -or
                        $entry.Name.Contains('/') -or $entry.Name.Contains('\')) {
                        throw "MSI directory has unsafe name $($entry.Name)."
                    }
                    $result = if ($parent) { Join-Path $parent $entry.Name } else { $entry.Name }
                }
                $resolvedDirectories[$identifier] = $result
                $result
            }
            finally {
                [void]$resolvingDirectories.Remove($identifier)
            }
        }

        $rawPayloadPrefix = [IO.Path]::GetFullPath($rawPayload).TrimEnd('\') + '\'
        $payloadPrefix = [IO.Path]::GetFullPath($payloadPath).TrimEnd('\') + '\'
        foreach ($row in Import-IdtRows (Join-Path $working 'File.idt')) {
            $fileKey = [string]$row.File
            $component = [string]$row.Component_
            if ([string]::IsNullOrWhiteSpace($fileKey) -or $fileKey -in @('.', '..') -or
                [IO.Path]::IsPathRooted($fileKey) -or $fileKey.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0 -or
                $fileKey.Contains('/') -or $fileKey.Contains('\')) {
                throw "MSI has unsafe cabinet file key $fileKey."
            }
            if (-not $components.ContainsKey($component)) { throw "MSI file references unknown component $component." }
            $name = Get-LongMsiName ([string]$row.FileName)
            if ([string]::IsNullOrWhiteSpace($name) -or $name.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0 -or
                $name.Contains('/') -or $name.Contains('\')) {
                throw "MSI file has unsafe name $name."
            }
            $relativeDirectory = Resolve-Directory $components[$component]
            $relativePath = if ($relativeDirectory) { Join-Path $relativeDirectory $name } else { $name }
            $source = [IO.Path]::GetFullPath((Join-Path $rawPayload $fileKey))
            if (-not $source.StartsWith($rawPayloadPrefix, [StringComparison]::OrdinalIgnoreCase)) {
                throw "MSI cabinet file escaped the extraction directory: $fileKey"
            }
            if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Cabinet is missing MSI file key $fileKey." }
            $target = [IO.Path]::GetFullPath((Join-Path $payloadPath $relativePath))
            if (-not $target.StartsWith($payloadPrefix, [StringComparison]::OrdinalIgnoreCase)) {
                throw "MSI file escaped the logical payload directory: $relativePath"
            }
            $targetDirectory = Split-Path $target -Parent
            if (-not (Test-Path -LiteralPath $targetDirectory)) { New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null }
            Copy-Item -LiteralPath $source -Destination $target
        }
    }
    finally {
        Release-ComObject $summary
        Release-ComObject $database
        Release-ComObject $installer
        $resolvedWorking = [IO.Path]::GetFullPath($working)
        $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if ($resolvedWorking.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $resolvedWorking -Recurse -Force
        }
    }

    [pscustomobject]@{ Root = $destinationPath; Metadata = $metadataPath; Payload = $payloadPath }
}

Export-ModuleMember -Function Export-MsiLogicalContent
