[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $SptDir,

    [Parameter(Mandatory)]
    [string] $SptSharedAssembliesDir,

    [string] $Configuration = "SPT-4.1 Release",

    [string] $EvidencePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path -LiteralPath (Split-Path $PSScriptRoot -Parent)).Path
$solutionPath = Join-Path $repositoryRoot "SamSWAT.FireSupport.ArysReloaded.sln"
$repositoryGitPath = $repositoryRoot.Replace("\", "/")
$evidenceMode = -not [string]::IsNullOrWhiteSpace($EvidencePath)
$resolvedEvidencePath = $null
$evidenceHead = $null
$evidenceTree = $null
$dotnetSdkVersion = $null
$releaseVersion = $null
$expectedAssemblyVersion = $null
$expectedProductVersion = $null

function Invoke-Checked {
    param(
        [Parameter(Mandatory)]
        [string] $FilePath,

        [Parameter()]
        [string[]] $Arguments = @()
    )

    Write-Host "> $FilePath $($Arguments -join ' ')"
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
    }
}

function Invoke-Captured {
    param(
        [Parameter(Mandatory)]
        [string] $FilePath,

        [Parameter()]
        [string[]] $Arguments = @()
    )

    $output = @(& $FilePath @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')`n$($output -join "`n")"
    }

    return ($output -join "`n").Trim()
}

