[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$GameRoot,
    [string]$DotNet = 'dotnet'
)
$ErrorActionPreference = 'Stop'
$game = (Resolve-Path -LiteralPath $GameRoot).Path
$project = Join-Path $PSScriptRoot 'src/TscHh60Visual.csproj'
$config = Join-Path $PSScriptRoot 'src/NuGet.Config'
& $DotNet build $project -c Release "-p:GameRoot=$game" "-p:RestoreConfigFile=$config" -p:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Output 'Build only: no game files, configuration, or profiles have been changed.'
exit 0
