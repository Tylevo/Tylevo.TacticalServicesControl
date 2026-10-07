[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path -LiteralPath (Split-Path $PSScriptRoot -Parent)).Path
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) ('tsc-build-contract-' + [Guid]::NewGuid().ToString('N'))

function Read-EvaluatedProperties {
    param([string[]] $AdditionalArguments = @())
    $propertyNames = 'SkipTscDeploy,SptDir,SptSharedAssembliesDir,SptBepInExPluginsDir,SptServerRuntimeDir,SptServerModsDir,SptVersion,TscSptRoot,TscSharedAssembliesRoot'
    $output = @(& dotnet msbuild (Join-Path $fixtureRoot 'fixture.proj') -nologo "-getProperty:$propertyNames" @AdditionalArguments)
    if ($LASTEXITCODE -ne 0) { throw 'Build-property evaluation failed.' }
    return (($output -join "`n") | ConvertFrom-Json).Properties
}

try {
    New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'Directory.Build.props') -Destination $fixtureRoot
    Set-Content -LiteralPath (Join-Path $fixtureRoot 'fixture.proj') -Encoding UTF8 -Value '<Project><Import Project="Directory.Build.props" /></Project>'
    $defaults = Read-EvaluatedProperties
    if ($defaults.SkipTscDeploy -cne 'true') { throw 'Ordinary builds must default to SkipTscDeploy=true.' }
    if ($defaults.SptDir -or $defaults.SptSharedAssembliesDir -or $defaults.SptBepInExPluginsDir -or $defaults.SptServerRuntimeDir -or $defaults.SptServerModsDir) {
        throw 'A checkout without local configuration must not infer reference or installation paths.'
    }
    $explicitDeploy = Read-EvaluatedProperties -AdditionalArguments @('-p:SkipTscDeploy=false')
    if ($explicitDeploy.SkipTscDeploy -cne 'false') { throw 'Explicit deployment opt-in must remain available.' }

    # These are inert fixture paths. Evaluation never builds or deploys anything.
    $referenceRoot = Join-Path $fixtureRoot 'reference-root'
    $assemblyRoot = Join-Path $fixtureRoot 'assembly-root'
    $escapedReference = [Security.SecurityElement]::Escape($referenceRoot)
    $escapedAssemblies = [Security.SecurityElement]::Escape($assemblyRoot)
    $localProps = '<Project><PropertyGroup><SptDir>' + $escapedReference + '</SptDir><SptSharedAssembliesDir>' + $escapedAssemblies + '</SptSharedAssembliesDir><SptVersion>410x</SptVersion></PropertyGroup></Project>'
    Set-Content -LiteralPath (Join-Path $fixtureRoot 'Shared.User.props') -Encoding UTF8 -Value $localProps
    $local = Read-EvaluatedProperties
    $expected = @{
        SptDir = $referenceRoot + [IO.Path]::DirectorySeparatorChar
        SptSharedAssembliesDir = $assemblyRoot + [IO.Path]::DirectorySeparatorChar
        SptBepInExPluginsDir = Join-Path $referenceRoot 'BepInEx\plugins\'
        SptServerRuntimeDir = Join-Path $referenceRoot 'SPT_Runtime\'
        SptServerModsDir = Join-Path $referenceRoot 'SPT_Runtime\user\mods\'
    }
    foreach ($name in $expected.Keys) {
        if ($local.$name.Replace('/', '\').TrimEnd('\') -cne $expected[$name].Replace('/', '\').TrimEnd('\')) {
            throw "Shared.User.props must be imported before deriving ${name}: got '$($local.$name)'."
        }
    }
    if ($local.SptVersion -cne '410x' -or $local.SkipTscDeploy -cne 'true') {
        throw 'Local references must retain the selected version and safe deployment default.'
    }
    $overrideRoot = Join-Path $fixtureRoot 'command-line-root'
    $overrideAssemblies = Join-Path $fixtureRoot 'command-line-assemblies'
    $commandLine = Read-EvaluatedProperties -AdditionalArguments @("-p:SptDir=$overrideRoot", "-p:SptSharedAssembliesDir=$overrideAssemblies", '-p:SkipTscDeploy=false')
    if ($commandLine.SptBepInExPluginsDir.Replace('/', '\').TrimEnd('\') -cne (Join-Path $overrideRoot 'BepInEx\plugins') -or
        $commandLine.TscSharedAssembliesRoot.Replace('/', '\') -cne ($overrideAssemblies + '\') -or
        $commandLine.SkipTscDeploy -cne 'false') {
        throw 'Explicit MSBuild properties must override local configuration before dependent paths are evaluated.'
    }
    Write-Host 'Build configuration contract passed: no inferred roots, early local import, deployment off by default, explicit overrides honored.'
}
finally {
    # Only delete the freshly created, exactly named test fixture below the temp root.
    $resolvedFixture = [IO.Path]::GetFullPath($fixtureRoot)
    $expectedPrefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar + 'tsc-build-contract-'
    if (-not $resolvedFixture.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Refusing cleanup outside the build-contract fixture.' }
    if (Test-Path -LiteralPath $resolvedFixture) { Remove-Item -LiteralPath $resolvedFixture -Recurse -Force }
}
