[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$toolsRoot = Split-Path $PSScriptRoot -Parent
$repositoryRoot = Split-Path $toolsRoot -Parent
. (Join-Path $toolsRoot 'BundledDependencies.ps1')
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Assert-Rejected([scriptblock] $Action, [string] $Description) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw "Expected rejection: $Description" }
    Write-Host "PASS $Description"
}
function Get-TestHash([byte[]] $Bytes) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($algorithm.ComputeHash($Bytes))).Replace('-', '') }
    finally { $algorithm.Dispose() }
}
function Write-Fixture([string] $Root, [string] $Relative, [byte[]] $Bytes) {
    $destination = Join-Path $Root $Relative
    [void] [IO.Directory]::CreateDirectory((Split-Path $destination -Parent))
    [IO.File]::WriteAllBytes($destination, $Bytes)
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ('tsc-toolkit-contract-' + [Guid]::NewGuid().ToString('N'))
[void] [IO.Directory]::CreateDirectory($temporaryRoot)
try {
    $manifest = Get-Content -LiteralPath (Join-Path $toolsRoot 'package-layout.allowlist.json') -Raw | ConvertFrom-Json
    $realContract = Get-BundledDependencyContract -Manifest $manifest
    if ($realContract.Count -ne 15) { throw 'Reviewed contract must contain fifteen Toolkit files.' }
    Write-Host 'PASS reviewed manifest has the exact dependency contract'

    # Synthetic byte payloads exercise the real hashing/inventory/checker code
    # without checking in, loading, or downloading any dependency assemblies.
    $fixtureBytes = [Text.Encoding]::UTF8.GetBytes('synthetic-library-fixture')
    $fixtureHash = Get-TestHash $fixtureBytes
    foreach ($file in $manifest.bundledDependencies[0].files) {
        $file.length = $fixtureBytes.Length
        $file.sha256 = $fixtureHash
        if ($file.origin -eq 'upstream-release') {
            $file.upstreamLength = $fixtureBytes.Length
            $file.upstreamSha256 = $fixtureHash
        }
    }
    ($manifest.criticalReferencePins | Where-Object fileName -EQ 'UnityToolkit.dll').sha256 = $fixtureHash
    $contract = Get-BundledDependencyContract -Manifest $manifest
    $inputRoot = Join-Path $temporaryRoot 'dependency'
    foreach ($relative in $contract.Keys) { Write-Fixture $inputRoot $relative $fixtureBytes }
    if (@(Get-VerifiedBundledDependencyFiles -Root $inputRoot -Contract $contract).Count -ne 15) { throw 'Valid synthetic dependency inventory failed.' }
    Write-Host 'PASS external exact inventory and bytes'

    $probeRelative = 'BepInEx/plugins/UnityToolkit/UnityToolkit.dll'
    $probe = Join-Path $inputRoot $probeRelative
    $badBytes = [byte[]] $fixtureBytes.Clone()
    $badBytes[0] = 0
    [IO.File]::WriteAllBytes($probe, $badBytes)
    Assert-Rejected { Get-VerifiedBundledDependencyFiles -Root $inputRoot -Contract $contract } 'modified dependency bytes'
    [IO.File]::WriteAllBytes($probe, $fixtureBytes)
    [IO.File]::Delete($probe)
    Assert-Rejected { Get-VerifiedBundledDependencyFiles -Root $inputRoot -Contract $contract } 'missing dependency file'
    [IO.File]::WriteAllBytes($probe, $fixtureBytes)
    $extra = Join-Path $inputRoot 'BepInEx/plugins/UnityToolkit/WTT-ClientCommonLib.dll'
    [IO.File]::WriteAllBytes($extra, $fixtureBytes)
    Assert-Rejected { Get-VerifiedBundledDependencyFiles -Root $inputRoot -Contract $contract } 'extra proprietary dependency file'
    [IO.File]::Delete($extra)
    $extraDirectory = Join-Path $inputRoot 'SPT_Runtime'
    [void] [IO.Directory]::CreateDirectory($extraDirectory)
    Assert-Rejected { Get-VerifiedBundledDependencyFiles -Root $inputRoot -Contract $contract } 'extra empty dependency directory'
    [IO.Directory]::Delete($extraDirectory)

    $originalPath = $manifest.bundledDependencies[0].files[0].path
    $manifest.bundledDependencies[0].files[0].path = 'BepInEx/plugins/UnityToolkit/../../Assembly-CSharp.dll'
    Assert-Rejected { Get-BundledDependencyContract -Manifest $manifest } 'manifest traversal or arbitrary dependency exception'
    $manifest.bundledDependencies[0].files[0].path = $originalPath
    $manifest.bundledDependencies[0].files[0].path = $manifest.bundledDependencies[0].files[1].path
    Assert-Rejected { Get-BundledDependencyContract -Manifest $manifest } 'duplicate manifest dependency path'
    $manifest.bundledDependencies[0].files[0].path = $originalPath

    $packageRoot = Join-Path $temporaryRoot 'package'
    foreach ($mirror in $manifest.mirrors) {
        foreach ($relative in $mirror.files) { Write-Fixture $packageRoot ($mirror.destination + '/' + $relative) $fixtureBytes }
    }
    foreach ($artifact in $manifest.buildArtifacts) { Write-Fixture $packageRoot $artifact.destination $fixtureBytes }
    foreach ($file in $manifest.copiedFiles) { Write-Fixture $packageRoot $file.destination $fixtureBytes }
    foreach ($file in $manifest.baselineAssetArchive.files) { Write-Fixture $packageRoot $file.path $fixtureBytes }
    foreach ($relative in $contract.Keys) { Write-Fixture $packageRoot $relative $fixtureBytes }
    $fixtureManifestPath = Join-Path $temporaryRoot 'manifest.json'
    $manifest | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $fixtureManifestPath -Encoding UTF8
    $checker = Join-Path $toolsRoot 'Test-PackageLayout.ps1'
    & $checker -Path $packageRoot -ManifestPath $fixtureManifestPath -SourceRoot $repositoryRoot
    $zipPath = Join-Path $temporaryRoot 'valid.zip'
    [IO.Compression.ZipFile]::CreateFromDirectory($packageRoot, $zipPath)
    & $checker -Path $zipPath -ManifestPath $fixtureManifestPath -SourceRoot $repositoryRoot
    Write-Host 'PASS complete directory and ZIP accept exact synthetic package'

    [IO.File]::WriteAllBytes((Join-Path $packageRoot $probeRelative), $badBytes)
    Assert-Rejected { & $checker -Path $packageRoot -ManifestPath $fixtureManifestPath -SourceRoot $repositoryRoot } 'staged dependency tampering'
    $tamperedZip = Join-Path $temporaryRoot 'tampered.zip'
    [IO.Compression.ZipFile]::CreateFromDirectory($packageRoot, $tamperedZip)
    Assert-Rejected { & $checker -Path $tamperedZip -ManifestPath $fixtureManifestPath -SourceRoot $repositoryRoot } 'ZIP dependency tampering'
    [IO.File]::WriteAllBytes((Join-Path $packageRoot $probeRelative), $fixtureBytes)
    foreach ($forbiddenName in @('Assembly-CSharp.dll', 'SPTarkov.Server.Core.dll', 'WTT-ClientCommonLib.dll', 'Fika.Core.dll', 'UnityToolkit.dll')) {
        $foreign = Join-Path $packageRoot ('BepInEx/plugins/Tylevo.TacticalServicesControl/' + $forbiddenName)
        [IO.File]::WriteAllBytes($foreign, $fixtureBytes)
        Assert-Rejected { & $checker -Path $packageRoot -ManifestPath $fixtureManifestPath -SourceRoot $repositoryRoot } "foreign or duplicate dependency remains forbidden: $forbiddenName"
        [IO.File]::Delete($foreign)
    }
    Write-Host 'Bundled dependency contract tests passed.'
} finally {
    $resolvedTemporary = [IO.Path]::GetFullPath($temporaryRoot)
    $temporaryPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedTemporary.StartsWith($temporaryPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolvedTemporary) -notmatch '^tsc-toolkit-contract-[0-9a-f]{32}$') {
        throw 'Refusing cleanup outside the unique test directory.'
    }
    Remove-Item -LiteralPath $resolvedTemporary -Recurse -Force
}
