[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $BaselineAssetArchive,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $OutputDirectory,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $BuildEvidencePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot 'PackageContract.ps1')

$RepositoryRoot = Split-Path $PSScriptRoot -Parent
$ManifestPath = Join-Path $PSScriptRoot "package-layout.allowlist.json"

function Normalize-RelativePath {
    param(
        [Parameter(Mandatory)]
        [string] $Value,

        [Parameter(Mandatory)]
        [string] $Description
    )

    $normalized = $Value.Replace("\", "/")
    if ([string]::IsNullOrWhiteSpace($normalized) -or
        $normalized.StartsWith("/", [StringComparison]::Ordinal) -or
        $normalized -match "^[A-Za-z]:" -or
        $normalized.IndexOf([char] 0) -ge 0) {
        throw "$Description is empty, absolute, or otherwise unsafe: '$Value'."
    }

    $segments = $normalized.Split("/", [StringSplitOptions]::RemoveEmptyEntries)
    if ($segments.Count -eq 0 -or $segments -contains "." -or $segments -contains "..") {
        throw "$Description contains an unsafe path segment: '$Value'."
    }

    return [string]::Join("/", $segments)
}

function Get-PathUnderRoot {
    param(
        [Parameter(Mandatory)]
        [string] $Root,

        [Parameter(Mandatory)]
        [string] $RelativePath,

        [Parameter(Mandatory)]
        [string] $Description
    )

    $normalized = Normalize-RelativePath -Value $RelativePath -Description $Description
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd("\", "/")
    $candidate = [IO.Path]::GetFullPath(
        (Join-Path $rootPath $normalized.Replace("/", [IO.Path]::DirectorySeparatorChar))
    )
    if (-not $candidate.StartsWith(
        $rootPath + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase
    )) {
        throw "$Description escapes its root: '$RelativePath'."
    }

    return $candidate
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
    if (-not $resolvedFile.StartsWith(
        $resolvedRoot + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase
    )) {
        throw "File '$resolvedFile' is outside '$resolvedRoot'."
    }

    return $resolvedFile.Substring($resolvedRoot.Length + 1).Replace("\", "/")
}

function Invoke-GitText {
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments
    )

    $repositoryGitPath = $script:ResolvedRepositoryRoot.Replace("\", "/")
    $output = @(
        & git -c "safe.directory=$repositoryGitPath" -C $script:ResolvedRepositoryRoot @Arguments 2>&1
    )
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed: $($output -join [Environment]::NewLine)"
    }

    return $output
}

function Assert-TrackedHeadFile {
    param(
        [Parameter(Mandatory)]
        [string] $RelativePath
    )

    $normalized = Normalize-RelativePath `
        -Value $RelativePath `
        -Description "Required tracked release input"
    $trackedOutput = @(
        Invoke-GitText -Arguments @(
            "ls-files",
            "--error-unmatch",
            "--",
            $normalized
        )
    )
    if ($trackedOutput.Count -ne 1 -or
        -not ([string] $trackedOutput[0]).Equals($normalized, [StringComparison]::Ordinal)) {
        throw "Required release input is not tracked at its exact path: '$normalized'."
    }

    [void] (Invoke-GitText -Arguments @(
        "diff",
        "--quiet",
        "HEAD",
        "--",
        $normalized
    ))
}

function Get-HeadBlobBytes {
    param(
        [Parameter(Mandatory)]
        [string] $RelativePath
    )

    $normalized = Normalize-RelativePath -Value $RelativePath -Description "HEAD blob path"
    $repositoryGitPath = $script:ResolvedRepositoryRoot.Replace("\", "/")
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = "git"
    $startInfo.WorkingDirectory = $script:ResolvedRepositoryRoot
    $startInfo.Arguments = (
        "-c `"safe.directory=$repositoryGitPath`" " +
        "cat-file blob `"HEAD:$normalized`""
    )
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $memory = [IO.MemoryStream]::new()
    try {
        if (-not $process.Start()) {
            throw "Could not start git while reading HEAD blob '$normalized'."
        }

        $process.StandardOutput.BaseStream.CopyTo($memory)
        $errorText = $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) {
            throw "Could not read HEAD blob '$normalized': $errorText"
        }

        return ,$memory.ToArray()
    }
    finally {
        $memory.Dispose()
        $process.Dispose()
    }
}

function Get-ByteArraySha256 {
    param(
        [Parameter(Mandatory)]
        [byte[]] $Bytes
    )

    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        return (($sha256.ComputeHash($Bytes) | ForEach-Object { $_.ToString("X2") }) -join "")
    }
    finally {
        $sha256.Dispose()
    }
}

function Write-HeadBlobToStage {
    param(
        [Parameter(Mandatory)]
        [string] $RepositoryRelative,

        [Parameter(Mandatory)]
        [string] $DestinationRelative
    )

    $destination = Get-PathUnderRoot `
        -Root $script:StagePath `
        -RelativePath $DestinationRelative `
        -Description "Staged HEAD-blob destination"
    if (Test-Path -LiteralPath $destination) {
        throw "Staging attempted to write a duplicate package path: '$DestinationRelative'."
    }

    [void] [IO.Directory]::CreateDirectory((Split-Path $destination -Parent))
    $bytes = Get-HeadBlobBytes -RelativePath $RepositoryRelative
    $stream = [IO.File]::Open(
        $destination,
        [IO.FileMode]::CreateNew,
        [IO.FileAccess]::Write,
        [IO.FileShare]::None
    )
    try {
        $stream.Write($bytes, 0, $bytes.Length)
    }
    finally {
        $stream.Dispose()
    }
}

function Get-RequiredXmlValue {
    param(
        [Parameter(Mandatory)]
        [xml] $Document,

        [Parameter(Mandatory)]
        [string] $XPath,

        [Parameter(Mandatory)]
        [string] $Description
    )

    $nodes = @($Document.SelectNodes($XPath))
    if ($nodes.Count -ne 1) {
        throw "Expected exactly one $Description at XPath '$XPath'; found $($nodes.Count)."
    }

    $value = [string] $nodes[0].InnerText
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "$Description must not be empty."
    }

    return $value.Trim()
}

function Assert-BuildEvidenceReferences {
    param(
        [Parameter(Mandatory)]
        [object] $Evidence,

        [Parameter(Mandatory)]
        [object] $CriticalReferencePins
    )

    $buildProperty = $Evidence.PSObject.Properties["Build"]
    if ($null -eq $buildProperty) {
        throw "Build evidence has no Build record."
    }

    $countProperty = $buildProperty.Value.PSObject.Properties["ReferenceCount"]
    $referencesProperty = $buildProperty.Value.PSObject.Properties["References"]
    if ($null -eq $countProperty -or
        -not ($countProperty.Value -is [int]) -or
        [int] $countProperty.Value -le 0) {
        throw "Build evidence ReferenceCount must be one positive JSON integer."
    }

    if ($null -eq $referencesProperty -or
        -not ($referencesProperty.Value -is [Array])) {
        throw "Build evidence References must be one nonempty JSON array."
    }

    $references = [object[]] $referencesProperty.Value
    if ($references.Count -eq 0 -or $references.Count -ne [int] $countProperty.Value) {
        throw (
            "Build evidence References count $($references.Count) does not equal " +
            "ReferenceCount $($countProperty.Value), or is empty."
        )
    }

    $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($reference in $references) {
        $requiredProperties = @(
            "Path",
            "Projects",
            "ReferenceNames",
            "ResolvedFrom",
            "ExpectedPathMatches",
            "Sha256",
            "Size",
            "AssemblyName",
            "AssemblyVersion",
            "FileVersion",
            "FileVersionDisplay",
            "ProductVersion",
            "OriginalFilename"
        )
        foreach ($propertyName in $requiredProperties) {
            if ($null -eq $reference.PSObject.Properties[$propertyName]) {
                throw "Build-evidence reference is missing required property '$propertyName'."
            }
        }

        $path = [string] $reference.Path
        if ([string]::IsNullOrWhiteSpace($path) -or
            -not [IO.Path]::IsPathRooted($path)) {
            throw "Every build-evidence reference must identify one rooted Path."
        }

        $normalizedPath = [IO.Path]::GetFullPath($path)
        if (-not $paths.Add($normalizedPath)) {
            throw "Build evidence contains a duplicate reference Path: '$path'."
        }

        if (-not ([string] $reference.ResolvedFrom).Equals(
            "{HintPathFromItem}",
            [StringComparison]::Ordinal
        )) {
            throw (
                "Build-evidence reference '$path' must prove resolution from " +
                "'{HintPathFromItem}'."
            )
        }

        if (-not ($reference.ExpectedPathMatches -is [bool]) -or
            $reference.ExpectedPathMatches -ne $true) {
            throw (
                "Build-evidence reference '$path' must prove its actual resolved path " +
                "exactly matched its expected path."
            )
        }

        if (-not (($reference.Size -is [int]) -or ($reference.Size -is [long])) -or
            [long] $reference.Size -le 0) {
            throw "Build-evidence reference '$path' must declare one positive JSON integer Size."
        }

        if ([string] $reference.Sha256 -notmatch "^[0-9A-Fa-f]{64}$") {
            throw "Build-evidence reference '$path' must declare one exact SHA-256."
        }

        foreach ($identityProperty in @(
            "AssemblyName",
            "AssemblyVersion",
            "FileVersion",
            "FileVersionDisplay",
            "ProductVersion",
            "OriginalFilename"
        )) {
            if ([string]::IsNullOrWhiteSpace([string] $reference.$identityProperty)) {
                throw "Build-evidence reference '$path' has empty '$identityProperty' identity."
            }
        }

        $parsedAssemblyVersion = $null
        $parsedFileVersion = $null
        if (-not [Version]::TryParse(
            [string] $reference.AssemblyVersion,
            [ref] $parsedAssemblyVersion
        ) -or -not [Version]::TryParse(
            [string] $reference.FileVersion,
            [ref] $parsedFileVersion
        )) {
            throw "Build-evidence reference '$path' has an invalid assembly or file version."
        }

        foreach ($usageProperty in @("Projects", "ReferenceNames")) {
            $usageValue = $reference.$usageProperty
            if (-not ($usageValue -is [Array]) -or $usageValue.Count -eq 0) {
                throw "Build-evidence reference '$path' must have a nonempty '$usageProperty' array."
            }

            $usages = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            foreach ($usage in $usageValue) {
                $usageText = [string] $usage
                if ([string]::IsNullOrWhiteSpace($usageText) -or
                    -not $usages.Add($usageText)) {
                    throw "Build-evidence reference '$path' has an empty or duplicate '$usageProperty' entry."
                }

                if ($usageProperty -eq "Projects") {
                    $normalizedProject = $usageText.Replace("\", "/")
                    $projectSegments = $normalizedProject.Split(
                        "/",
                        [StringSplitOptions]::RemoveEmptyEntries
                    )
                    if ([IO.Path]::IsPathRooted($usageText) -or
                        $projectSegments.Count -eq 0 -or
                        $projectSegments -contains "." -or
                        $projectSegments -contains ".." -or
                        -not $normalizedProject.EndsWith(
                            ".csproj",
                            [StringComparison]::OrdinalIgnoreCase
                        )) {
                        throw "Build-evidence reference '$path' has unsafe project usage '$usageText'."
                    }
                }
            }
        }
    }

    if (-not ($CriticalReferencePins -is [Array]) -or $CriticalReferencePins.Count -eq 0) {
        throw "Package allowlist criticalReferencePins must be one nonempty JSON array."
    }

    $pinFileNames = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase
    )
    $pinHashes = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($pin in $CriticalReferencePins) {
        $pinPropertyNames = [string[]] @($pin.PSObject.Properties.Name)
        if ($pinPropertyNames.Count -ne 2 -or
            -not ($pinPropertyNames -ccontains "fileName") -or
            -not ($pinPropertyNames -ccontains "sha256")) {
            throw (
                "Every criticalReferencePins entry must contain exactly 'fileName' " +
                "and 'sha256'."
            )
        }

        $fileName = [string] $pin.fileName
        if ([string]::IsNullOrWhiteSpace($fileName) -or
            -not ([IO.Path]::GetFileName($fileName)).Equals(
                $fileName,
                [StringComparison]::Ordinal
            ) -or -not [IO.Path]::GetExtension($fileName).Equals(
                ".dll",
                [StringComparison]::OrdinalIgnoreCase
            )) {
            throw "Critical-reference pin fileName must be one DLL leaf name: '$fileName'."
        }

        if (-not $pinFileNames.Add($fileName)) {
            throw "Package allowlist contains a duplicate critical-reference fileName: '$fileName'."
        }

        $pinSha256 = [string] $pin.sha256
        if ($pinSha256 -notmatch "^[0-9A-F]{64}$") {
            throw "Critical-reference pin '$fileName' must declare one uppercase SHA-256."
        }

        if (-not $pinHashes.Add($pinSha256)) {
            throw "Package allowlist contains a duplicate critical-reference SHA-256: '$pinSha256'."
        }

        $matchingReferences = @(
            $references |
                Where-Object {
                    [IO.Path]::GetFileName([string] $_.Path).Equals(
                        $fileName,
                        [StringComparison]::OrdinalIgnoreCase
                    )
                }
        )
        if ($matchingReferences.Count -ne 1) {
            throw (
                "Build evidence must contain exactly one reference whose Path leaf is " +
                "critical pinned file '$fileName'; found $($matchingReferences.Count)."
            )
        }

        if (-not ([string] $matchingReferences[0].Sha256).Equals(
            $pinSha256,
            [StringComparison]::Ordinal
        )) {
            throw (
                "Build-evidence SHA-256 for critical reference '$fileName' does not " +
                "match its package allowlist pin."
            )
        }
    }
}

function Assert-NewEmptyOutputDirectory {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    if (Test-Path -LiteralPath $Path) {
        if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
            throw "Output path exists and is not a directory: '$Path'."
        }

        if (@(Get-ChildItem -LiteralPath $Path -Force).Count -ne 0) {
            throw "Output directory must be new or completely empty: '$Path'."
        }
    }
}

function Assert-TargetDoesNotExist {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $Description
    )

    if (Test-Path -LiteralPath $Path) {
        throw "$Description already exists; release packaging never reuses or updates targets: '$Path'."
    }
}

function Copy-ExactFile {
    param(
        [Parameter(Mandatory)]
        [string] $Source,

        [Parameter(Mandatory)]
        [string] $DestinationRelative
    )

    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
        throw "Package source file was not found: '$Source'."
    }

    $destination = Get-PathUnderRoot `
        -Root $script:StagePath `
        -RelativePath $DestinationRelative `
        -Description "Staged destination"
    if (Test-Path -LiteralPath $destination) {
        throw "Staging attempted to write a duplicate package path: '$DestinationRelative'."
    }

    $parent = Split-Path $destination -Parent
    [void] [IO.Directory]::CreateDirectory($parent)
    [IO.File]::Copy($Source, $destination, $false)
}

function Get-StreamSha256 {
    param(
        [Parameter(Mandatory)]
        [IO.Stream] $Stream
    )

    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        $hashBytes = $sha256.ComputeHash($Stream)
        return (($hashBytes | ForEach-Object { $_.ToString("X2") }) -join "")
    }
    finally {
        $sha256.Dispose()
    }
}

