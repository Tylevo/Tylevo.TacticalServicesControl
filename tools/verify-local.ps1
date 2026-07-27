[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $SptDir,

    [Parameter(Mandatory)]
    [string] $SptSharedAssembliesDir,

    [string] $Configuration = "SPT-4.0 Release"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path -LiteralPath (Split-Path $PSScriptRoot -Parent)).Path
$solutionPath = Join-Path $repositoryRoot "SamSWAT.FireSupport.ArysReloaded.sln"

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

$resolvedSptDir = Resolve-DirectoryWithTrailingSeparator -Value $SptDir -Description "SPT directory"
$resolvedSharedAssemblies = Resolve-DirectoryWithTrailingSeparator -Value $SptSharedAssembliesDir -Description "SPT shared assemblies directory"
$msbuildSptDir = $resolvedSptDir.Replace("\", "/")
$msbuildSharedAssemblies = $resolvedSharedAssemblies.Replace("\", "/")
$targetSptVersion = switch ($Configuration) {
    "SPT-3.10 Release" { "310x"; break }
    "SPT-3.11 Release" { "311x"; break }
    default { "400x" }
}

$propertyValues = @{
    '$(SptDir)' = $resolvedSptDir
    '$(SptSharedAssembliesDir)' = $resolvedSharedAssemblies
    '$(SptVersion)' = $targetSptVersion
    '$(SptBepInExPluginsDir)' = Join-Path $resolvedSptDir "BepInEx\plugins\"
    '$(SptServerModsDir)' = Join-Path $resolvedSptDir "SPT\user\mods\"
}

$runtimeProjects = @(
    "project\SamSWAT.FireSupport\SamSWAT.FireSupport.Core.csproj",
    "project\SamSWAT.FireSupport.Server\SamSWAT.FireSupport.Server.csproj",
    "project\SamSWAT.FireSupport.Fika.Interop\SamSWAT.FireSupport.Fika.Interop.csproj",
    "project\SamSWAT.FireSupport.Fika\SamSWAT.FireSupport.Fika.csproj"
)

Write-Host "Checking local proprietary/dependency references without copying them."
$missingReferences = [Collections.Generic.List[string]]::new()
foreach ($relativeProject in $runtimeProjects) {
    [xml] $projectXml = Get-Content -LiteralPath (Join-Path $repositoryRoot $relativeProject) -Raw
    foreach ($reference in @($projectXml.SelectNodes("//Reference[HintPath]"))) {
        $expandedPath = [string] $reference.HintPath
        foreach ($property in $propertyValues.GetEnumerator()) {
            $expandedPath = $expandedPath.Replace([string] $property.Key, [string] $property.Value)
        }

        if ($expandedPath.Contains('$(')) {
            throw "Could not expand reference path '$($reference.HintPath)' in '$relativeProject'."
        }

        if (-not (Test-Path -LiteralPath $expandedPath -PathType Leaf)) {
            $missingReferences.Add("$relativeProject -> $expandedPath")
        }
    }
}

if ($missingReferences.Count -gt 0) {
    throw "Missing required local references:`n - $($missingReferences -join "`n - ")"
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
        FileName = "SamSWAT.FireSupport.ArysReloaded.Core.dll"
    },
    @{
        ProjectDirectory = "project\SamSWAT.FireSupport.Server"
        FileName = "Tylevo.TacticalServicesControl.Server.dll"
    },
    @{
        ProjectDirectory = "project\SamSWAT.FireSupport.Fika.Interop"
        FileName = "Tylevo.TacticalServicesControl.Fika.Interop.dll"
    },
    @{
        ProjectDirectory = "project\SamSWAT.FireSupport.Fika"
        FileName = "SamSWAT.FireSupport.ArysReloaded.Fika.dll"
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

Write-Host "Local full verification passed. SkipTscDeploy remained enabled; no live files were deployed."
