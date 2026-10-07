[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $OutputDirectory,
    [string] $DotNet = 'dotnet'
)
$ErrorActionPreference = 'Stop'
$output = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($output) | Out-Null
$companionRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$project = Join-Path $companionRoot 'tests/selection/SelectionTests.csproj'
$artifacts = Join-Path $output 'artifacts'
$passed = 0

function Invoke-Case([string] $Name, [string] $Command, [string[]] $Arguments, [string] $ExpectedFailure = '') {
    $log = Join-Path $output ($Name + '.log')
    # Native stderr is captured as evidence, including deliberately rejected builds.
    $oldPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $lines = @(& $Command @Arguments 2>&1); $exitCode = $LASTEXITCODE }
    finally { $ErrorActionPreference = $oldPreference }
    $lines | Set-Content -LiteralPath $log -Encoding UTF8
    $text = $lines -join "`n"
    if ($ExpectedFailure) {
        if ($exitCode -eq 0 -or -not $text.Contains($ExpectedFailure)) { throw "Expected rejection did not occur: $Name. See $log" }
    } elseif ($exitCode -ne 0) { throw "Identity regression failed: $Name. See $log" }
    $script:passed++
    Write-Output "PASS $Name"
}
function Test-Identity([string] $Name, [string] $Supplied, [string] $Expected) {
    Invoke-Case $Name $DotNet @('run', '--project', $project, '-c', 'Release',
        "-p:Hh60ValidatedCoreSha256=$Supplied", '-p:UseArtifactsOutput=true', "-p:ArtifactsPath=$artifacts",
        '--', '--expect-core-sha', $Expected)
}

# Deliberately synthetic identities: these outputs are test executables, never
# companion DLLs eligible for installation. Reuse the same outputs without clean.
$first = 'abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789'
$second = '9876543210abcdef9876543210abcdef9876543210abcdef9876543210abcdef'
$historical = '3B5CF9B9F8647E5C3DD9D701C1C6B1C19054B81FB4B0C12F22441AC25FA57F63'
Test-Identity 'generated-identity-a' $first $first.ToUpperInvariant()
Test-Identity 'generated-identity-b' $second $second.ToUpperInvariant()
Test-Identity 'historical-fallback-after-override' '' $historical
Invoke-Case 'malformed-override-rejected' $DotNet @('build', $project, '-c', 'Release',
    '-p:Hh60ValidatedCoreSha256=not-a-hash', '-p:UseArtifactsOutput=true', "-p:ArtifactsPath=$artifacts") 'Hh60ValidatedCoreSha256 must be an explicit audited 64-digit SHA-256 hash.'
Test-Identity 'historical-fallback-after-rejection' '' $historical

$fakeCore = Join-Path $output 'unsupported-core-fixture.dll'
'Synthetic mismatch fixture. This is not a native assembly.' | Set-Content -LiteralPath $fakeCore -Encoding UTF8
$shell = (Get-Process -Id $PID).Path
Invoke-Case 'mismatch-rejected-before-audit-or-build' $shell @('-NoProfile', '-File', (Join-Path $companionRoot 'build.ps1'),
    '-GameRoot', $output, '-CoreDll', $fakeCore, '-ValidatedCoreSha256', ('0' * 64),
    '-DotNet', 'HH60_TEST_COMPILER_MUST_NOT_RUN') 'Selected Core does not match the explicitly supplied validated hash.'
Write-Output "PASS $passed Core identity build/guard scenarios; no game references or native assets were used. Logs: $output"

# Expected negative child processes must not leak failure into pwsh -File/CI.
# Every nonzero result has already been matched to its required rejection above.
$global:LASTEXITCODE = 0