function Test-IsPathWithinRoot {
    param(
        [Parameter(Mandatory)]
        [string] $Candidate,

        [Parameter(Mandatory)]
        [string] $Root
    )

    $normalizedCandidate = [IO.Path]::GetFullPath($Candidate).TrimEnd("\", "/")
    $normalizedRoot = [IO.Path]::GetFullPath($Root).TrimEnd("\", "/")
    if ($normalizedCandidate.Equals($normalizedRoot, [StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

    $rootPrefix = $normalizedRoot + [IO.Path]::DirectorySeparatorChar
    return $normalizedCandidate.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)
}

function Resolve-NewExternalEvidencePath {
    param(
        [Parameter(Mandatory)]
        [string] $Value
    )

    $fullPath = [IO.Path]::GetFullPath($Value)
    if (Test-Path -LiteralPath $fullPath) {
        throw "Evidence path already exists and will not be overwritten: '$fullPath'."
    }

    $leafName = [IO.Path]::GetFileName($fullPath)
    if ([string]::IsNullOrWhiteSpace($leafName)) {
        throw "Evidence path must identify a file outside the repository: '$Value'."
    }

    $parentPath = [IO.Path]::GetDirectoryName($fullPath)
    if ([string]::IsNullOrWhiteSpace($parentPath) -or
        -not (Test-Path -LiteralPath $parentPath -PathType Container)) {
        throw "Evidence parent directory was not found: '$parentPath'."
    }

    $resolvedParent = (Resolve-Path -LiteralPath $parentPath).Path
    $resolvedPath = Join-Path $resolvedParent $leafName
    if (Test-IsPathWithinRoot -Candidate $resolvedPath -Root $repositoryRoot) {
        throw "Evidence must be written outside the repository: '$resolvedPath'."
    }

    if (Test-Path -LiteralPath $resolvedPath) {
        throw "Evidence path already exists and will not be overwritten: '$resolvedPath'."
    }

    return $resolvedPath
}

function New-OrdinalIgnoreCaseSet {
    $set = [Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase
    )
    return ,$set
}

function Get-BinaryEvidence {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    $resolvedPath = (Resolve-Path -LiteralPath $Path).Path
    $item = Get-Item -LiteralPath $resolvedPath
    try {
        $assemblyName = [Reflection.AssemblyName]::GetAssemblyName($resolvedPath)
    }
    catch {
        throw (
            "Release evidence requires managed .NET assembly metadata, but " +
            "'$resolvedPath' could not be read as a managed assembly: " +
            $_.Exception.Message
        )
    }

    $fileInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($resolvedPath)
    $fileVersion = "{0}.{1}.{2}.{3}" -f
        $fileInfo.FileMajorPart,
        $fileInfo.FileMinorPart,
        $fileInfo.FileBuildPart,
        $fileInfo.FilePrivatePart

    return [pscustomobject] [ordered] @{
        Path = $resolvedPath
        Sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $resolvedPath).Hash
        Size = [long] $item.Length
        AssemblyName = $assemblyName.Name
        AssemblyVersion = $assemblyName.Version.ToString()
        FileVersion = $fileVersion
        FileVersionDisplay = $fileInfo.FileVersion
        ProductVersion = $fileInfo.ProductVersion
        OriginalFilename = $fileInfo.OriginalFilename
    }
}

function Resolve-DirectoryWithTrailingSeparator {
    param(
        [Parameter(Mandatory)]
        [string] $Value,

        [Parameter(Mandatory)]
        [string] $Description
    )

    if (-not (Test-Path -LiteralPath $Value -PathType Container)) {
        throw "$Description was not found: '$Value'."
    }

    return (Resolve-Path -LiteralPath $Value).Path.TrimEnd("\", "/") + [IO.Path]::DirectorySeparatorChar
}

function Invoke-MsBuildReferenceQuery {
    param(
        [Parameter(Mandatory)]
        [string] $ProjectPath
    )

    $resultPath = [IO.Path]::GetTempFileName()
    $arguments = @(
        "msbuild",
        $ProjectPath,
        "-nologo",
        "-verbosity:quiet",
        "-restore",
        "-target:ResolveReferences",
        "-getItem:Reference",
        "-getItem:ReferencePath",
        "-getResultOutputFile:$resultPath",
        "-property:Configuration=$Configuration",
        "-property:Platform=AnyCPU",
        "-property:SptDir=$msbuildSptDir",
        "-property:SptSharedAssembliesDir=$msbuildSharedAssemblies",
        "-property:SkipTscDeploy=true",
        "-property:ContinuousIntegrationBuild=true",
        "-property:BuildProjectReferences=false"
    )

    try {
        Write-Host "> dotnet $($arguments -join ' ')"
        $commandOutput = Invoke-Captured -FilePath "dotnet" -Arguments $arguments
        if (-not [string]::IsNullOrWhiteSpace($commandOutput)) {
            Write-Host $commandOutput
        }

        try {
            return Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
        }
        catch {
            throw "Could not parse evaluated MSBuild references for '$ProjectPath': $($_.Exception.Message)"
        }
    }
    finally {
        if (Test-Path -LiteralPath $resultPath) {
            Remove-Item -LiteralPath $resultPath -Force
        }
    }
}

if ($evidenceMode) {
    if ($Configuration -ne "SPT-4.1 Release") {
        throw "Release evidence mode requires configuration 'SPT-4.1 Release'; found '$Configuration'."
    }

    $resolvedEvidencePath = Resolve-NewExternalEvidencePath -Value $EvidencePath
    $worktreeStatus = Invoke-Captured -FilePath "git" -Arguments @(
        "-c",
        "safe.directory=$repositoryGitPath",
        "-C",
        $repositoryRoot,
        "status",
        "--porcelain=v1",
        "--untracked-files=all"
    )
    if (-not [string]::IsNullOrWhiteSpace($worktreeStatus)) {
        throw "Release evidence mode requires a clean Git worktree.`n$worktreeStatus"
    }

    $evidenceHead = Invoke-Captured -FilePath "git" -Arguments @(
        "-c",
        "safe.directory=$repositoryGitPath",
        "-C",
        $repositoryRoot,
        "rev-parse",
        "HEAD"
    )
    $evidenceTree = Invoke-Captured -FilePath "git" -Arguments @(
        "-c",
        "safe.directory=$repositoryGitPath",
        "-C",
        $repositoryRoot,
        "rev-parse",
        "HEAD^{tree}"
    )
    if ($evidenceHead -notmatch "^[0-9a-f]{40}$" -or
        $evidenceTree -notmatch "^[0-9a-f]{40}$") {
        throw "Git did not return exact 40-character HEAD/tree object IDs."
    }

    $propsPath = Join-Path $repositoryRoot "Directory.Build.props"
    try {
        [xml] $props = Get-Content -LiteralPath $propsPath -Raw
    }
    catch {
        throw "Could not parse release version from '$propsPath': $($_.Exception.Message)"
    }

    $versionNodes = @($props.SelectNodes("/Project/PropertyGroup[not(@Condition)]/Version"))
    if ($versionNodes.Count -ne 1) {
        throw "Expected exactly one unconditional Version in '$propsPath'; found $($versionNodes.Count)."
    }

    $releaseVersion = ([string] $versionNodes[0].InnerText).Trim()
    if ($releaseVersion -notmatch "^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$") {
        throw "Release evidence requires a three-part numeric Version; found '$releaseVersion'."
    }

    $expectedAssemblyVersion = "$releaseVersion.0"
    $expectedProductVersion = "$releaseVersion+$evidenceHead"

    $dotnetSdkVersion = Invoke-Captured -FilePath "dotnet" -Arguments @("--version")
    if ($dotnetSdkVersion -notmatch "^[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?$") {
        throw "dotnet --version did not return one exact SDK version; found '$dotnetSdkVersion'."
    }
}

$resolvedSptDir = Resolve-DirectoryWithTrailingSeparator -Value $SptDir -Description "SPT directory"
$resolvedSharedAssemblies = Resolve-DirectoryWithTrailingSeparator -Value $SptSharedAssembliesDir -Description "SPT shared assemblies directory"
$msbuildSptDir = $resolvedSptDir.Replace("\", "/")
$msbuildSharedAssemblies = $resolvedSharedAssemblies.Replace("\", "/")

$runtimeProjects = @(
    "project\SamSWAT.FireSupport\SamSWAT.FireSupport.Core.csproj",
    "project\SamSWAT.FireSupport.Server\SamSWAT.FireSupport.Server.csproj",
    "project\SamSWAT.FireSupport.Fika.Interop\SamSWAT.FireSupport.Fika.Interop.csproj",
    "project\SamSWAT.FireSupport.Fika\SamSWAT.FireSupport.Fika.csproj"
)

Write-Host "Resolving and attesting local proprietary/dependency references without copying them."
$resolvedReferences = @{}
foreach ($relativeProject in $runtimeProjects) {
    $projectPath = Join-Path $repositoryRoot $relativeProject
    $projectDirectory = Split-Path $projectPath -Parent
    $query = Invoke-MsBuildReferenceQuery -ProjectPath $projectPath
    $referenceItemsProperty = $query.Items.PSObject.Properties["Reference"]
    $referencePathItemsProperty = $query.Items.PSObject.Properties["ReferencePath"]
    if ($null -eq $referenceItemsProperty -or $null -eq $referencePathItemsProperty) {
        throw "MSBuild did not return both Reference and ReferencePath items for '$relativeProject'."
    }

    $explicitReferences = @(
        $referenceItemsProperty.Value |
            Where-Object {
                $hintPathProperty = $_.PSObject.Properties["HintPath"]
                $null -ne $hintPathProperty -and
                    -not [string]::IsNullOrWhiteSpace([string] $hintPathProperty.Value)
            }
    )
    $resolvedReferencePaths = @($referencePathItemsProperty.Value)
    if ($explicitReferences.Count -eq 0) {
        throw "MSBuild returned no explicit HintPath references for '$relativeProject'."
    }

    foreach ($resolvedReference in $resolvedReferencePaths) {
        $fullPathProperty = $resolvedReference.PSObject.Properties["FullPath"]
        if ($null -eq $fullPathProperty -or
            [string]::IsNullOrWhiteSpace([string] $fullPathProperty.Value)) {
            continue
        }

        $candidatePath = [IO.Path]::GetFullPath([string] $fullPathProperty.Value)
        if ($candidatePath -match '(?i)[\\/]Build[\\/](?:Debug|SPT-4\.0)(?:[\\/]|$)') {
            throw "Stale reference resolution is forbidden for '$relativeProject': '$candidatePath'."
        }
    }

    foreach ($reference in $explicitReferences) {
        $referenceName = [string] $reference.Identity
        $hintPath = [string] $reference.HintPath
        $expectedPath = if ([IO.Path]::IsPathRooted($hintPath)) {
            [IO.Path]::GetFullPath($hintPath)
        }
        else {
            [IO.Path]::GetFullPath((Join-Path $projectDirectory $hintPath))
        }

        if (-not (Test-Path -LiteralPath $expectedPath -PathType Leaf)) {
            throw "Missing required local reference for '$relativeProject': '$expectedPath'."
        }

        $matches = @(
            $resolvedReferencePaths |
                Where-Object {
                    $originalItemSpecProperty = $_.PSObject.Properties["OriginalItemSpec"]
                    $null -ne $originalItemSpecProperty -and
                        ([string] $originalItemSpecProperty.Value).Equals(
                            $referenceName,
                            [StringComparison]::OrdinalIgnoreCase
                        )
                }
        )
        if ($matches.Count -ne 1) {
            throw (
                "Explicit reference '$referenceName' in '$relativeProject' resolved " +
                "$($matches.Count) times; expected exactly once."
            )
        }

        $match = $matches[0]
        $resolvedFromProperty = $match.PSObject.Properties["ResolvedFrom"]
        $resolvedFrom = if ($null -eq $resolvedFromProperty) {
            ""
        }
        else {
            [string] $resolvedFromProperty.Value
        }
        if ($resolvedFrom -cne "{HintPathFromItem}") {
            throw (
                "Explicit reference '$referenceName' in '$relativeProject' resolved from " +
                "'$resolvedFrom'; expected '{HintPathFromItem}'."
            )
        }

        $fullPathProperty = $match.PSObject.Properties["FullPath"]
        if ($null -eq $fullPathProperty -or
            [string]::IsNullOrWhiteSpace([string] $fullPathProperty.Value)) {
            throw "Resolved reference '$referenceName' in '$relativeProject' has no FullPath."
        }

        $actualPath = [IO.Path]::GetFullPath([string] $fullPathProperty.Value)
        if (-not $actualPath.Equals($expectedPath, [StringComparison]::OrdinalIgnoreCase)) {
            throw (
                "Explicit reference '$referenceName' in '$relativeProject' resolved to " +
                "'$actualPath'; expected exact HintPath '$expectedPath'."
            )
        }

        if (-not $resolvedReferences.ContainsKey($actualPath)) {
            $resolvedReferences[$actualPath] = [pscustomobject] @{
                Path = $actualPath
                Projects = New-OrdinalIgnoreCaseSet
                ReferenceNames = New-OrdinalIgnoreCaseSet
                ResolvedFrom = $resolvedFrom
                ExpectedPathMatches = $true
            }

        }

        $referenceRecord = $resolvedReferences[$actualPath]
        if ($referenceRecord.ResolvedFrom -cne $resolvedFrom -or
            $referenceRecord.ExpectedPathMatches -ne $true) {
            throw "Reference provenance was inconsistent for '$actualPath'."
        }

        [void] $referenceRecord.Projects.Add($relativeProject.Replace("\", "/"))
        [void] $referenceRecord.ReferenceNames.Add($referenceName)
    }
}

if ($Configuration -eq "SPT-4.1 Release") {
    $manifestPath = Join-Path $PSScriptRoot "package-layout.allowlist.json"
    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    }
    catch {
        throw "Could not parse critical reference pins from '$manifestPath': $($_.Exception.Message)"
    }

    $pinsProperty = $manifest.PSObject.Properties["criticalReferencePins"]
    if ($null -eq $pinsProperty -or -not ($pinsProperty.Value -is [Array])) {
        throw "Package manifest criticalReferencePins must be one JSON array."
    }

    $criticalReferencePins = @($pinsProperty.Value)
    $expectedCriticalFiles = @(
        "hollowed.dll",
        "WTT-ClientCommonLib.dll",
        "WTT-ServerCommonLib.dll",
        "Fika.Core.dll",
        "UnityToolkit.dll"
    )
    if ($criticalReferencePins.Count -ne $expectedCriticalFiles.Count) {
        throw (
            "Package manifest contains $($criticalReferencePins.Count) critical reference pins; " +
            "expected exactly $($expectedCriticalFiles.Count)."
        )
    }

    $pinnedFileNames = New-OrdinalIgnoreCaseSet
    foreach ($pin in $criticalReferencePins) {
        $pinPropertyNames = @($pin.PSObject.Properties.Name | Sort-Object)
        if ($pinPropertyNames.Count -ne 2 -or
            $pinPropertyNames[0] -cne "fileName" -or
            $pinPropertyNames[1] -cne "sha256") {
            throw "Every critical reference pin must contain exactly fileName and sha256."
        }

        $fileName = [string] $pin.fileName
        $sha256 = [string] $pin.sha256
        if ([string]::IsNullOrWhiteSpace($fileName) -or
            [IO.Path]::IsPathRooted($fileName) -or
            [IO.Path]::GetFileName($fileName) -cne $fileName -or
            -not $pinnedFileNames.Add($fileName)) {
            throw "Critical reference pin has an unsafe, empty, or duplicate fileName: '$fileName'."
        }

        if ($sha256 -cnotmatch '^[0-9A-F]{64}$') {
            throw "Critical reference pin '$fileName' must declare one uppercase SHA-256."
        }
    }

    foreach ($expectedFile in $expectedCriticalFiles) {
        if (-not $pinnedFileNames.Contains($expectedFile)) {
            throw "Package manifest is missing critical reference pin '$expectedFile'."
        }
    }

    foreach ($pin in $criticalReferencePins) {
        $matchingReferences = @(
            $resolvedReferences.Values |
                Where-Object {
                    ([IO.Path]::GetFileName([string] $_.Path)).Equals(
                        [string] $pin.fileName,
                        [StringComparison]::OrdinalIgnoreCase
                    )
                }
        )
        if ($matchingReferences.Count -ne 1) {
            throw (
                "Critical reference '$($pin.fileName)' resolved to " +
                "$($matchingReferences.Count) distinct paths; expected exactly one."
            )
        }

        $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $matchingReferences[0].Path).Hash
        if ($actualHash -cne [string] $pin.sha256) {
            throw (
                "Critical reference '$($pin.fileName)' SHA-256 mismatch: " +
                "expected $($pin.sha256); found $actualHash."
            )
        }
    }
}

$referenceEvidence = @()
if ($evidenceMode) {
    $referenceEvidence = @(
        foreach ($referenceRecord in @($resolvedReferences.Values | Sort-Object Path)) {
            $binary = Get-BinaryEvidence -Path $referenceRecord.Path
            [pscustomobject] [ordered] @{
                Path = $binary.Path
                Projects = @($referenceRecord.Projects | Sort-Object)
                ReferenceNames = @($referenceRecord.ReferenceNames | Sort-Object)
                ResolvedFrom = $referenceRecord.ResolvedFrom
                ExpectedPathMatches = [bool] $referenceRecord.ExpectedPathMatches
                Sha256 = $binary.Sha256
                Size = $binary.Size
                AssemblyName = $binary.AssemblyName
                AssemblyVersion = $binary.AssemblyVersion
                FileVersion = $binary.FileVersion
                FileVersionDisplay = $binary.FileVersionDisplay
                ProductVersion = $binary.ProductVersion
                OriginalFilename = $binary.OriginalFilename
            }
        }
    )
}

Write-Host "Running CI-safe checks and regression suite first."
& (Join-Path $PSScriptRoot "verify-ci.ps1")
if (-not $?) {
    throw "CI-safe verification failed."
}

$buildStartedUtc = [DateTime]::UtcNow.AddSeconds(-2)
Write-Host "Building all four runtime projects plus regression tests with deployment disabled."
Invoke-Checked -FilePath "dotnet" -Arguments @(
    "build",
    $solutionPath,
    "--configuration",
    $Configuration,
    "--no-incremental",
    "-p:SptDir=$msbuildSptDir",
    "-p:SptSharedAssembliesDir=$msbuildSharedAssemblies",
    "-p:SkipTscDeploy=true",
    "-p:ContinuousIntegrationBuild=true"
)

$expectedOutputs = @(
    @{
        ProjectDirectory = "project\SamSWAT.FireSupport"
        Project = "project/SamSWAT.FireSupport/SamSWAT.FireSupport.Core.csproj"
        FileName = "SamSWAT.FireSupport.ArysReloaded.Core.dll"
        TargetRelativePath = "project\SamSWAT.FireSupport\Build\SPT-4.1\netstandard2.1\SamSWAT.FireSupport.ArysReloaded.Core.dll"
        StagedFileName = "Tylevo.TacticalServicesControl.Core.dll"
        ExpectedAssemblyName = "SamSWAT.FireSupport.ArysReloaded.Core"
    },
    @{
        ProjectDirectory = "project\SamSWAT.FireSupport.Server"
        Project = "project/SamSWAT.FireSupport.Server/SamSWAT.FireSupport.Server.csproj"
        FileName = "Tylevo.TacticalServicesControl.Server.dll"
        TargetRelativePath = "project\SamSWAT.FireSupport.Server\Build\SPT-4.1\net10.0\Tylevo.TacticalServicesControl.Server.dll"
        StagedFileName = "Tylevo.TacticalServicesControl.Server.dll"
        ExpectedAssemblyName = "Tylevo.TacticalServicesControl.Server"
    },
    @{
        ProjectDirectory = "project\SamSWAT.FireSupport.Fika.Interop"
        Project = "project/SamSWAT.FireSupport.Fika.Interop/SamSWAT.FireSupport.Fika.Interop.csproj"
        FileName = "Tylevo.TacticalServicesControl.Fika.Interop.dll"
        TargetRelativePath = "project\SamSWAT.FireSupport.Fika.Interop\Build\SPT-4.1\netstandard2.1\Tylevo.TacticalServicesControl.Fika.Interop.dll"
        StagedFileName = "Tylevo.TacticalServicesControl.Fika.Interop.dll"
        ExpectedAssemblyName = "Tylevo.TacticalServicesControl.Fika.Interop"
    },
    @{
        ProjectDirectory = "project\SamSWAT.FireSupport.Fika"
        Project = "project/SamSWAT.FireSupport.Fika/SamSWAT.FireSupport.Fika.csproj"
        FileName = "SamSWAT.FireSupport.ArysReloaded.Fika.dll"
        TargetRelativePath = "project\SamSWAT.FireSupport.Fika\Build\SPT-4.1\netstandard2.1\SamSWAT.FireSupport.ArysReloaded.Fika.dll"
        StagedFileName = "Tylevo.TacticalServicesControl.Fika.dll"
        ExpectedAssemblyName = "SamSWAT.FireSupport.ArysReloaded.Fika"
    }
)

foreach ($expectedOutput in $expectedOutputs) {
    $projectDirectory = Join-Path $repositoryRoot $expectedOutput.ProjectDirectory
    $matches = @(
        Get-ChildItem -LiteralPath $projectDirectory -File -Recurse -Filter $expectedOutput.FileName |
            Where-Object {
                ($_.FullName -match '[\\/](?:bin|Build)[\\/]') -and
                $_.LastWriteTimeUtc -ge $buildStartedUtc
            }
    )

    if ($matches.Count -eq 0) {
        throw "Build did not produce a fresh '$($expectedOutput.FileName)' under '$projectDirectory'."
    }

    Write-Host "Verified output: $($matches[0].FullName)"
}

$outputEvidence = @()
if ($evidenceMode) {
    $outputEvidence = @(
        foreach ($expectedOutput in $expectedOutputs) {
            $targetPath = Join-Path $repositoryRoot $expectedOutput.TargetRelativePath
            if (-not (Test-Path -LiteralPath $targetPath -PathType Leaf)) {
                throw "Release build did not produce exact TargetPath '$targetPath'."
            }

            $targetItem = Get-Item -LiteralPath $targetPath
            if ($targetItem.LastWriteTimeUtc -lt $buildStartedUtc) {
                throw "Release TargetPath was not refreshed by this build: '$targetPath'."
            }

            $binary = Get-BinaryEvidence -Path $targetPath
            if ($binary.AssemblyName -ne $expectedOutput.ExpectedAssemblyName) {
                throw "Release TargetPath '$targetPath' has assembly identity '$($binary.AssemblyName)'; expected '$($expectedOutput.ExpectedAssemblyName)'."
            }

            if ($binary.AssemblyVersion -ne $expectedAssemblyVersion) {
                throw "Release TargetPath '$targetPath' has AssemblyVersion '$($binary.AssemblyVersion)'; expected '$expectedAssemblyVersion'."
            }

            if ($binary.FileVersion -ne $expectedAssemblyVersion) {
                throw "Release TargetPath '$targetPath' has FileVersion '$($binary.FileVersion)'; expected '$expectedAssemblyVersion'."
            }

            if ($binary.ProductVersion -cne $expectedProductVersion) {
                throw "Release TargetPath '$targetPath' has ProductVersion '$($binary.ProductVersion)'; expected '$expectedProductVersion'."
            }

            [pscustomobject] [ordered] @{
                Project = $expectedOutput.Project
                TargetPath = $binary.Path
                StagedFileName = $expectedOutput.StagedFileName
                Sha256 = $binary.Sha256
                Size = $binary.Size
                AssemblyName = $binary.AssemblyName
                AssemblyVersion = $binary.AssemblyVersion
                FileVersion = $binary.FileVersion
                FileVersionDisplay = $binary.FileVersionDisplay
                ProductVersion = $binary.ProductVersion
                OriginalFilename = $binary.OriginalFilename
            }
        }
    )

    foreach ($reference in $referenceEvidence) {
        $currentReference = Get-BinaryEvidence -Path $reference.Path
        if ($currentReference.Size -ne $reference.Size -or
            $currentReference.Sha256 -cne $reference.Sha256) {
            throw "Build reference changed while release evidence was being collected: '$($reference.Path)'."
        }
    }

    $postBuildStatus = Invoke-Captured -FilePath "git" -Arguments @(
        "-c",
        "safe.directory=$repositoryGitPath",
        "-C",
        $repositoryRoot,
        "status",
        "--porcelain=v1",
        "--untracked-files=all"
    )
    if (-not [string]::IsNullOrWhiteSpace($postBuildStatus)) {
        throw "Git worktree changed while release evidence was being collected.`n$postBuildStatus"
    }

    $postBuildHead = Invoke-Captured -FilePath "git" -Arguments @(
        "-c",
        "safe.directory=$repositoryGitPath",
        "-C",
        $repositoryRoot,
        "rev-parse",
        "HEAD"
    )
    $postBuildTree = Invoke-Captured -FilePath "git" -Arguments @(
        "-c",
        "safe.directory=$repositoryGitPath",
        "-C",
        $repositoryRoot,
        "rev-parse",
        "HEAD^{tree}"
    )
    if ($postBuildHead -cne $evidenceHead -or $postBuildTree -cne $evidenceTree) {
        throw "Git HEAD/tree changed while release evidence was being collected."
    }

    $evidence = [ordered] @{
        SchemaVersion = 1
        GeneratedAtUtc = [DateTime]::UtcNow.ToString("o")
        Repository = [ordered] @{
            Head = $evidenceHead
            Tree = $evidenceTree
            WorktreeCleanBeforeAndAfter = $true
        }
        Build = [ordered] @{
            Configuration = $Configuration
            DotnetSdkVersion = $dotnetSdkVersion
            SkipTscDeploy = $true
            ReleaseVersion = $releaseVersion
            ExpectedAssemblyVersion = $expectedAssemblyVersion
            ExpectedProductVersion = $expectedProductVersion
            ReferenceCount = $referenceEvidence.Count
            References = $referenceEvidence
            Outputs = $outputEvidence
        }
    }

    $json = $evidence | ConvertTo-Json -Depth 8
    $stream = $null
    $writer = $null
    try {
        $stream = [IO.File]::Open(
            $resolvedEvidencePath,
            [IO.FileMode]::CreateNew,
            [IO.FileAccess]::Write,
            [IO.FileShare]::None
        )
        $writer = [IO.StreamWriter]::new($stream, [Text.UTF8Encoding]::new($false))
        $writer.WriteLine($json)
    }
    finally {
        if ($null -ne $writer) {
            $writer.Dispose()
        }
        elseif ($null -ne $stream) {
            $stream.Dispose()
        }
    }

    Write-Host "Release build evidence written: $resolvedEvidencePath"
}

Write-Host "Local full verification passed. SkipTscDeploy remained enabled; no live files were deployed."
