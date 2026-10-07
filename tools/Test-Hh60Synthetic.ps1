[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path -LiteralPath (Split-Path $PSScriptRoot -Parent)).Path
$testRoot = Join-Path $repositoryRoot 'extras\hh60-visual\tests'
# Every project must have a reviewed classification. New suites cannot silently miss CI.
$syntheticProjects = @(
    'escort-burst/EscortBurstTests.csproj',
    'escort-held-trigger/HeldTriggerTests.csproj',
    'escort-input/EscortInputTests.csproj',
    'escort-mode/ModePolicyTests.csproj',
    'escort-motion/EscortMotionTests.csproj',
    'escort-targeting/EscortTargetingTests.csproj',
    'extraction-cover/ExtractionCoverTests.csproj',
    'flight/FlightMathTests.csproj',
    'glass/GlassPolicyTests.csproj',
    'muzzle-flash/MuzzleFlashTests.csproj',
    'payload/PayloadTests.csproj',
    'selection/SelectionTests.csproj'
)
$localOnlyProjects = @('extraction-gun-visuals/ExtractionGunVisualTests.csproj')
$actual = @(Get-ChildItem -LiteralPath $testRoot -Filter '*.csproj' -Recurse -File | ForEach-Object {
    $_.FullName.Substring($testRoot.Length + 1).Replace('\', '/')
})
$expected = @($syntheticProjects) + @($localOnlyProjects)
if (@($expected | Select-Object -Unique).Count -ne $expected.Count -or
    @(Compare-Object -ReferenceObject $expected -DifferenceObject $actual).Count -ne 0) {
    throw 'HH-60 test inventory changed. Classify every project as synthetic or requiring a local native payload before updating CI.'
}
foreach ($relative in $syntheticProjects) {
    Write-Host "Running synthetic HH-60 suite: $relative"
    $projectPath = Join-Path $testRoot $relative
    # Evaluate conditions, so optional native modes remain opt-in while the CI
    # configuration cannot quietly acquire local DLL or runtime-project inputs.
    $referenceOutput = @(& dotnet msbuild $projectPath -nologo '-getItem:Reference,ProjectReference' '-p:Configuration=Release')
    if ($LASTEXITCODE -ne 0) { throw "Could not evaluate synthetic references: $relative" }
    $evaluated = ($referenceOutput -join "`n") | ConvertFrom-Json
    if (@($evaluated.Items.Reference).Count -ne 0 -or @($evaluated.Items.ProjectReference).Count -ne 0) {
        throw "Synthetic HH-60 suite must not reference local DLLs or runtime projects: $relative"
    }
    & dotnet run --project $projectPath --configuration Release
    if ($LASTEXITCODE -ne 0) { throw "HH-60 synthetic suite failed: $relative" }
}
Write-Host 'Checking Core identity generation across incremental builds without native references.'
$identityOutput = Join-Path ([IO.Path]::GetTempPath()) ('tsc-hh60-core-identity-' + [Guid]::NewGuid().ToString('N'))
& (Join-Path $testRoot 'core-identity\Test-CoreIdentity.ps1') -OutputDirectory $identityOutput
if (-not $?) { throw 'HH-60 Core identity build/guard scenarios failed.' }
Write-Host "All $($syntheticProjects.Count) synthetic HH-60 suites passed. Extraction gun visuals require an explicit local native scene.json and are checked separately."