function Get-BaselineEntryMap {
    param(
        [Parameter(Mandatory)]
        [IO.Compression.ZipArchive] $Archive,

        [Parameter(Mandatory)]
        [object[]] $Specifications
    )

    $expectedPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($specification in $Specifications) {
        $sourcePathProperty = $specification.PSObject.Properties["sourcePath"]
        $sourcePath = if ($null -eq $sourcePathProperty) {
            [string] $specification.path
        }
        else {
            [string] $sourcePathProperty.Value
        }
        $path = Normalize-RelativePath `
            -Value $sourcePath `
            -Description "Baseline archive bundle path"
        if (-not $expectedPaths.Add($path)) {
            throw "Baseline archive bundle inventory contains a duplicate or case collision: '$path'."
        }
    }

    $entries = [Collections.Generic.Dictionary[string, IO.Compression.ZipArchiveEntry]]::new(
        [StringComparer]::OrdinalIgnoreCase
    )
    foreach ($entry in $Archive.Entries) {
        if ([string]::IsNullOrEmpty($entry.Name)) {
            continue
        }

        $entryPath = $entry.FullName.Replace("\", "/")
        if (-not $expectedPaths.Contains($entryPath)) {
            continue
        }

        $declaredPath = @(
            $Specifications |
                Where-Object {
                    $candidateSourcePathProperty = $_.PSObject.Properties["sourcePath"]
                    $candidateSourcePath = if ($null -eq $candidateSourcePathProperty) {
                        [string] $_.path
                    }
                    else {
                        [string] $candidateSourcePathProperty.Value
                    }
                    $candidateSourcePath.Equals(
                        $entryPath,
                        [StringComparison]::OrdinalIgnoreCase
                    )
                }
        )[0]
        $declaredSourcePathProperty = $declaredPath.PSObject.Properties["sourcePath"]
        $declaredSourcePath = if ($null -eq $declaredSourcePathProperty) {
            [string] $declaredPath.path
        }
        else {
            [string] $declaredSourcePathProperty.Value
        }
        if (-not $entryPath.Equals($declaredSourcePath, [StringComparison]::Ordinal)) {
            throw "Baseline archive bundle path has unexpected casing: '$entryPath'."
        }

        if ($entries.ContainsKey($entryPath)) {
            throw "Baseline archive contains a duplicate or case-colliding bundle entry: '$entryPath'."
        }

        $entries.Add($entryPath, $entry)
    }

    foreach ($path in $expectedPaths) {
        if (-not $entries.ContainsKey($path)) {
            throw "Baseline archive is missing allowlisted bundle: '$path'."
        }
    }

    return ,$entries
}

function Get-ContentInventory {
    param(
        [Parameter(Mandatory)]
        [string] $Root
    )

    $relativePaths = [string[]] @(
        Get-ChildItem -LiteralPath $Root -File -Recurse |
            ForEach-Object {
                Get-RelativeFilePath -Root $Root -File $_.FullName
            }
    )
    [Array]::Sort($relativePaths, [StringComparer]::Ordinal)

    $inventory = @(
        foreach ($relativePath in $relativePaths) {
            $fullPath = Get-PathUnderRoot `
                -Root $Root `
                -RelativePath $relativePath `
                -Description "Content inventory file"
            $file = Get-Item -LiteralPath $fullPath
            [PSCustomObject] [ordered] @{
                path = $relativePath
                size = [long] $file.Length
                sha256 = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash.ToUpperInvariant()
            }
        }
    )

    return $inventory
}

function Assert-ContentInventoriesEqual {
    param(
        [Parameter(Mandatory)]
        [object[]] $Expected,

        [Parameter(Mandatory)]
        [object[]] $Actual,

        [Parameter(Mandatory)]
        [string] $ActualDescription
    )

    if ($Actual.Count -ne $Expected.Count) {
        throw (
            "$ActualDescription contains $($Actual.Count) files; " +
            "expected $($Expected.Count) staged files."
        )
    }

    for ($index = 0; $index -lt $Expected.Count; $index++) {
        $expectedEntry = $Expected[$index]
        $actualEntry = $Actual[$index]
        if (-not ([string] $actualEntry.path).Equals(
            [string] $expectedEntry.path,
            [StringComparison]::Ordinal
        )) {
            throw (
                "$ActualDescription path mismatch at index ${index}: " +
                "expected '$($expectedEntry.path)'; found '$($actualEntry.path)'."
            )
        }

        if ([long] $actualEntry.size -ne [long] $expectedEntry.size) {
            throw (
                "$ActualDescription size mismatch for '$($expectedEntry.path)': " +
                "expected $($expectedEntry.size); found $($actualEntry.size)."
            )
        }

        if (-not ([string] $actualEntry.sha256).Equals(
            [string] $expectedEntry.sha256,
            [StringComparison]::Ordinal
        )) {
            throw (
                "$ActualDescription SHA-256 mismatch for '$($expectedEntry.path)': " +
                "expected $($expectedEntry.sha256); found $($actualEntry.sha256)."
            )
        }
    }
}

function Write-JsonCreateNew {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [object] $Value
    )

    Assert-TargetDoesNotExist -Path $Path -Description "Release evidence sidecar"
    $json = $Value | ConvertTo-Json -Depth 12
    $encoding = [Text.UTF8Encoding]::new($false)
    $bytes = $encoding.GetBytes($json + [Environment]::NewLine)
    $stream = [IO.File]::Open(
        $Path,
        [IO.FileMode]::CreateNew,
        [IO.FileAccess]::Write,
        [IO.FileShare]::None
    )
    try {
        $stream.Write($bytes, 0, $bytes.Length)
    }
    finally {
        $stream.Dispose()
    }
}

function New-DeterministicZip {
    param(
        [Parameter(Mandatory)]
        [string] $SourceDirectory,

        [Parameter(Mandatory)]
        [string] $ArchivePath
    )

    Assert-TargetDoesNotExist -Path $ArchivePath -Description "Release archive"

    $relativePaths = [string[]] @(
        Get-ChildItem -LiteralPath $SourceDirectory -File -Recurse |
            ForEach-Object {
                Get-RelativeFilePath -Root $SourceDirectory -File $_.FullName
            }
    )
    [Array]::Sort($relativePaths, [StringComparer]::Ordinal)

    $fixedTimestamp = [DateTimeOffset]::new(
        1980,
        1,
        1,
        0,
        0,
        0,
        [TimeSpan]::Zero
    )
    $archiveStream = [IO.File]::Open(
        $ArchivePath,
        [IO.FileMode]::CreateNew,
        [IO.FileAccess]::ReadWrite,
        [IO.FileShare]::None
    )
    try {
        $archive = [IO.Compression.ZipArchive]::new(
            $archiveStream,
            [IO.Compression.ZipArchiveMode]::Create,
            $false
        )
        try {
            foreach ($relativePath in $relativePaths) {
                $source = Get-PathUnderRoot `
                    -Root $SourceDirectory `
                    -RelativePath $relativePath `
                    -Description "ZIP source"
                $entry = $archive.CreateEntry(
                    $relativePath,
                    [IO.Compression.CompressionLevel]::Optimal
                )
                $entry.LastWriteTime = $fixedTimestamp

                $sourceStream = [IO.File]::OpenRead($source)
                $entryStream = $entry.Open()
                try {
                    $sourceStream.CopyTo($entryStream)
                }
                finally {
                    $entryStream.Dispose()
                    $sourceStream.Dispose()
                }
            }
        }
        finally {
            $archive.Dispose()
        }
    }
    finally {
        $archiveStream.Dispose()
    }
}

$ResolvedRepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$resolvedManifestPath = (Resolve-Path -LiteralPath $ManifestPath).Path
$resolvedBaselineArchive = (Resolve-Path -LiteralPath $BaselineAssetArchive).Path
$resolvedBuildEvidencePath = (Resolve-Path -LiteralPath $BuildEvidencePath).Path
$resolvedOutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$repositoryPrefix = $ResolvedRepositoryRoot.TrimEnd("\", "/") + [IO.Path]::DirectorySeparatorChar
if ($resolvedOutputDirectory.Equals(
    $ResolvedRepositoryRoot,
    [StringComparison]::OrdinalIgnoreCase
) -or $resolvedOutputDirectory.StartsWith(
    $repositoryPrefix,
    [StringComparison]::OrdinalIgnoreCase
)) {
    throw "OutputDirectory must be outside the entire source repository: '$resolvedOutputDirectory'."
}

if (-not (Test-Path -LiteralPath $resolvedBuildEvidencePath -PathType Leaf) -or
    $resolvedBuildEvidencePath.Equals(
        $ResolvedRepositoryRoot,
        [StringComparison]::OrdinalIgnoreCase
    ) -or $resolvedBuildEvidencePath.StartsWith(
        $repositoryPrefix,
        [StringComparison]::OrdinalIgnoreCase
    )) {
    throw "BuildEvidencePath must be one existing file outside the source repository."
}

if (-not [IO.Path]::GetExtension($resolvedBaselineArchive).Equals(
    ".zip",
    [StringComparison]::OrdinalIgnoreCase
)) {
    throw "Baseline asset archive must be a ZIP: '$resolvedBaselineArchive'."
}

$propsPath = Join-Path $ResolvedRepositoryRoot "Directory.Build.props"
[xml] $props = Get-Content -LiteralPath $propsPath -Raw
$version = Get-RequiredXmlValue `
    -Document $props `
    -XPath "/Project/PropertyGroup[not(@Condition)]/Version" `
    -Description "release Version"
$targetSptVersion = Get-RequiredXmlValue `
    -Document $props `
    -XPath "/Project/PropertyGroup[not(@Condition)]/TargetSptVersion" `
    -Description "target SPT version"
$solutionName = Get-RequiredXmlValue `
    -Document $props `
    -XPath "/Project/PropertyGroup[not(@Condition)]/SolutionName" `
    -Description "solution name"

$globalJsonPath = Join-Path $ResolvedRepositoryRoot "global.json"
$globalJson = Get-Content -LiteralPath $globalJsonPath -Raw | ConvertFrom-Json
$pinnedDotnetSdkVersion = [string] $globalJson.sdk.version
if ($pinnedDotnetSdkVersion -notmatch "^[0-9]+\.[0-9]+\.[0-9]+$") {
    throw "global.json must pin one exact three-part .NET SDK version."
}

$versionParts = $version.Split(".")
if ($versionParts.Count -ne 3 -or $version -notmatch "^[0-9]+\.[0-9]+\.[0-9]+$") {
    throw "Release Version must be a three-part numeric version; found '$version'."
}

$expectedBinaryVersion = [Version] (
    "{0}.{1}.{2}.0" -f $versionParts[0], $versionParts[1], $versionParts[2]
)

$workingTreeChanges = @(
    Invoke-GitText -Arguments @("status", "--porcelain=v1", "--untracked-files=all") |
        Where-Object { -not [string]::IsNullOrWhiteSpace([string] $_) }
)
if ($workingTreeChanges.Count -ne 0) {
    throw (
        "Release packaging requires a clean tracked and untracked worktree. " +
        "Commit or remove these changes first:" +
        [Environment]::NewLine +
        ($workingTreeChanges -join [Environment]::NewLine)
    )
}

foreach ($releaseInput in @(
    "Directory.Build.props",
    "global.json",
    "tools/New-ReleasePackage.ps1",
    "tools/Test-PackageLayout.ps1",
    "tools/PackageContract.ps1",
    "tools/package-layout.allowlist.json",
    "tools/verify-local.ps1"
)) {
    Assert-TrackedHeadFile -RelativePath $releaseInput
}

$headOutput = @(Invoke-GitText -Arguments @("rev-parse", "--verify", "HEAD"))
if ($headOutput.Count -ne 1 -or ([string] $headOutput[0]) -notmatch "^[0-9a-fA-F]{40}$") {
    throw "Could not resolve one exact 40-character packaging source revision."
}

$sourceRevision = ([string] $headOutput[0]).ToLowerInvariant()
$treeOutput = @(Invoke-GitText -Arguments @("rev-parse", "--verify", "HEAD^{tree}"))
if ($treeOutput.Count -ne 1 -or ([string] $treeOutput[0]) -notmatch "^[0-9a-fA-F]{40}$") {
    throw "Could not resolve one exact 40-character packaging source tree."
}

$sourceTree = ([string] $treeOutput[0]).ToLowerInvariant()
$expectedInformationalVersion = "$version+$sourceRevision"
$manifestBytes = Get-HeadBlobBytes -RelativePath "tools/package-layout.allowlist.json"
$manifestText = [Text.UTF8Encoding]::new($false, $true).GetString($manifestBytes)
$manifest = $manifestText | ConvertFrom-Json
Assert-TscOnlyPackageContract -Manifest $manifest
$manifestHash = Get-ByteArraySha256 -Bytes $manifestBytes

$buildEvidenceFile = Get-Item -LiteralPath $resolvedBuildEvidencePath
$buildEvidenceHash = (
    Get-FileHash -LiteralPath $resolvedBuildEvidencePath -Algorithm SHA256
).Hash.ToUpperInvariant()
$buildEvidence = Get-Content -LiteralPath $resolvedBuildEvidencePath -Raw -Encoding UTF8 | ConvertFrom-Json
if ([int] $buildEvidence.SchemaVersion -ne 1) {
    throw "Unsupported release build evidence schema '$($buildEvidence.SchemaVersion)'."
}

if (-not ([string] $buildEvidence.Repository.Head).Equals(
    $sourceRevision,
    [StringComparison]::Ordinal
) -or -not ([string] $buildEvidence.Repository.Tree).Equals(
    $sourceTree,
    [StringComparison]::Ordinal
) -or -not ($buildEvidence.Repository.WorktreeCleanBeforeAndAfter -is [bool]) -or
    $buildEvidence.Repository.WorktreeCleanBeforeAndAfter -ne $true) {
    throw "Build evidence does not identify the current clean packaging HEAD and tree."
}

if (-not ([string] $buildEvidence.Build.Configuration).Equals(
    "SPT-4.1 Release",
    [StringComparison]::Ordinal
) -or -not ($buildEvidence.Build.SkipTscDeploy -is [bool]) -or
    $buildEvidence.Build.SkipTscDeploy -ne $true) {
    throw "Build evidence must come from SPT-4.1 Release with SkipTscDeploy=true."
}

if (-not ([string] $buildEvidence.Build.DotnetSdkVersion).Equals(
    $pinnedDotnetSdkVersion,
    [StringComparison]::Ordinal
) -or -not ([string] $buildEvidence.Build.ReleaseVersion).Equals(
    $version,
    [StringComparison]::Ordinal
) -or -not ([string] $buildEvidence.Build.ExpectedAssemblyVersion).Equals(
    $expectedBinaryVersion.ToString(),
    [StringComparison]::Ordinal
) -or -not ([string] $buildEvidence.Build.ExpectedProductVersion).Equals(
    $expectedInformationalVersion,
    [StringComparison]::Ordinal
)) {
    throw "Build evidence SDK or release-version identity does not match the packaging commit."
}

$criticalReferencePinsProperty = $manifest.PSObject.Properties["criticalReferencePins"]
if ($null -eq $criticalReferencePinsProperty) {
    throw "Package allowlist has no criticalReferencePins array."
}

Assert-BuildEvidenceReferences `
    -Evidence $buildEvidence `
    -CriticalReferencePins $criticalReferencePinsProperty.Value

$buildEvidenceOutputs = @($buildEvidence.Build.Outputs)
if ($buildEvidenceOutputs.Count -ne [int] $manifest.exactCounts.builtDlls) {
    throw (
        "Build evidence contains $($buildEvidenceOutputs.Count) runtime outputs; " +
        "expected exactly $($manifest.exactCounts.builtDlls) freshly built TSC DLLs."
    )
}

foreach ($evidenceOutput in $buildEvidenceOutputs) {
    if ([string]::IsNullOrWhiteSpace([string] $evidenceOutput.TargetPath) -or
        -not [IO.Path]::IsPathRooted([string] $evidenceOutput.TargetPath)) {
        throw "Every build-evidence output must identify one absolute TargetPath."
    }
}

$layoutChecker = Join-Path $PSScriptRoot "Test-PackageLayout.ps1"
& $layoutChecker `
    -ManifestPath $resolvedManifestPath `
    -SourceRoot $ResolvedRepositoryRoot `
    -ValidateSourceInputs
if (-not $?) {
    throw "Package source inventory validation failed."
}

$baselineExpectedHash = ([string] $manifest.baselineAssetArchive.sha256).ToUpperInvariant()
$baselineExpectedName = [string] $manifest.baselineAssetArchive.fileName
$baselineExpectedLength = [long] $manifest.baselineAssetArchive.length
$baselineFile = Get-Item -LiteralPath $resolvedBaselineArchive
if ($baselineFile.Name -cne $baselineExpectedName) {
    throw (
        "Baseline asset archive name mismatch. " +
        "Expected '$baselineExpectedName'; found '$($baselineFile.Name)'."
    )
}

if ($baselineFile.Length -ne $baselineExpectedLength) {
    throw (
        "Baseline asset archive length mismatch. " +
        "Expected $baselineExpectedLength; found $($baselineFile.Length)."
    )
}

$baselineActualHash = (Get-FileHash -LiteralPath $resolvedBaselineArchive -Algorithm SHA256).Hash.ToUpperInvariant()
if ($baselineActualHash -cne $baselineExpectedHash) {
    throw (
        "Baseline asset archive SHA-256 mismatch. " +
        "Expected $baselineExpectedHash; found $baselineActualHash."
    )
}

$validatedArtifacts = @()
$matchedBuildEvidenceTargets = [Collections.Generic.HashSet[string]]::new(
    [StringComparer]::OrdinalIgnoreCase
)
foreach ($artifact in $manifest.buildArtifacts) {
    $sourceRelative = Normalize-RelativePath `
        -Value ([string] $artifact.source) `
        -Description "Build artifact source"
    $source = Get-PathUnderRoot `
        -Root $ResolvedRepositoryRoot `
        -RelativePath $sourceRelative `
        -Description "Build artifact source"
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Required freshly built DLL is missing: '$sourceRelative'."
    }

    $assemblyName = [Reflection.AssemblyName]::GetAssemblyName($source)
    if (-not $assemblyName.Name.Equals(
        [string] $artifact.assemblyName,
        [StringComparison]::Ordinal
    )) {
        throw (
            "DLL '$sourceRelative' has assembly name '$($assemblyName.Name)'; " +
            "expected '$($artifact.assemblyName)'."
        )
    }

    if ($assemblyName.Version -ne $expectedBinaryVersion) {
        throw (
            "DLL '$sourceRelative' has AssemblyVersion '$($assemblyName.Version)'; " +
            "expected '$expectedBinaryVersion'."
        )
    }

    $fileInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($source)
    $actualFileVersion = [Version] (
        "{0}.{1}.{2}.{3}" -f
            $fileInfo.FileMajorPart,
            $fileInfo.FileMinorPart,
            $fileInfo.FileBuildPart,
            $fileInfo.FilePrivatePart
    )
    if ($actualFileVersion -ne $expectedBinaryVersion) {
        throw (
            "DLL '$sourceRelative' has FileVersion '$actualFileVersion'; " +
            "expected '$expectedBinaryVersion'."
        )
    }

    if (-not ([string] $fileInfo.ProductVersion).Equals(
        $expectedInformationalVersion,
        [StringComparison]::Ordinal
    )) {
        throw (
            "DLL '$sourceRelative' has AssemblyInformationalVersion " +
            "'$($fileInfo.ProductVersion)'; expected '$expectedInformationalVersion'. " +
            "Rebuild all runtime projects from the clean packaging commit."
        )
    }

    $destination = Normalize-RelativePath `
        -Value ([string] $artifact.destination) `
        -Description "Build artifact destination"
    $sourceFile = Get-Item -LiteralPath $source
    $sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToUpperInvariant()
    $matchingEvidenceOutputs = @(
        $buildEvidenceOutputs |
            Where-Object {
                [IO.Path]::GetFullPath([string] $_.TargetPath).Equals(
                    $source,
                    [StringComparison]::OrdinalIgnoreCase
                )
            }
    )
    if ($matchingEvidenceOutputs.Count -ne 1) {
        throw (
            "Build evidence must contain exactly one output for '$sourceRelative'; " +
            "found $($matchingEvidenceOutputs.Count)."
        )
    }

    if (-not $matchedBuildEvidenceTargets.Add($source)) {
        throw "Build evidence target was matched more than once: '$sourceRelative'."
    }

    $evidenceOutput = $matchingEvidenceOutputs[0]
    if (-not ([string] $evidenceOutput.StagedFileName).Equals(
        [IO.Path]::GetFileName($destination),
        [StringComparison]::Ordinal
    ) -or [long] $evidenceOutput.Size -ne [long] $sourceFile.Length -or
        -not ([string] $evidenceOutput.Sha256).Equals(
            $sourceHash,
            [StringComparison]::OrdinalIgnoreCase
        ) -or -not ([string] $evidenceOutput.AssemblyName).Equals(
            $assemblyName.Name,
            [StringComparison]::Ordinal
        ) -or -not ([string] $evidenceOutput.AssemblyVersion).Equals(
            $assemblyName.Version.ToString(),
            [StringComparison]::Ordinal
        ) -or -not ([string] $evidenceOutput.FileVersion).Equals(
            $actualFileVersion.ToString(),
            [StringComparison]::Ordinal
        ) -or -not ([string] $evidenceOutput.ProductVersion).Equals(
            [string] $fileInfo.ProductVersion,
            [StringComparison]::Ordinal
        )) {
        throw "Build evidence metadata or SHA-256 does not match '$sourceRelative'."
    }

    $validatedArtifacts += [PSCustomObject] [ordered] @{
        Source = $source
        Destination = $destination
        AssemblyName = $assemblyName.Name
        AssemblyVersion = $assemblyName.Version.ToString()
        FileVersion = $actualFileVersion.ToString()
        InformationalVersion = [string] $fileInfo.ProductVersion
        Size = [long] $sourceFile.Length
        Sha256 = $sourceHash
    }
}

if ($matchedBuildEvidenceTargets.Count -ne $buildEvidenceOutputs.Count) {
    throw "Build evidence contains an output that is not one of the four manifest build artifacts."
}

$validatedCopiedFiles = @()
foreach ($copiedFile in $manifest.copiedFiles) {
    $sourceRelative = Normalize-RelativePath `
        -Value ([string] $copiedFile.source) `
        -Description "Copied source file"
    $source = Get-PathUnderRoot `
        -Root $ResolvedRepositoryRoot `
        -RelativePath $sourceRelative `
        -Description "Copied source file"
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Required copied source file is missing: '$sourceRelative'."
    }

    $validatedCopiedFiles += [PSCustomObject] @{
        SourceRelative = $sourceRelative
        Destination = Normalize-RelativePath `
            -Value ([string] $copiedFile.destination) `
            -Description "Copied package destination"
    }
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$baselineSpecifications = @($manifest.baselineAssetArchive.files)
$baselineZip = [IO.Compression.ZipFile]::OpenRead($resolvedBaselineArchive)
try {
    $baselineEntries = Get-BaselineEntryMap `
        -Archive $baselineZip `
        -Specifications $baselineSpecifications

    foreach ($specification in $baselineSpecifications) {
        $sourcePathProperty = $specification.PSObject.Properties["sourcePath"]
        $sourcePath = if ($null -eq $sourcePathProperty) {
            [string] $specification.path
        }
        else {
            [string] $sourcePathProperty.Value
        }
        $sourcePath = Normalize-RelativePath `
            -Value $sourcePath `
            -Description "Baseline archive bundle path"
        $packagePath = Normalize-RelativePath `
            -Value ([string] $specification.path) `
            -Description "Baseline package bundle path"
        $entry = $baselineEntries[$sourcePath]
        if ($entry.Length -ne [long] $specification.length) {
            throw (
                "Baseline bundle '$sourcePath' for '$packagePath' has length $($entry.Length); " +
                "expected $($specification.length)."
            )
        }

        $entryStream = $entry.Open()
        try {
            $actualHash = Get-StreamSha256 -Stream $entryStream
        }
        finally {
            $entryStream.Dispose()
        }

        $expectedHash = ([string] $specification.sha256).ToUpperInvariant()
        if ($actualHash -cne $expectedHash) {
            throw "Baseline bundle '$sourcePath' for '$packagePath' SHA-256 mismatch: expected $expectedHash; found $actualHash."
        }

        $overrideSourceProperty = $specification.PSObject.Properties["overrideSource"]
        if ($null -ne $overrideSourceProperty) {
            $overrideSource = Normalize-RelativePath `
                -Value ([string] $overrideSourceProperty.Value) `
                -Description "Bundle override source"
            $overridePath = Get-PathUnderRoot `
                -Root $ResolvedRepositoryRoot `
                -RelativePath $overrideSource `
                -Description "Bundle override source"
            if (-not (Test-Path -LiteralPath $overridePath -PathType Leaf)) {
                throw "Bundle override source was not found: '$overrideSource'."
            }

            $overrideFile = Get-Item -LiteralPath $overridePath
            $overrideHash = (Get-FileHash -LiteralPath $overridePath -Algorithm SHA256).Hash.ToUpperInvariant()
            if ($overrideFile.Length -ne [long] $specification.overrideLength -or
                $overrideHash -cne ([string] $specification.overrideSha256).ToUpperInvariant()) {
                throw "Bundle override source pin mismatch: '$overrideSource'."
            }
        }
    }

    Assert-NewEmptyOutputDirectory -Path $resolvedOutputDirectory
    [void] [IO.Directory]::CreateDirectory($resolvedOutputDirectory)

    $archiveName = "$solutionName-v$version-SPT$targetSptVersion-TESTER.zip"
    $StagePath = Join-Path $resolvedOutputDirectory "stage"
    $extractPath = Join-Path $resolvedOutputDirectory "verify-extracted"
    $archivePath = Join-Path $resolvedOutputDirectory $archiveName
    $evidenceName = "$([IO.Path]::GetFileNameWithoutExtension($archiveName)).content-evidence.json"
    $evidencePath = Join-Path $resolvedOutputDirectory $evidenceName

    Assert-TargetDoesNotExist -Path $StagePath -Description "Staging directory"
    Assert-TargetDoesNotExist -Path $extractPath -Description "Validation extraction directory"
    Assert-TargetDoesNotExist -Path $archivePath -Description "Release archive"
    Assert-TargetDoesNotExist -Path $evidencePath -Description "Release evidence sidecar"
    [void] [IO.Directory]::CreateDirectory($StagePath)

    foreach ($mirror in $manifest.mirrors) {
        $sourceRoot = Normalize-RelativePath `
            -Value ([string] $mirror.source) `
            -Description "Mirror source"
        $destinationRoot = Normalize-RelativePath `
            -Value ([string] $mirror.destination) `
            -Description "Mirror destination"
        foreach ($declaredFile in $mirror.files) {
            $relative = Normalize-RelativePath `
                -Value ([string] $declaredFile) `
                -Description "Reviewed mirror file"
            Write-HeadBlobToStage `
                -RepositoryRelative "$sourceRoot/$relative" `
                -DestinationRelative "$destinationRoot/$relative"
        }
    }

    foreach ($copiedFile in $validatedCopiedFiles) {
        Write-HeadBlobToStage `
            -RepositoryRelative ([string] $copiedFile.SourceRelative) `
            -DestinationRelative ([string] $copiedFile.Destination)
    }

    foreach ($artifact in $validatedArtifacts) {
        Copy-ExactFile `
            -Source ([string] $artifact.Source) `
            -DestinationRelative ([string] $artifact.Destination)
    }

    foreach ($specification in $baselineSpecifications) {
        $sourcePathProperty = $specification.PSObject.Properties["sourcePath"]
        $sourcePath = if ($null -eq $sourcePathProperty) {
            [string] $specification.path
        }
        else {
            [string] $sourcePathProperty.Value
        }
        $sourcePath = Normalize-RelativePath `
            -Value $sourcePath `
            -Description "Baseline archive bundle path"
        $path = Normalize-RelativePath `
            -Value ([string] $specification.path) `
            -Description "Baseline package bundle path"
        $destination = Get-PathUnderRoot `
            -Root $StagePath `
            -RelativePath $path `
            -Description "Staged baseline bundle"
        if (Test-Path -LiteralPath $destination) {
            throw "Staging attempted to overwrite package path with baseline bundle: '$path'."
        }

        $overrideSourceProperty = $specification.PSObject.Properties["overrideSource"]
        if ($null -ne $overrideSourceProperty) {
            $overrideSource = Normalize-RelativePath `
                -Value ([string] $overrideSourceProperty.Value) `
                -Description "Bundle override source"
            $overridePath = Get-PathUnderRoot `
                -Root $ResolvedRepositoryRoot `
                -RelativePath $overrideSource `
                -Description "Bundle override source"
            [void] [IO.Directory]::CreateDirectory((Split-Path $destination -Parent))
            [IO.File]::Copy($overridePath, $destination, $false)
        }
        else {
            $entry = $baselineEntries[$sourcePath]
            [void] [IO.Directory]::CreateDirectory((Split-Path $destination -Parent))
            $sourceStream = $entry.Open()
            $destinationStream = [IO.File]::Open(
                $destination,
                [IO.FileMode]::CreateNew,
                [IO.FileAccess]::Write,
                [IO.FileShare]::None
            )
            try {
                $sourceStream.CopyTo($destinationStream)
            }
            finally {
                $destinationStream.Dispose()
                $sourceStream.Dispose()
            }
        }
    }
}
finally {
    $baselineZip.Dispose()
}

& $layoutChecker `
    -Path $StagePath `
    -ManifestPath $resolvedManifestPath `
    -SourceRoot $ResolvedRepositoryRoot
if (-not $?) {
    throw "Staged package layout validation failed."
}

$stageInventory = @(Get-ContentInventory -Root $StagePath)
New-DeterministicZip -SourceDirectory $StagePath -ArchivePath $archivePath

$archiveGuard = [IO.File]::Open(
    $archivePath,
    [IO.FileMode]::Open,
    [IO.FileAccess]::Read,
    [IO.FileShare]::Read
)
try {
$initialArchiveFile = Get-Item -LiteralPath $archivePath
$initialArchiveLength = [long] $initialArchiveFile.Length
$initialArchiveHash = (
    Get-FileHash -LiteralPath $archivePath -Algorithm SHA256
).Hash.ToUpperInvariant()
& $layoutChecker `
    -Path $archivePath `
    -ManifestPath $resolvedManifestPath `
    -SourceRoot $ResolvedRepositoryRoot
if (-not $?) {
    throw "Release ZIP layout validation failed."
}

Assert-TargetDoesNotExist -Path $extractPath -Description "Validation extraction directory"
[void] [IO.Directory]::CreateDirectory($extractPath)
[IO.Compression.ZipFile]::ExtractToDirectory($archivePath, $extractPath)

& $layoutChecker `
    -Path $extractPath `
    -ManifestPath $resolvedManifestPath `
    -SourceRoot $ResolvedRepositoryRoot
if (-not $?) {
    throw "Extracted release ZIP validation failed."
}

$extractedInventory = @(Get-ContentInventory -Root $extractPath)
Assert-ContentInventoriesEqual `
    -Expected $stageInventory `
    -Actual $extractedInventory `
    -ActualDescription "Extracted release ZIP"

$archiveFile = Get-Item -LiteralPath $archivePath
$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToUpperInvariant()
if ($archiveFile.Length -ne $initialArchiveLength -or
    $archiveHash -cne $initialArchiveHash) {
    throw "Release archive changed after deterministic creation."
}

$fileCount = $stageInventory.Count
$dllCount = @(
    $stageInventory |
        Where-Object {
            ([string] $_.path).EndsWith(".dll", [StringComparison]::OrdinalIgnoreCase)
        }
).Count
$bundleCount = @(
    $stageInventory |
        Where-Object {
            ([string] $_.path).EndsWith(".bundle", [StringComparison]::OrdinalIgnoreCase)
        }
).Count

if ($dllCount -ne [int] $manifest.exactCounts.".dll" -or
    $bundleCount -ne [int] $manifest.exactCounts.".bundle") {
    throw (
        "Content evidence count mismatch: $dllCount DLLs and $bundleCount bundles; " +
        "expected $($manifest.exactCounts.'.dll') DLLs and $($manifest.exactCounts.'.bundle') bundles."
    )
}

$stageEntries = [Collections.Generic.Dictionary[string, object]]::new(
    [StringComparer]::Ordinal
)
foreach ($entry in $stageInventory) {
    $stageEntries.Add([string] $entry.path, $entry)
}

$artifactDestinations = [string[]] @(
    $validatedArtifacts |
        ForEach-Object { [string] $_.Destination }
)
[Array]::Sort($artifactDestinations, [StringComparer]::Ordinal)
$dllEvidence = @(
    foreach ($destination in $artifactDestinations) {
        $artifact = @(
            $validatedArtifacts |
                Where-Object {
                    ([string] $_.Destination).Equals($destination, [StringComparison]::Ordinal)
                }
        )[0]
        $stagedEntry = $stageEntries[$destination]
        if ([long] $artifact.Size -ne [long] $stagedEntry.size -or
            -not ([string] $artifact.Sha256).Equals(
                [string] $stagedEntry.sha256,
                [StringComparison]::Ordinal
            )) {
            throw "Staged DLL content differs from its validated build output: '$destination'."
        }

        [PSCustomObject] [ordered] @{
            path = $destination
            assemblyName = [string] $artifact.AssemblyName
            assemblyVersion = [string] $artifact.AssemblyVersion
            fileVersion = [string] $artifact.FileVersion
            informationalVersion = [string] $artifact.InformationalVersion
            size = [long] $stagedEntry.size
            sha256 = [string] $stagedEntry.sha256
        }
    }
)

$bundlePaths = [string[]] @(
    $baselineSpecifications |
        ForEach-Object { [string] $_.path }
)
[Array]::Sort($bundlePaths, [StringComparer]::Ordinal)
$bundleEvidence = @(
    foreach ($bundlePath in $bundlePaths) {
        $specification = @(
            $baselineSpecifications |
                Where-Object {
                    ([string] $_.path).Equals($bundlePath, [StringComparison]::Ordinal)
                }
        )[0]
        $stagedEntry = $stageEntries[$bundlePath]
        $expectedLength = if ($null -ne $specification.PSObject.Properties["overrideLength"]) {
            [long] $specification.overrideLength
        }
        else {
            [long] $specification.length
        }
        $expectedHash = if ($null -ne $specification.PSObject.Properties["overrideSha256"]) {
            ([string] $specification.overrideSha256).ToUpperInvariant()
        }
        else {
            ([string] $specification.sha256).ToUpperInvariant()
        }
        if ($expectedLength -ne [long] $stagedEntry.size -or
            -not $expectedHash.Equals(
                [string] $stagedEntry.sha256,
                [StringComparison]::Ordinal
            )) {
            throw "Staged bundle content differs from its manifest pin: '$bundlePath'."
        }

        $overrideSourceProperty = $specification.PSObject.Properties["overrideSource"]
        $sourcePathProperty = $specification.PSObject.Properties["sourcePath"]
        $evidenceSourcePath = if ($null -ne $overrideSourceProperty) {
            [string] $overrideSourceProperty.Value
        }
        elseif ($null -ne $sourcePathProperty) {
            [string] $sourcePathProperty.Value
        }
        else {
            [string] $specification.path
        }

        [PSCustomObject] [ordered] @{
            path = $bundlePath
            size = $expectedLength
            sha256 = $expectedHash
            sourceKind = if ($null -ne $overrideSourceProperty) { "trackedOverride" } else { "baselineArchive" }
            sourcePath = $evidenceSourcePath
            baselineSha256 = ([string] $specification.sha256).ToUpperInvariant()
        }
    }
)

$currentBuildEvidenceFile = Get-Item -LiteralPath $resolvedBuildEvidencePath
$currentBuildEvidenceHash = (
    Get-FileHash -LiteralPath $resolvedBuildEvidencePath -Algorithm SHA256
).Hash.ToUpperInvariant()
if ($currentBuildEvidenceFile.Length -ne $buildEvidenceFile.Length -or
    $currentBuildEvidenceHash -cne $buildEvidenceHash) {
    throw "Release build evidence changed while the package was being created."
}

$postPackageChanges = @(
    Invoke-GitText -Arguments @("status", "--porcelain=v1", "--untracked-files=all") |
        Where-Object { -not [string]::IsNullOrWhiteSpace([string] $_) }
)
$postHead = @(Invoke-GitText -Arguments @("rev-parse", "--verify", "HEAD"))
$postTree = @(Invoke-GitText -Arguments @("rev-parse", "--verify", "HEAD^{tree}"))
if ($postPackageChanges.Count -ne 0 -or
    $postHead.Count -ne 1 -or
    $postTree.Count -ne 1 -or
    -not ([string] $postHead[0]).Equals($sourceRevision, [StringComparison]::Ordinal) -or
    -not ([string] $postTree[0]).Equals($sourceTree, [StringComparison]::Ordinal)) {
    throw "Git worktree, HEAD, or tree changed while the release package was being created."
}

$evidence = [ordered] @{
    schemaVersion = 1
    source = [ordered] @{
        head = $sourceRevision
        tree = $sourceTree
    }
    manifest = [ordered] @{
        fileName = [IO.Path]::GetFileName($resolvedManifestPath)
        schemaVersion = [int] $manifest.schemaVersion
        sha256 = $manifestHash
    }
    buildEvidence = [ordered] @{
        fileName = $buildEvidenceFile.Name
        schemaVersion = [int] $buildEvidence.SchemaVersion
        size = [long] $buildEvidenceFile.Length
        sha256 = $buildEvidenceHash
    }
    baselineAssetArchive = [ordered] @{
        fileName = $baselineFile.Name
        size = [long] $baselineFile.Length
        sha256 = $baselineActualHash
    }
    release = [ordered] @{
        version = $version
        targetSptVersion = $targetSptVersion
        archive = [ordered] @{
            fileName = $archiveFile.Name
            size = [long] $archiveFile.Length
            sha256 = $archiveHash
        }
        counts = [ordered] @{
            files = $fileCount
            dlls = $dllCount
            builtDlls = $validatedArtifacts.Count
            bundles = $bundleCount
        }
        dlls = $dllEvidence
        bundlePins = $bundleEvidence
        files = $stageInventory
    }
}

Write-JsonCreateNew -Path $evidencePath -Value $evidence
$evidenceHash = (Get-FileHash -LiteralPath $evidencePath -Algorithm SHA256).Hash.ToUpperInvariant()
$finalArchiveFile = Get-Item -LiteralPath $archivePath
$finalArchiveHash = (
    Get-FileHash -LiteralPath $archivePath -Algorithm SHA256
).Hash.ToUpperInvariant()
if ($finalArchiveFile.Length -ne $initialArchiveLength -or
    $finalArchiveHash -cne $initialArchiveHash -or
    $finalArchiveHash -cne $archiveHash) {
    throw "Release archive changed before final evidence reporting."
}

Write-Host "Clean release package created and validated."
Write-Host "  Source revision: $sourceRevision"
Write-Host "  Source tree: $sourceTree"
Write-Host "  Files: $fileCount"
Write-Host "  ZIP: $archivePath"
Write-Host "  SHA-256: $archiveHash"
Write-Host "  Evidence: $evidencePath"
Write-Host "  Evidence SHA-256: $evidenceHash"

[PSCustomObject] @{
    ArchivePath = $archivePath
    Sha256 = $archiveHash
    SourceRevision = $sourceRevision
    SourceTree = $sourceTree
    FileCount = $fileCount
    DllCount = $dllCount
    BundleCount = $bundleCount
    EvidencePath = $evidencePath
    EvidenceSha256 = $evidenceHash
    StagePath = $StagePath
    ExtractedValidationPath = $extractPath
}
}
finally {
    $archiveGuard.Dispose()
}
