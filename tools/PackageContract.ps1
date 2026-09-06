# Shared by the layout checker and packager. Dependencies are installed
# separately; a manifest cannot expand the release to ship arbitrary DLLs.
function Assert-TscOnlyPackageContract {
    param([Parameter(Mandatory)][object] $Manifest)

    if ($Manifest.schemaVersion -ne 5) {
        throw "Unsupported package allowlist schema '$($Manifest.schemaVersion)'."
    }
    if ($null -ne $Manifest.PSObject.Properties['bundledDependencies'] -or
        $null -ne $Manifest.exactCounts.PSObject.Properties['bundledDlls']) {
        throw 'TSC-only packages must not declare bundled dependencies or bundled DLL exceptions.'
    }

    $expectedRoots = @(
        'BepInEx/plugins/Tylevo.TacticalServicesControl',
        'SPT_Runtime/user/mods/Tylevo.TacticalServicesControl'
    )
    if (@($Manifest.installRoots).Count -ne $expectedRoots.Count -or
        @($Manifest.installRoots | Where-Object { $expectedRoots -cnotcontains $_ }).Count -ne 0 -or
        @($Manifest.installRoots | Select-Object -Unique).Count -ne $expectedRoots.Count) {
        throw 'Install roots must be exactly the two TSC directories.'
    }
    if ([int] $Manifest.exactCounts.builtDlls -ne 4 -or
        [int] $Manifest.exactCounts.'.dll' -ne 4 -or
        [int] $Manifest.exactCounts.'.bundle' -ne 8) {
        throw 'TSC packages require exactly four built TSC DLLs and eight TSC bundles.'
    }

    $expectedArtifacts = @{
        'BepInEx/plugins/Tylevo.TacticalServicesControl/Tylevo.TacticalServicesControl.Core.dll' = @(
            'project/SamSWAT.FireSupport/Build/SPT-4.1/netstandard2.1/SamSWAT.FireSupport.ArysReloaded.Core.dll',
            'SamSWAT.FireSupport.ArysReloaded.Core'
        )
        'BepInEx/plugins/Tylevo.TacticalServicesControl/Tylevo.TacticalServicesControl.Fika.dll' = @(
            'project/SamSWAT.FireSupport.Fika/Build/SPT-4.1/netstandard2.1/SamSWAT.FireSupport.ArysReloaded.Fika.dll',
            'SamSWAT.FireSupport.ArysReloaded.Fika'
        )
        'BepInEx/plugins/Tylevo.TacticalServicesControl/Tylevo.TacticalServicesControl.Fika.Interop.dll' = @(
            'project/SamSWAT.FireSupport.Fika.Interop/Build/SPT-4.1/netstandard2.1/Tylevo.TacticalServicesControl.Fika.Interop.dll',
            'Tylevo.TacticalServicesControl.Fika.Interop'
        )
        'SPT_Runtime/user/mods/Tylevo.TacticalServicesControl/Tylevo.TacticalServicesControl.Server.dll' = @(
            'project/SamSWAT.FireSupport.Server/Build/SPT-4.1/net10.0/Tylevo.TacticalServicesControl.Server.dll',
            'Tylevo.TacticalServicesControl.Server'
        )
    }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($artifact in $Manifest.buildArtifacts) {
        $destination = [string] $artifact.destination
        if ($expectedArtifacts.Keys -cnotcontains $destination -or -not $seen.Add($destination)) {
            throw "Unexpected or duplicate TSC DLL destination: '$destination'."
        }
        $expected = $expectedArtifacts[$destination]
        if ($artifact.source -cne $expected[0] -or $artifact.assemblyName -cne $expected[1]) {
            throw "TSC DLL '$destination' must use its fixed project output and assembly identity."
        }
    }
    if ($seen.Count -ne 4) { throw 'The package must map all four built TSC DLLs exactly once.' }
}
