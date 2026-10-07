[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$toolsRoot = Split-Path $PSScriptRoot -Parent
$repositoryRoot = Split-Path $toolsRoot -Parent
. (Join-Path $toolsRoot 'PackageContract.ps1')
Add-Type -AssemblyName System.IO.Compression.FileSystem
$contract = Get-TscPilotQuestlinePackageContract
$checker = Join-Path $toolsRoot 'Test-PilotQuestlinePackage.ps1'

function Assert-Rejected([scriptblock] $Action, [string] $Description) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    if (-not $rejected) { throw "Expected rejection: $Description" }
    Write-Host "PASS $Description"
}
function Write-Fixture([string] $Root, [string] $Relative, [string] $Content) {
    $destination = Join-Path $Root $Relative
    [void] [IO.Directory]::CreateDirectory((Split-Path $destination -Parent))
    [IO.File]::WriteAllText($destination, $Content, [Text.UTF8Encoding]::new($false))
}

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ('tsc-addon-contract-' + [Guid]::NewGuid().ToString('N'))
[void] [IO.Directory]::CreateDirectory($temporaryRoot)
try {
    $sourceRoot = Join-Path $repositoryRoot $contract.Source
    & $checker -SourceRoot $repositoryRoot
    $packageRoot = Join-Path $temporaryRoot 'package'
    foreach ($relative in $contract.Files) {
        Write-Fixture $packageRoot ($contract.Destination + '/' + $relative) (Get-Content -LiteralPath (Join-Path $sourceRoot $relative) -Raw -Encoding UTF8)
    }
    & $checker -Path $packageRoot -SourceRoot $repositoryRoot
    $validZip = Join-Path $temporaryRoot 'valid.zip'
    [IO.Compression.ZipFile]::CreateFromDirectory($packageRoot, $validZip)
    & $checker -Path $validZip -SourceRoot $repositoryRoot
    Write-Host 'PASS complete optional addon directory and ZIP contain exactly eight data/document files'
    Assert-Rejected { & (Join-Path $toolsRoot 'Test-PackageLayout.ps1') -Path $validZip -SourceRoot $repositoryRoot } 'addon cannot substitute for the main TSC package'

    $forbidden = @(
        ($contract.Destination + '/Tylevo.TacticalServicesControl.Server.dll'),
        ($contract.Destination + '/config/tsc-config.json'),
        ($contract.Destination + '/storage/authorizations.json'),
        ($contract.Destination + '/assets/device.bundle'),
        ($contract.Destination + '/another-addon.json'),
        'SPT_Runtime/user/mods/Tylevo.TacticalServicesControl/config/tsc-config.json',
        'SPT_Runtime/user/mods/Tylevo.TacticalServicesControl/db/CustomAssortSchemes/jaeger_uav_uplink.json',
        'SPT_Runtime/user/profiles/private-profile.json',
        'BepInEx/plugins/Tylevo.TacticalServicesControl/Tylevo.TacticalServicesControl.Core.dll'
    )
    foreach ($relative in $forbidden) {
        Write-Fixture $packageRoot $relative '{}'
        Assert-Rejected { & $checker -Path $packageRoot -SourceRoot $repositoryRoot } "addon directory rejects $relative"
        $badZip = Join-Path $temporaryRoot 'forbidden.zip'
        [IO.Compression.ZipFile]::CreateFromDirectory($packageRoot, $badZip)
        Assert-Rejected { & $checker -Path $badZip -SourceRoot $repositoryRoot } "addon ZIP rejects $relative"
        [IO.File]::Delete($badZip)
        [IO.File]::Delete((Join-Path $packageRoot $relative))
    }

    $manifestRelative = $contract.Destination + '/addon.json'
    $originalManifest = Get-Content -LiteralPath (Join-Path $packageRoot $manifestRelative) -Raw
    foreach ($field in @('version', 'targetSptVersion', 'schemaVersion', 'schemaVersionType', 'id', 'runtimeConfig')) {
        $manifest = $originalManifest | ConvertFrom-Json
        switch ($field) {
            'version' { $manifest.version = '0.0.0' }
            'targetSptVersion' { $manifest.targetSptVersion = '0.0.0' }
            'schemaVersion' { $manifest.schemaVersion = 2 }
            'schemaVersionType' { $manifest.schemaVersion = '1' }
            'id' { $manifest.id = 'foreign-addon' }
            'runtimeConfig' { $manifest | Add-Member -NotePropertyName runtimeConfig -NotePropertyValue @{} }
        }
        Write-Fixture $packageRoot $manifestRelative ($manifest | ConvertTo-Json)
        Assert-Rejected { & $checker -Path $packageRoot -SourceRoot $repositoryRoot } "addon rejects unsupported or mismatched manifest $field"
    }
    Write-Fixture $packageRoot $manifestRelative $originalManifest

    foreach ($entryName in @($manifestRelative, $manifestRelative.ToUpperInvariant(), ($contract.Destination + '/../addon.json'))) {
        $badZip = Join-Path $temporaryRoot 'unsafe.zip'
        [IO.File]::Copy($validZip, $badZip)
        $archive = [IO.Compression.ZipFile]::Open($badZip, [IO.Compression.ZipArchiveMode]::Update)
        try {
            $entry = $archive.CreateEntry($entryName)
            $writer = [IO.StreamWriter]::new($entry.Open())
            try { $writer.Write('{}') } finally { $writer.Dispose() }
        }
        finally { $archive.Dispose() }
        Assert-Rejected { & $checker -Path $badZip -SourceRoot $repositoryRoot } "addon rejects duplicate, noncanonical or traversal ZIP entry $entryName"
        [IO.File]::Delete($badZip)
    }

    $questRelative = $contract.Destination + '/' + $contract.Files[-1]
    Write-Fixture $packageRoot $questRelative '{bad-json'
    Assert-Rejected { & $checker -Path $packageRoot -SourceRoot $repositoryRoot } 'addon rejects malformed quest JSON'
    [IO.File]::Delete((Join-Path $packageRoot $questRelative))
    Assert-Rejected { & $checker -Path $packageRoot -SourceRoot $repositoryRoot } 'addon rejects a missing required quest file'
    Write-Host 'Pilot questline addon package contract tests passed.'
}
finally {
    $resolvedTemporary = [IO.Path]::GetFullPath($temporaryRoot)
    $temporaryPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedTemporary.StartsWith($temporaryPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolvedTemporary) -notmatch '^tsc-addon-contract-[0-9a-f]{32}$') {
        throw 'Refusing cleanup outside the unique addon test directory.'
    }
    Remove-Item -LiteralPath $resolvedTemporary -Recurse -Force
}
