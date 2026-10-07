[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $GameRoot,
    [Parameter(Mandatory = $true)][string] $CoreDll,
    [Parameter(Mandatory = $true)][ValidatePattern('^[A-Fa-f0-9]{64}$')][string] $ValidatedCoreSha256,
    [string] $ValidationReport,
    [string] $DotNet = 'dotnet'
)
$ErrorActionPreference = 'Stop'
$game = (Resolve-Path -LiteralPath $GameRoot).Path
$core = (Resolve-Path -LiteralPath $CoreDll).Path
$expected = $ValidatedCoreSha256.ToUpperInvariant()
if ((Get-FileHash -LiteralPath $core -Algorithm SHA256).Hash -ne $expected) {
    throw 'Selected Core does not match the explicitly supplied validated hash. No companion was built.'
}
$verify = Join-Path $PSScriptRoot 'tools/verify-native-contract.ps1'
$audit = (& $verify -AuditCore -CoreDll $core -GameRoot $game) | ConvertFrom-Json
if ($audit.mode -ne 'CoreAudit' -or $audit.coreSha256 -ne $expected) {
    throw 'Native Core audit did not validate the explicitly supplied identity. No companion was built.'
}
Write-Output "Native Core audit passed: $($audit.passed) metadata checks. Building for $expected."
$project = Join-Path $PSScriptRoot 'src/TscHh60Visual.csproj'
$config = Join-Path $PSScriptRoot 'src/NuGet.Config'
& $DotNet build $project -c Release "-p:GameRoot=$game" "-p:Hh60ValidatedCoreSha256=$expected" "-p:RestoreConfigFile=$config" -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$companion = Join-Path $PSScriptRoot 'src/bin/Release/netstandard2.1/TscHh60Visual.dll'
if ($ValidationReport) {
    & $verify -CompanionDll $companion -CoreDll $core -GameRoot $game -Report $ValidationReport
} else {
    $result = (& $verify -CompanionDll $companion -CoreDll $core -GameRoot $game) | ConvertFrom-Json
    Write-Output "Companion and exact Core identity verified: $($result.passed) checks."
}
Write-Output 'Build only; no deployment or game changes.'
exit 0
