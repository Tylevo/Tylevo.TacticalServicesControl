[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string] $Path,

    [string] $ManifestPath,

    [string] $SourceRoot,

    [switch] $ValidateSourceInputs
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $ManifestPath = Join-Path $PSScriptRoot "package-layout.allowlist.json"
}

if ([string]::IsNullOrWhiteSpace($SourceRoot)) {
    $SourceRoot = Split-Path $PSScriptRoot -Parent
}

function Normalize-PackagePath {
    param(
        [Parameter(Mandatory)]
        [string] $Value,

        [Parameter(Mandatory)]
        [string] $Description
    )

    $normalized = $Value.Replace("\", "/")
    if ([string]::IsNullOrWhiteSpace($normalized)) {
        throw "$Description is empty."
    }

    if ($normalized.StartsWith("/", [StringComparison]::Ordinal) -or
        $normalized -match "^[A-Za-z]:" -or
        $normalized.IndexOf([char]0) -ge 0) {
        throw "$Description is absolute or otherwise unsafe: '$Value'."
    }

    $segments = $normalized.Split("/", [StringSplitOptions]::RemoveEmptyEntries)
    if ($segments.Count -eq 0 -or $segments -contains "." -or $segments -contains "..") {
        throw "$Description contains an unsafe path segment: '$Value'."
    }

    return [string]::Join("/", $segments)
}

function Get-RelativeFilePath {
    param(
        [Parameter(Mandatory)]
        [string] $Root,

        [Parameter(Mandatory)]
        [string] $File
    )

    $resolvedRoot = (Resolve-Path -LiteralPath $Root).Path.TrimEnd("\", "/")
    $resolvedFile = (Resolve-Path -LiteralPath $File).Path
    if (-not $resolvedFile.StartsWith($resolvedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "File '$resolvedFile' is outside '$resolvedRoot'."
    }

    return $resolvedFile.Substring($resolvedRoot.Length + 1).Replace("\", "/")
}

function New-OrdinalIgnoreCaseSet {
    return ,([Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase))
}

function Assert-AddUnique {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [Collections.Generic.HashSet[string]] $Set,

        [Parameter(Mandatory)]
        [string] $Value,

        [Parameter(Mandatory)]
        [string] $Description
    )

    if (-not $Set.Add($Value)) {
        throw "$Description contains a duplicate or case-colliding path: '$Value'."
    }
}

function Test-IsUnderPackageRoot {
    param(
        [Parameter(Mandatory)]
        [string] $Entry,

        [Parameter(Mandatory)]
        [string[]] $Roots
    )

    foreach ($root in $Roots) {
        if ($Entry.StartsWith($root + "/", [StringComparison]::Ordinal) -or
            $Entry.Equals($root, [StringComparison]::Ordinal)) {
            return $true
        }
    }

    return $false
}

if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
    throw "Package allowlist manifest was not found: '$ManifestPath'."
}

$resolvedSourceRoot = (Resolve-Path -LiteralPath $SourceRoot).Path
$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 2) {
    throw "Unsupported package allowlist schema '$($manifest.schemaVersion)'."
}

$archiveRoots = @(
    $manifest.archiveRoots |
        ForEach-Object { Normalize-PackagePath -Value ([string] $_) -Description "Archive root" }
)

$archiveRootSet = New-OrdinalIgnoreCaseSet
foreach ($root in $archiveRoots) {
    if ($root.IndexOf("/", [StringComparison]::Ordinal) -ge 0) {
        throw "Archive root must be one top-level path segment: '$root'."
    }

    Assert-AddUnique -Set $archiveRootSet -Value $root -Description "Archive roots"
}

$installRoots = @(
    $manifest.installRoots |
        ForEach-Object { Normalize-PackagePath -Value ([string] $_) -Description "Install root" }
)

$installRootSet = New-OrdinalIgnoreCaseSet
foreach ($root in $installRoots) {
    if (-not (Test-IsUnderPackageRoot -Entry $root -Roots $archiveRoots)) {
        throw "Install root '$root' is outside the declared archive roots."
    }

    Assert-AddUnique -Set $installRootSet -Value $root -Description "Install roots"
}

foreach ($archiveRoot in $archiveRoots) {
    $hasInstallRoot = $false
    foreach ($installRoot in $installRoots) {
        if (Test-IsUnderPackageRoot -Entry $installRoot -Roots @($archiveRoot)) {
            $hasInstallRoot = $true
            break
        }
    }

    if (-not $hasInstallRoot) {
        throw "Archive root '$archiveRoot' has no declared install root."
    }
}

$allowed = New-OrdinalIgnoreCaseSet
$mirrorEntries = New-OrdinalIgnoreCaseSet
foreach ($mirror in $manifest.mirrors) {
    $sourceRelative = Normalize-PackagePath -Value ([string] $mirror.source) -Description "Mirror source"
    $destination = Normalize-PackagePath -Value ([string] $mirror.destination) -Description "Mirror destination"
    if (-not (Test-IsUnderPackageRoot -Entry $destination -Roots $installRoots)) {
        throw "Mirror destination '$destination' is outside the declared install roots."
    }

    $sourceDirectory = Join-Path $resolvedSourceRoot $sourceRelative.Replace("/", [IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path -LiteralPath $sourceDirectory -PathType Container)) {
        throw "Mirror source directory was not found: '$sourceDirectory'."
    }

    $excludedNames = New-OrdinalIgnoreCaseSet
    foreach ($excludedName in @($mirror.excludedFileNames)) {
        [void] $excludedNames.Add([string] $excludedName)
    }

    foreach ($file in Get-ChildItem -LiteralPath $sourceDirectory -File -Recurse) {
        if ($excludedNames.Contains($file.Name)) {
            continue
        }

        $relative = Get-RelativeFilePath -Root $sourceDirectory -File $file.FullName
        $entry = Normalize-PackagePath -Value "$destination/$relative" -Description "Mirrored package path"
        Assert-AddUnique -Set $allowed -Value $entry -Description "Allowlist"
        [void] $mirrorEntries.Add($entry)
    }
}

foreach ($generatedFile in $manifest.generatedFiles) {
    $entry = Normalize-PackagePath -Value ([string] $generatedFile) -Description "Generated package path"
    if (-not (Test-IsUnderPackageRoot -Entry $entry -Roots $installRoots)) {
        throw "Generated file '$entry' is outside the declared install roots."
    }

    Assert-AddUnique -Set $allowed -Value $entry -Description "Allowlist"
}

$required = New-OrdinalIgnoreCaseSet
foreach ($requiredFile in $manifest.requiredFiles) {
    $entry = Normalize-PackagePath -Value ([string] $requiredFile) -Description "Required package path"
    if (-not $allowed.Contains($entry)) {
        throw "Required file '$entry' is not part of the exact allowlist."
    }

    Assert-AddUnique -Set $required -Value $entry -Description "Required files"
}

$forbiddenExtensions = @($manifest.forbiddenExtensions | ForEach-Object { ([string] $_).ToLowerInvariant() })
$forbiddenSegments = @(
    $manifest.forbiddenPathSegments |
        ForEach-Object { Normalize-PackagePath -Value ([string] $_) -Description "Forbidden path segment" }
)
$forbiddenFileNamePatterns = @($manifest.forbiddenFileNamePatterns | ForEach-Object { [string] $_ })

function Assert-EntryPassesSecurityRules {
    param(
        [Parameter(Mandatory)]
        [string] $Entry
    )

    if (-not (Test-IsUnderPackageRoot -Entry $Entry -Roots $archiveRoots)) {
        throw "Entry '$Entry' is outside the declared archive roots."
    }

    $lowerEntry = $Entry.ToLowerInvariant()
    foreach ($extension in $forbiddenExtensions) {
        if ($lowerEntry.EndsWith($extension, [StringComparison]::Ordinal)) {
            throw "Entry '$Entry' uses forbidden extension '$extension'."
        }
    }

    foreach ($segment in $forbiddenSegments) {
        $lowerSegment = $segment.ToLowerInvariant()
        if ($lowerEntry.Equals($lowerSegment, [StringComparison]::Ordinal) -or
            $lowerEntry.StartsWith($lowerSegment + "/", [StringComparison]::Ordinal) -or
            $lowerEntry.IndexOf("/$lowerSegment/", [StringComparison]::Ordinal) -ge 0 -or
            $lowerEntry.EndsWith("/$lowerSegment", [StringComparison]::Ordinal)) {
            throw "Entry '$Entry' contains forbidden path '$segment'."
        }
    }

    $leaf = [IO.Path]::GetFileName($Entry)
    foreach ($pattern in $forbiddenFileNamePatterns) {
        if ($leaf -match $pattern) {
            throw "Entry '$Entry' matches forbidden dependency pattern '$pattern'."
        }
    }
}

foreach ($entry in $allowed) {
    Assert-EntryPassesSecurityRules -Entry $entry
}

$generatedDllCount = @($manifest.generatedFiles | Where-Object { ([string] $_).EndsWith(".dll", [StringComparison]::OrdinalIgnoreCase) }).Count
$generatedBundleCount = @($manifest.generatedFiles | Where-Object { ([string] $_).EndsWith(".bundle", [StringComparison]::OrdinalIgnoreCase) }).Count
if ($generatedDllCount -ne [int] $manifest.exactCounts.".dll") {
    throw "Allowlist declares $generatedDllCount generated DLLs; expected $($manifest.exactCounts.'.dll')."
}

if ($generatedBundleCount -ne [int] $manifest.exactCounts.".bundle") {
    throw "Allowlist declares $generatedBundleCount generated bundles; expected $($manifest.exactCounts.'.bundle')."
}

if ($ValidateSourceInputs) {
    Write-Host "Package allowlist source validation passed: $($mirrorEntries.Count) mirrored files, $generatedDllCount DLLs, and $generatedBundleCount bundles."
}

if ([string]::IsNullOrWhiteSpace($Path)) {
    if (-not $ValidateSourceInputs) {
        throw "Provide -Path for a package directory/ZIP, or use -ValidateSourceInputs."
    }

    return
}

if (-not (Test-Path -LiteralPath $Path)) {
    throw "Package path was not found: '$Path'."
}

$actual = New-OrdinalIgnoreCaseSet
$resolvedPackagePath = (Resolve-Path -LiteralPath $Path).Path
if (Test-Path -LiteralPath $resolvedPackagePath -PathType Container) {
    foreach ($file in Get-ChildItem -LiteralPath $resolvedPackagePath -File -Recurse) {
        $relative = Get-RelativeFilePath -Root $resolvedPackagePath -File $file.FullName
        $entry = Normalize-PackagePath -Value $relative -Description "Package entry"
        Assert-AddUnique -Set $actual -Value $entry -Description "Package"
    }
}
elseif ([IO.Path]::GetExtension($resolvedPackagePath).Equals(".zip", [StringComparison]::OrdinalIgnoreCase)) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($resolvedPackagePath)
    try {
        foreach ($zipEntry in $archive.Entries) {
            if ([string]::IsNullOrEmpty($zipEntry.Name)) {
                continue
            }

            $entry = Normalize-PackagePath -Value $zipEntry.FullName -Description "ZIP entry"
            Assert-AddUnique -Set $actual -Value $entry -Description "ZIP"
        }
    }
    finally {
        $archive.Dispose()
    }
}
else {
    throw "Package path must be a directory or .zip archive: '$resolvedPackagePath'."
}

