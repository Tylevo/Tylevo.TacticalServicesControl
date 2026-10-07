[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$toolsRoot = Split-Path $PSScriptRoot -Parent
$repositoryRoot = Split-Path $toolsRoot -Parent
. (Join-Path $toolsRoot 'PackageContract.ps1')
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Assert-Rejected([scriptblock] $Action, [string] $Description) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw "Expected rejection: $Description" }
    Write-Host "PASS $Description"
}
function Write-Fixture([string] $Root, [string] $Relative, [byte[]] $Bytes) {
    $destination = Join-Path $Root $Relative
    [void] [IO.Directory]::CreateDirectory((Split-Path $destination -Parent))
    [IO.File]::WriteAllBytes($destination, $Bytes)
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ('tsc-package-contract-' + [Guid]::NewGuid().ToString('N'))
[void] [IO.Directory]::CreateDirectory($temporaryRoot)
try {
    $manifestText = Get-Content -LiteralPath (Join-Path $toolsRoot 'package-layout.allowlist.json') -Raw
    $manifest = $manifestText | ConvertFrom-Json
    Assert-TscOnlyPackageContract -Manifest $manifest
    Write-Host 'PASS reviewed manifest declares only four TSC DLLs and eight bundles'

    # Fixture payloads test inventory/security rules without checking in,
    # loading, or downloading game and dependency assemblies.
    $fixtureBytes = [Text.Encoding]::UTF8.GetBytes('synthetic-package-fixture')
    $packageRoot = Join-Path $temporaryRoot 'package'
    foreach ($mirror in $manifest.mirrors) {
        foreach ($relative in $mirror.files) { Write-Fixture $packageRoot ($mirror.destination + '/' + $relative) $fixtureBytes }
    }
    foreach ($artifact in $manifest.buildArtifacts) { Write-Fixture $packageRoot $artifact.destination $fixtureBytes }
    foreach ($file in $manifest.copiedFiles) { Write-Fixture $packageRoot $file.destination $fixtureBytes }
    foreach ($file in $manifest.baselineAssetArchive.files) { Write-Fixture $packageRoot $file.path $fixtureBytes }
    $fixtureManifestPath = Join-Path $temporaryRoot 'manifest.json'
    [IO.File]::WriteAllText($fixtureManifestPath, $manifestText, [Text.UTF8Encoding]::new($false))
    $checker = Join-Path $toolsRoot 'Test-PackageLayout.ps1'
    & $checker -Path $packageRoot -ManifestPath $fixtureManifestPath -SourceRoot $repositoryRoot
    $zipPath = Join-Path $temporaryRoot 'valid.zip'
    [IO.Compression.ZipFile]::CreateFromDirectory($packageRoot, $zipPath)
    & $checker -Path $zipPath -ManifestPath $fixtureManifestPath -SourceRoot $repositoryRoot
    Write-Host 'PASS complete TSC-only directory and ZIP'

    $forbiddenPaths = @(
        'BepInEx/plugins/UnityToolkit/UnityToolkit.dll',
        'BepInEx/plugins/UnityToolkit/Assemblies.jsonc',
        'BepInEx/patchers/UnityToolkit/UnityToolkit-Prepatcher.dll',
        'BepInEx/plugins/Tylevo.TacticalServicesControl/UnityToolkit.dll',
        'BepInEx/plugins/Tylevo.TacticalServicesControl/UnityToolkit-Prepatcher.dll',
        'BepInEx/plugins/Tylevo.TacticalServicesControl/Unity.Collections.dll',
        'BepInEx/plugins/Tylevo.TacticalServicesControl/VContainer.dll',
        'BepInEx/plugins/Tylevo.TacticalServicesControl/System.Runtime.CompilerServices.Unsafe.dll',
        'BepInEx/plugins/Tylevo.TacticalServicesControl/Assembly-CSharp.dll',
        'BepInEx/plugins/Tylevo.TacticalServicesControl/WTT-ClientCommonLib.dll',
        'BepInEx/plugins/Tylevo.TacticalServicesControl/Fika.Core.dll',
        'BepInEx/plugins/Tylevo.TacticalServicesControl/unknown-third-party.dll',
        'SPT_Runtime/user/mods/Tylevo.TacticalServicesControl/SPTarkov.Server.Core.dll',
        'SPT_Runtime/user/mods/Tylevo.TacticalServicesControl/UnityToolkit.dll',
        'SPT_Runtime/user/mods/Tylevo.TacticalServicesControl/unknown-third-party.dll',
        'SPT_Runtime/user/mods/Tylevo.TacticalServicesControl/config/tsc-config.json',
        'SPT_Runtime/user/mods/Tylevo.TacticalServicesControl/storage/authorizations.json',
        'SPT_Runtime/user/mods/Tylevo.TacticalServicesControl/addons/pilot-questline/addon.json',
        'SPT_Runtime/user/mods/Tylevo.TacticalServicesControl/db/CustomQuests/5a7c2eca46aef81a7ca2145d/Quests/open_channel.json',
        'SPT_Runtime/user/mods/Tylevo.TacticalServicesControl/db/CustomAssortSchemes/pilot_repeater.json',
        'SPT_Runtime/user/profiles/private-profile.json'
    )
    foreach ($relative in $forbiddenPaths) {
        Write-Fixture $packageRoot $relative $fixtureBytes
        Assert-Rejected { & $checker -Path $packageRoot -ManifestPath $fixtureManifestPath -SourceRoot $repositoryRoot } "directory rejects $relative"
        $badZipPath = Join-Path $temporaryRoot 'forbidden.zip'
        [IO.Compression.ZipFile]::CreateFromDirectory($packageRoot, $badZipPath)
        Assert-Rejected { & $checker -Path $badZipPath -ManifestPath $fixtureManifestPath -SourceRoot $repositoryRoot } "ZIP rejects $relative"
        [IO.File]::Delete($badZipPath)
        [IO.File]::Delete((Join-Path $packageRoot $relative))
    }

    $probe = Join-Path $packageRoot $manifest.buildArtifacts[0].destination
    [IO.File]::Delete($probe)
    Assert-Rejected { & $checker -Path $packageRoot -ManifestPath $fixtureManifestPath -SourceRoot $repositoryRoot } 'missing required TSC DLL'
    [IO.File]::WriteAllBytes($probe, $fixtureBytes)

    $changed = $manifestText | ConvertFrom-Json
    $changed | Add-Member -NotePropertyName bundledDependencies -NotePropertyValue @()
    Assert-Rejected { Assert-TscOnlyPackageContract $changed } 'legacy dependency exception even when empty'
    $changed = $manifestText | ConvertFrom-Json
    $changed.exactCounts | Add-Member -NotePropertyName bundledDlls -NotePropertyValue 0
    Assert-Rejected { Assert-TscOnlyPackageContract $changed } 'legacy bundled DLL count even when zero'
    $changed = $manifestText | ConvertFrom-Json
    $changed.schemaVersion = 4
    Assert-Rejected { Assert-TscOnlyPackageContract $changed } 'old bundling manifest schema'
    $changed = $manifestText | ConvertFrom-Json
    $changed.installRoots += 'BepInEx/plugins/UnityToolkit'
    Assert-Rejected { Assert-TscOnlyPackageContract $changed } 'extra dependency install root'
    $changed = $manifestText | ConvertFrom-Json
    $changed.exactCounts.'.dll' = 18
    Assert-Rejected { Assert-TscOnlyPackageContract $changed } 'expanded DLL count'
    $changed = $manifestText | ConvertFrom-Json
    $changed.buildArtifacts[0].destination = 'BepInEx/plugins/Tylevo.TacticalServicesControl/UnityToolkit.dll'
    Assert-Rejected { Assert-TscOnlyPackageContract $changed } 'foreign DLL substituted into build mappings'
    $changed = $manifestText | ConvertFrom-Json
    $changed.buildArtifacts[0].source = 'external/UnityToolkit.dll'
    Assert-Rejected { Assert-TscOnlyPackageContract $changed } 'foreign output renamed as a TSC DLL'
    $changed = $manifestText | ConvertFrom-Json
    $changed.buildArtifacts[0].assemblyName = 'UnityToolkit'
    Assert-Rejected { Assert-TscOnlyPackageContract $changed } 'foreign assembly identity under a TSC filename'

    foreach ($relative in @('addons/pilot-questline/addon.json', 'db/CustomQuests/intro.json', 'db/CustomAssortSchemes/pilot_repeater.json')) {
        $changed = $manifestText | ConvertFrom-Json
        $changed.mirrors[1].files += $relative
        Assert-Rejected { Assert-TscOnlyPackageContract $changed } "base allowlist cannot include optional progression via mirrors: $relative"
        $changed = $manifestText | ConvertFrom-Json
        $changed.copiedFiles += [pscustomobject] @{ source = ('addons/pilot-questline/' + $relative); destination = ($manifest.installRoots[1] + '/' + $relative) }
        Assert-Rejected { Assert-TscOnlyPackageContract $changed } "base allowlist cannot include optional progression via copied files: $relative"
    }

    # Even an edited allowlist cannot authorize another DLL via mirrors or
    # copied files inside a TSC directory.
    foreach ($addition in @('copiedFiles', 'mirrors')) {
        $changed = $manifestText | ConvertFrom-Json
        if ($addition -eq 'copiedFiles') {
            $changed.copiedFiles += [pscustomobject]@{
                source='external/custom.dll'
                destination='BepInEx/plugins/Tylevo.TacticalServicesControl/custom.dll'
            }
        } else {
            $changed.mirrors[0].files += 'custom.dll'
        }
        [IO.File]::WriteAllText($fixtureManifestPath, ($changed | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
        Assert-Rejected { & $checker -Path $packageRoot -ManifestPath $fixtureManifestPath -SourceRoot $repositoryRoot } "allowlist cannot authorize third-party DLLs via $addition"
    }
    Write-Host 'TSC-only package contract tests passed.'
} finally {
    $resolvedTemporary = [IO.Path]::GetFullPath($temporaryRoot)
    $temporaryPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedTemporary.StartsWith($temporaryPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolvedTemporary) -notmatch '^tsc-package-contract-[0-9a-f]{32}$') {
        throw 'Refusing cleanup outside the unique test directory.'
    }
    Remove-Item -LiteralPath $resolvedTemporary -Recurse -Force
}
