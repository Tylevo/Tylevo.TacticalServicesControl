[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string] $Path,
    [string] $SourceRoot,
    [switch] $ValidateSourceInputs
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'PackageContract.ps1')
if ([string]::IsNullOrWhiteSpace($SourceRoot)) { $SourceRoot = Split-Path $PSScriptRoot -Parent }
$contract = Get-TscPilotQuestlinePackageContract
$resolvedSourceRoot = (Resolve-Path -LiteralPath $SourceRoot).Path
[xml] $props = Get-Content -LiteralPath (Join-Path $resolvedSourceRoot 'Directory.Build.props') -Raw
$versionNodes = @($props.SelectNodes('/Project/PropertyGroup[not(@Condition)]/Version'))
$sptNodes = @($props.SelectNodes('/Project/PropertyGroup[not(@Condition)]/TargetSptVersion'))
if ($versionNodes.Count -ne 1 -or $sptNodes.Count -ne 1) {
    throw 'Directory.Build.props must declare one release version and target SPT version.'
}
$version = $versionNodes[0].InnerText
$targetSptVersion = $sptNodes[0].InnerText

function Assert-AddonText([string] $Relative, [string] $Content) {
    if ([string]::IsNullOrWhiteSpace($Content)) { throw "Empty addon file: '$Relative'." }
    if ($Relative.EndsWith('.json', [StringComparison]::Ordinal)) {
        $parsed = $Content | ConvertFrom-Json
        if ($null -eq $parsed) { throw "Empty JSON value: '$Relative'." }
        if ($Relative -ceq 'addon.json') {
            $fields = @($parsed.PSObject.Properties.Name)
            $expectedFields = @('schemaVersion', 'id', 'version', 'targetSptVersion')
            if ($fields.Count -ne 4 -or @($fields | Where-Object { $expectedFields -cnotcontains $_ }).Count -ne 0 -or
                ($parsed.schemaVersion -isnot [int] -and $parsed.schemaVersion -isnot [long]) -or
                $parsed.id -isnot [string] -or $parsed.version -isnot [string] -or $parsed.targetSptVersion -isnot [string] -or
                $parsed.schemaVersion -ne 1 -or $parsed.id -cne 'tsc-pilot-questline' -or
                $parsed.version -cne $version -or $parsed.targetSptVersion -cne $targetSptVersion) {
                throw 'addon.json must contain only the supported identity/schema and match Directory.Build.props versions.'
            }
        }
    }
}

function Assert-AddonPath([string] $Entry, [Collections.Generic.HashSet[string]] $Seen, [string[]] $Allowed) {
    # Require exact canonical paths; ZIP extraction cannot change their meaning.
    if ($Allowed -cnotcontains $Entry) { throw "Non-allowlisted addon file: '$Entry'." }
    if (-not $Seen.Add($Entry)) { throw "Duplicate or case-colliding addon file: '$Entry'." }
}

function Test-AddonDirectory([string] $Root, [string] $Prefix, [switch] $Tracked) {
    $resolvedRoot = (Resolve-Path -LiteralPath $Root).Path.TrimEnd('\', '/')
    $allowed = @($contract.Files | ForEach-Object { $Prefix + $_ })
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $trackedFiles = @()
    if ($Tracked) {
        $trackedFiles = @(& git -c "safe.directory=$($resolvedSourceRoot.Replace('\', '/'))" -C $resolvedSourceRoot ls-files -- $contract.Source)
        if ($LASTEXITCODE -ne 0) { throw 'Could not read tracked addon source files.' }
    }
    foreach ($entry in Get-ChildItem -LiteralPath $resolvedRoot -Recurse -Force) {
        if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Addon files must not contain symbolic links or junctions: '$($entry.FullName)'."
        }
        if ($entry.PSIsContainer) { continue }
        $relative = $entry.FullName.Substring($resolvedRoot.Length + 1).Replace('\', '/')
        Assert-AddonPath $relative $seen $allowed
        $contentRelative = $relative.Substring($Prefix.Length)
        Assert-AddonText $contentRelative (Get-Content -LiteralPath $entry.FullName -Raw -Encoding UTF8)
        if ($Tracked -and $trackedFiles -cnotcontains ($contract.Source + '/' + $contentRelative)) {
            throw "Addon source must be tracked at its exact path: '$contentRelative'."
        }
    }
    if ($seen.Count -ne $allowed.Count) { throw 'Addon must contain exactly all eight reviewed data/document files.' }
}

if ($ValidateSourceInputs -or [string]::IsNullOrWhiteSpace($Path)) {
    Test-AddonDirectory -Root (Join-Path $resolvedSourceRoot $contract.Source) -Prefix '' -Tracked:$ValidateSourceInputs
    Write-Host 'Pilot questline source validation passed: eight data/document files, synchronized versions, no runtime binaries or configs.'
}
if ([string]::IsNullOrWhiteSpace($Path)) { return }

$resolvedPath = (Resolve-Path -LiteralPath $Path).Path
$prefix = $contract.Destination + '/'
if (Test-Path -LiteralPath $resolvedPath -PathType Container) {
    Test-AddonDirectory -Root $resolvedPath -Prefix $prefix
}
elseif ([IO.Path]::GetExtension($resolvedPath) -ieq '.zip') {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($resolvedPath)
    try {
        $allowed = @($contract.Files | ForEach-Object { $prefix + $_ })
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in $archive.Entries) {
            $entryPath = $entry.FullName.Replace('\', '/')
            if ([string]::IsNullOrEmpty($entry.Name)) {
                # Allow harmless directory records only when they are canonical
                # parents of a required file. Traversal records are rejected.
                if (@($allowed | Where-Object { $_.StartsWith($entryPath, [StringComparison]::Ordinal) }).Count -eq 0) {
                    throw "Non-allowlisted addon directory: '$($entry.FullName)'."
                }
                continue
            }
            Assert-AddonPath $entryPath $seen $allowed
            if ($entry.Length -gt 2MB) { throw "Oversized addon data file: '$($entry.FullName)'." }
            $reader = [IO.StreamReader]::new($entry.Open(), [Text.UTF8Encoding]::new($false, $true))
            try { Assert-AddonText ($entryPath.Substring($prefix.Length)) $reader.ReadToEnd() }
            finally { $reader.Dispose() }
        }
        if ($seen.Count -ne $allowed.Count) { throw 'Addon ZIP must contain exactly all eight reviewed data/document files.' }
    }
    finally { $archive.Dispose() }
}
else { throw 'Addon package path must be a directory or ZIP archive.' }

Write-Host 'Pilot questline package validation passed: eight data/document files, zero DLLs and bundles.'