$actualArchiveRoots = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($entry in $actual) {
    [void] $actualArchiveRoots.Add($entry.Split("/")[0])
}

if ($actualArchiveRoots.Count -ne $archiveRoots.Count) {
    throw "Package top-level roots are '$([string]::Join(", ", @($actualArchiveRoots | Sort-Object)))'; expected exactly '$([string]::Join(", ", $archiveRoots))'."
}

foreach ($archiveRoot in $archiveRoots) {
    if (-not $actualArchiveRoots.Contains($archiveRoot)) {
        throw "Package is missing required top-level root '$archiveRoot'."
    }
}

foreach ($entry in $actual) {
    Assert-EntryPassesSecurityRules -Entry $entry
    if (-not $allowed.Contains($entry)) {
        throw "Package contains a non-allowlisted file: '$entry'."
    }
}

foreach ($entry in $allowed) {
    if (-not $actual.Contains($entry)) {
        throw "Package is missing allowlisted file: '$entry'."
    }
}

foreach ($entry in $required) {
    if (-not $actual.Contains($entry)) {
        throw "Package is missing required file: '$entry'."
    }
}

$actualDllCount = @($actual | Where-Object { $_.EndsWith(".dll", [StringComparison]::OrdinalIgnoreCase) }).Count
$actualBundleCount = @($actual | Where-Object { $_.EndsWith(".bundle", [StringComparison]::OrdinalIgnoreCase) }).Count
if ($actualDllCount -ne [int] $manifest.exactCounts.".dll") {
    throw "Package contains $actualDllCount DLLs; expected exactly $($manifest.exactCounts.'.dll')."
}

if ($actualBundleCount -ne [int] $manifest.exactCounts.".bundle") {
    throw "Package contains $actualBundleCount bundles; expected exactly $($manifest.exactCounts.'.bundle')."
}

Write-Host "Package layout validation passed: $($actual.Count) files, $actualDllCount DLLs, and $actualBundleCount bundles."
