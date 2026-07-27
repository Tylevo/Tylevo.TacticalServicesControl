[CmdletBinding()]
param(
    [string] $BaseSha,
    [string] $HeadSha
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path -LiteralPath (Split-Path $PSScriptRoot -Parent)).Path
$repositoryGitPath = $repositoryRoot.Replace("\", "/")
$solutionPath = Join-Path $repositoryRoot "SamSWAT.FireSupport.ArysReloaded.sln"
$regressionProject = Join-Path $repositoryRoot "project\Tylevo.TacticalServicesControl.RegressionTests\Tylevo.TacticalServicesControl.RegressionTests.csproj"

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

function Invoke-Git {
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments
    )

    Invoke-Checked -FilePath "git" -Arguments (@("-c", "safe.directory=$repositoryGitPath", "-C", $repositoryRoot) + $Arguments)
}

function Assert-SolutionMapping {
    param(
        [Parameter(Mandatory)]
        [string] $SolutionText,

        [Parameter(Mandatory)]
        [string] $ProjectGuid,

        [Parameter(Mandatory)]
        [string] $SolutionConfiguration,

        [Parameter(Mandatory)]
        [string] $ProjectConfiguration
    )

    foreach ($platform in @("Any CPU", "x64", "x86")) {
        $active = "{$ProjectGuid}.$SolutionConfiguration|$platform.ActiveCfg = $ProjectConfiguration|Any CPU"
        $build = "{$ProjectGuid}.$SolutionConfiguration|$platform.Build.0 = $ProjectConfiguration|Any CPU"
        if ($SolutionText.IndexOf($active, [StringComparison]::Ordinal) -lt 0) {
            throw "Solution is missing mapping: $active"
        }

        if ($SolutionText.IndexOf($build, [StringComparison]::Ordinal) -lt 0) {
            throw "Solution is missing mapping: $build"
        }
    }
}

function Get-NormalizedCSharpSource {
    param(
        [Parameter(Mandatory)]
        [string] $RelativePath
    )

    $sourcePath = Join-Path $repositoryRoot $RelativePath
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "Runtime adapter source was not found: '$RelativePath'."
    }

    $source = Get-Content -LiteralPath $sourcePath -Raw
    $source = [regex]::Replace(
        $source,
        '/\*.*?\*/',
        ' ',
        [Text.RegularExpressions.RegexOptions]::Singleline
    )
    $source = [regex]::Replace(
        $source,
        '//[^\r\n]*',
        ' ',
        [Text.RegularExpressions.RegexOptions]::Multiline
    )
    return [regex]::Replace($source, '\s+', ' ').Trim()
}

function Assert-SourceWiring {
    param(
        [Parameter(Mandatory)]
        [string] $RelativePath,

        [Parameter(Mandatory)]
        [string] $NormalizedSource,

        [Parameter(Mandatory)]
        [string] $Pattern,

        [Parameter(Mandatory)]
        [string] $Expectation,

        [int] $MinimumOccurrences = 1
    )

    $matches = [regex]::Matches(
        $NormalizedSource,
        $Pattern,
        [Text.RegularExpressions.RegexOptions]::Singleline -bor
            [Text.RegularExpressions.RegexOptions]::CultureInvariant
    )
    if ($matches.Count -lt $MinimumOccurrences) {
        throw (
            "Runtime adapter wiring check failed in '$RelativePath': $Expectation " +
            "Expected at least $MinimumOccurrences normalized source match(es), found $($matches.Count). " +
            "Restore the production delegation or update this assertion with the intentional replacement."
        )
    }
}

Write-Host "Running whitespace verification."
Invoke-Git -Arguments @("diff", "--check")
Invoke-Git -Arguments @("diff", "--cached", "--check")

$isValidSha = {
    param([string] $Value)
    return -not [string]::IsNullOrWhiteSpace($Value) -and $Value -match "^[0-9a-fA-F]{40}$"
}

if (& $isValidSha $HeadSha) {
    & git -c "safe.directory=$repositoryGitPath" -C $repositoryRoot cat-file -e "$HeadSha`^{commit}" 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "HeadSha '$HeadSha' is not available in the checkout."
    }

    $baseIsUsable = $false
    if ((& $isValidSha $BaseSha) -and $BaseSha -notmatch "^0{40}$") {
        & git -c "safe.directory=$repositoryGitPath" -C $repositoryRoot cat-file -e "$BaseSha`^{commit}" 2>$null
        $baseIsUsable = $LASTEXITCODE -eq 0
    }

    if ($baseIsUsable) {
        Invoke-Git -Arguments @("diff", "--check", "$BaseSha...$HeadSha")
    }
    else {
        Invoke-Git -Arguments @("show", "--check", "--format=", $HeadSha)
    }
}
elseif (-not [string]::IsNullOrWhiteSpace($BaseSha) -or -not [string]::IsNullOrWhiteSpace($HeadSha)) {
    throw "BaseSha and HeadSha must be full 40-character Git object IDs when provided."
}

Write-Host "Validating solution membership and configuration mappings."
if (-not (Test-Path -LiteralPath $solutionPath -PathType Leaf)) {
    throw "Solution file was not found: '$solutionPath'."
}

$solutionText = Get-Content -LiteralPath $solutionPath -Raw
$solutionProjectMatches = [regex]::Matches(
    $solutionText,
    'Project\("[^"]+"\) = "[^"]+", "([^"]+\.csproj)", "\{[0-9A-Fa-f-]+\}"'
)
$actualProjects = @(
    $solutionProjectMatches |
        ForEach-Object { $_.Groups[1].Value.Replace("\", "/") }
)
$expectedProjects = @(
    "project/SamSWAT.FireSupport/SamSWAT.FireSupport.Core.csproj",
    "project/SamSWAT.FireSupport.Server/SamSWAT.FireSupport.Server.csproj",
    "project/SamSWAT.FireSupport.Fika.Interop/SamSWAT.FireSupport.Fika.Interop.csproj",
    "project/SamSWAT.FireSupport.Fika/SamSWAT.FireSupport.Fika.csproj",
    "project/Tylevo.TacticalServicesControl.RegressionTests/Tylevo.TacticalServicesControl.RegressionTests.csproj"
)

if ($actualProjects.Count -ne $expectedProjects.Count) {
    throw "Solution contains $($actualProjects.Count) project files; expected exactly $($expectedProjects.Count)."
}

foreach ($expectedProject in $expectedProjects) {
    if (@($actualProjects | Where-Object { $_.Equals($expectedProject, [StringComparison]::OrdinalIgnoreCase) }).Count -ne 1) {
        throw "Solution must contain exactly one '$expectedProject' entry."
    }
}

$interopGuid = "FC4AB935-71F3-48C2-A7A6-B2396E39BF41"
$bootstrapGuid = "E3D5AAB7-5B3B-4F3B-918F-93ECDA9EAA45"
$testsGuid = "2A1C7E6D-8D6F-4B81-A625-3DC7E3E0D44C"

Assert-SolutionMapping -SolutionText $solutionText -ProjectGuid $interopGuid -SolutionConfiguration "Release" -ProjectConfiguration "Release"
Assert-SolutionMapping -SolutionText $solutionText -ProjectGuid $interopGuid -SolutionConfiguration "SPT-4.0 Release" -ProjectConfiguration "SPT-4.0 Release"
foreach ($configuration in @("Debug", "SPT-3.10 Release", "SPT-3.11 Release", "SPT-4.0 Release", "Release")) {
    Assert-SolutionMapping -SolutionText $solutionText -ProjectGuid $bootstrapGuid -SolutionConfiguration $configuration -ProjectConfiguration $configuration
}

Assert-SolutionMapping -SolutionText $solutionText -ProjectGuid $testsGuid -SolutionConfiguration "Debug" -ProjectConfiguration "Debug"
Assert-SolutionMapping -SolutionText $solutionText -ProjectGuid $testsGuid -SolutionConfiguration "Release" -ProjectConfiguration "Release"
foreach ($configuration in @("SPT-3.10 Release", "SPT-3.11 Release", "SPT-4.0 Release")) {
    Assert-SolutionMapping -SolutionText $solutionText -ProjectGuid $testsGuid -SolutionConfiguration $configuration -ProjectConfiguration "Release"
}

Write-Host "Validating deploy guards."
$runtimeProjects = @(
    "project\SamSWAT.FireSupport\SamSWAT.FireSupport.Core.csproj",
    "project\SamSWAT.FireSupport.Server\SamSWAT.FireSupport.Server.csproj",
    "project\SamSWAT.FireSupport.Fika.Interop\SamSWAT.FireSupport.Fika.Interop.csproj",
    "project\SamSWAT.FireSupport.Fika\SamSWAT.FireSupport.Fika.csproj"
)
foreach ($relativeProject in $runtimeProjects) {
    $projectPath = Join-Path $repositoryRoot $relativeProject
    [xml] $projectXml = Get-Content -LiteralPath $projectPath -Raw
    $mutationTargets = @(
        $projectXml.Project.Target |
            Where-Object {
                $null -ne $_.PSObject.Properties["Exec"] -or
                $null -ne $_.PSObject.Properties["Delete"] -or
                $null -ne $_.PSObject.Properties["RemoveDir"]
            }
    )

    if ($mutationTargets.Count -eq 0) {
        throw "Expected at least one guarded deployment/package target in '$relativeProject'."
    }

    foreach ($target in $mutationTargets) {
        $condition = [string] $target.Condition
        if ($condition -notmatch [regex]::Escape('$(SkipTscDeploy)')) {
            throw "Mutating target '$($target.Name)' in '$relativeProject' is not guarded by SkipTscDeploy."
        }
    }
}

Write-Host "Validating the exclusive release archive workflow."
$trackedBuildDefinitions = @(
    & git -c "safe.directory=$repositoryGitPath" -C $repositoryRoot ls-files -- "*.csproj" "*.props" "*.targets"
)
if ($LASTEXITCODE -ne 0) {
    throw "git ls-files failed while locating tracked MSBuild definitions."
}

foreach ($relativeBuildDefinition in $trackedBuildDefinitions) {
    $definitionPath = Join-Path $repositoryRoot $relativeBuildDefinition
    $definitionText = Get-Content -LiteralPath $definitionPath -Raw
    if ($definitionText -match '(?i)\b(?:CreateReleaseZip|RemoveReleaseExcludedContent|SevenZipPath)\b') {
        throw "Removed legacy release-archive target/property was reintroduced in '$relativeBuildDefinition'."
    }

    if ($definitionText -match '(?i)(?:\b7z(?:\.exe)?\b|7-Zip)') {
        throw "MSBuild definitions must not invoke 7-Zip; use tools/New-ReleasePackage.ps1: '$relativeBuildDefinition'."
    }
}

Write-Host "Validating production wiring into proprietary-free regression seams."
$paymentSourcePath = "project\SamSWAT.FireSupport\Unity\FireSupportPayment.cs"
$paymentSource = Get-NormalizedCSharpSource -RelativePath $paymentSourcePath
Assert-SourceWiring `
    -RelativePath $paymentSourcePath `
    -NormalizedSource $paymentSource `
    -Pattern 'ApplyIncludedAuthorizations\s*\([^)]*\)\s*\{.{0,1200}?AuthorizationSnapshotPresence\s*\.\s*ShouldApply\s*\(' `
    -Expectation "ApplyIncludedAuthorizations must delegate snapshot-presence semantics to AuthorizationSnapshotPresence.ShouldApply."

$fikaSourcePath = "project\SamSWAT.FireSupport.Fika.Interop\FikaIntegration.cs"
$fikaSource = Get-NormalizedCSharpSource -RelativePath $fikaSourcePath
$fikaWiringChecks = @(
    @{
        Pattern = 'PendingRequestTable\s*<\s*SupportRequestFingerprint\s*,\s*ClientPendingRequest\s*>\s+s_pendingClientRequests\s*=\s*new\s*\('
        Expectation = "Fika client requests must be owned by a PendingRequestTable declaration."
    },
    @{
        Pattern = 's_pendingClientRequests\s*\.\s*GetOrAdd\s*\('
        Expectation = "Fika client request registration must flow through PendingRequestTable.GetOrAdd."
    },
    @{
        Pattern = 's_pendingClientRequests\s*\.\s*TryGetValue\s*\('
        Expectation = "Fika packet handlers must resolve pending requests through PendingRequestTable.TryGetValue."
    },
    @{
        Pattern = 's_pendingClientRequests\s*\.\s*ClearAndGetValues\s*\('
        Expectation = "Fika teardown must drain pending requests through PendingRequestTable.ClearAndGetValues."
    },
    @{
        Pattern = 'AcceptedEventRegistry\s*<\s*SupportRequestFingerprint\s*>\s+s_acceptedClientEvents\s*=\s*new\s*\('
        Expectation = "Accepted Fika events must be owned by an AcceptedEventRegistry declaration."
    },
    @{
        Pattern = 's_acceptedClientEvents\s*\.\s*Register\s*\('
        Expectation = "Accepted Fika event admission must flow through AcceptedEventRegistry.Register."
    },
    @{
        Pattern = 's_acceptedClientEvents\s*\.\s*TryGetValue\s*\('
        Expectation = "Accepted Fika event replay must resolve through AcceptedEventRegistry.TryGetValue."
    },
    @{
        Pattern = 's_acceptedClientEvents\s*\.\s*Clear\s*\('
        Expectation = "Fika teardown must clear AcceptedEventRegistry state."
    },
    @{
        Pattern = 'FirstResult\s*<\s*FireSupportNetworkRequestResult\s*>\s+_result\s*=\s*new\s*\('
        Expectation = "ClientPendingRequest must own its terminal result through FirstResult."
    },
    @{
        Pattern = '_result\s*\.\s*TrySet\s*\('
        Expectation = "ClientPendingRequest completion must use FirstResult.TrySet."
    },
    @{
        Pattern = '_result\s*\.\s*TryGet\s*\('
        Expectation = "ClientPendingRequest result reads must use FirstResult.TryGet."
    },
    @{
        Pattern = 'AuthorityExecutionTransition\s*<\s*AuthorityOutcome\s*>\s+_transition\s*=\s*new\s*\('
        Expectation = "AuthorityRequestEntry must own execution state through AuthorityExecutionTransition."
    },
    @{
        Pattern = '_transition\s*\.\s*TryBeginExecution\s*\('
        Expectation = "Authority work must begin through AuthorityExecutionTransition.TryBeginExecution."
    },
    @{
        Pattern = '_transition\s*\.\s*TryComplete\s*\('
        Expectation = "Authority completion must flow through AuthorityExecutionTransition.TryComplete."
    },
    @{
        Pattern = '_transition\s*\.\s*TryCancelBeforeExecution\s*\('
        Expectation = "Pre-execution cancellation must flow through AuthorityExecutionTransition.TryCancelBeforeExecution."
    },
    @{
        Pattern = '_transition\s*\.\s*Abandon\s*\('
        Expectation = "Raid teardown/peer loss must abandon authority execution through AuthorityExecutionTransition.Abandon."
    }
)
foreach ($check in $fikaWiringChecks) {
    Assert-SourceWiring `
        -RelativePath $fikaSourcePath `
        -NormalizedSource $fikaSource `
        -Pattern $check.Pattern `
        -Expectation $check.Expectation
}

$heliSourcePath = "project\SamSWAT.FireSupport\Unity\HeliExfiltrationPoint.cs"
$heliSource = Get-NormalizedCSharpSource -RelativePath $heliSourcePath
$heliWiringChecks = @(
    @{
        Pattern = 'ExtractionCountdownClock\s+_countdown\s*=\s*new\s*\('
        Expectation = "HeliExfiltrationPoint must own an ExtractionCountdownClock."
    },
    @{
        Pattern = '_countdown\s*\.\s*Initialize\s*\('
        Expectation = "HeliExfiltrationPoint initialization must configure ExtractionCountdownClock."
    },
    @{
        Pattern = '_countdown\s*\.\s*Reset\s*\('
        Expectation = "Extraction-zone reset behavior must delegate to ExtractionCountdownClock.Reset."
    },
    @{
        Pattern = '_countdown\s*\.\s*Advance\s*\('
        Expectation = "Extraction countdown advancement must delegate to ExtractionCountdownClock.Advance."
    },
    @{
        Pattern = '_countdown\s*\.\s*IsComplete\b'
        Expectation = "Extraction completion must be governed by ExtractionCountdownClock.IsComplete."
    }
)
foreach ($check in $heliWiringChecks) {
    Assert-SourceWiring `
        -RelativePath $heliSourcePath `
        -NormalizedSource $heliSource `
        -Pattern $check.Pattern `
        -Expectation $check.Expectation
}

$tuningSourcePath = "project\SamSWAT.FireSupport\Unity\FireSupportTuningSettings.cs"
$tuningSource = Get-NormalizedCSharpSource -RelativePath $tuningSourcePath
Assert-SourceWiring `
    -RelativePath $tuningSourcePath `
    -NormalizedSource $tuningSource `
    -Pattern 'CaptureHelicopterTiming\s*\([^)]*\)\s*\{.{0,1800}?return\s+ExtractionTimingPolicy\s*\.\s*CreateRuntimeSnapshot\s*\(' `
    -Expectation "CaptureHelicopterTiming must delegate runtime clamping/snapshot creation to ExtractionTimingPolicy.CreateRuntimeSnapshot."

$serverConfigSourcePath = "project\SamSWAT.FireSupport.Server\FireSupportServerConfigService.cs"
$serverConfigSource = Get-NormalizedCSharpSource -RelativePath $serverConfigSourcePath
$serverWiringChecks = @(
    @{
        Pattern = 'TryValidateConfig\s*\([^)]*\)\s*\{.{0,3500}?TryValidateExtractionTiming\s*\(\s*config\s*\.\s*Extraction\b.{0,1200}?TryValidateExtractionTiming\s*\(\s*config\s*\.\s*PriorityExfil\b'
        Expectation = "Server config validation must validate both standard and priority extraction timing through TryValidateExtractionTiming."
    },
    @{
        Pattern = 'TryValidateExtractionTiming\s*\([^)]*\)\s*\{\s*return\s+ExtractionTimingPolicy\s*\.\s*TryValidate\s*\('
        Expectation = "Server extraction validation must delegate to ExtractionTimingPolicy.TryValidate."
    },
    @{
        Pattern = 'RepairInvalidExtractionTimings\s*\([^)]*\)\s*\{.{0,1200}?RepairExtractionTiming\s*\(\s*config\s*\.\s*Extraction\b.{0,500}?RepairExtractionTiming\s*\(\s*config\s*\.\s*PriorityExfil\b'
        Expectation = "Server migration repair must repair both standard and priority extraction timing."
    },
    @{
        Pattern = 'RepairExtractionTiming\s*\([^)]*\)\s*\{.{0,800}?ExtractionTimingPolicy\s*\.\s*Repair\s*\('
        Expectation = "Server extraction repair must delegate to ExtractionTimingPolicy.Repair."
    }
)
foreach ($check in $serverWiringChecks) {
    Assert-SourceWiring `
        -RelativePath $serverConfigSourcePath `
        -NormalizedSource $serverConfigSource `
        -Pattern $check.Pattern `
        -Expectation $check.Expectation
}

Write-Host "Validating shipped JSON and dashboard JavaScript syntax."
foreach ($powerShellTool in Get-ChildItem -LiteralPath $PSScriptRoot -File -Filter "*.ps1") {
    $toolTokens = $null
    $toolParseErrors = $null
    [void] [Management.Automation.Language.Parser]::ParseFile(
        $powerShellTool.FullName,
        [ref] $toolTokens,
        [ref] $toolParseErrors
    )
    if ($toolParseErrors.Count -ne 0) {
        throw (
            "PowerShell syntax validation failed for '$($powerShellTool.Name)':" +
            [Environment]::NewLine +
            (($toolParseErrors | ForEach-Object { $_.Message }) -join [Environment]::NewLine)
        )
    }
}

$jsonRoots = @(
    (Join-Path $repositoryRoot "project\SamSWAT.FireSupport\CopyToOutput")
    (Join-Path $repositoryRoot "project\SamSWAT.FireSupport.Server\CopyToOutput")
)
foreach ($jsonRoot in $jsonRoots) {
    foreach ($jsonFile in Get-ChildItem -LiteralPath $jsonRoot -File -Recurse -Filter "*.json") {
        try {
            [void] (Get-Content -LiteralPath $jsonFile.FullName -Raw | ConvertFrom-Json)
        }
        catch {
            throw "Invalid JSON in '$($jsonFile.FullName)': $($_.Exception.Message)"
        }
    }
}

[void] (Get-Content -LiteralPath (Join-Path $PSScriptRoot "package-layout.allowlist.json") -Raw | ConvertFrom-Json)
Invoke-Checked -FilePath "node" -Arguments @(
    "--check",
    (Join-Path $repositoryRoot "project\SamSWAT.FireSupport.Server\CopyToOutput\web\app.mjs")
)

Write-Host "Validating release identity and metadata."
& (Join-Path $PSScriptRoot "Test-ReleaseMetadata.ps1") -RepositoryRoot $repositoryRoot
if (-not $?) {
    throw "Release metadata verification failed."
}

Write-Host "Validating tracked-file hygiene."
$trackedFiles = @(& git -c "safe.directory=$repositoryGitPath" -C $repositoryRoot ls-files)
if ($LASTEXITCODE -ne 0) {
    throw "git ls-files failed with exit code $LASTEXITCODE."
}

$forbiddenTrackedExtensions = @(
    ".7z",
    ".binlog",
    ".bundle",
    ".dll",
    ".exe",
    ".nupkg",
    ".pdb",
    ".snupkg",
    ".zip"
)
$textExtensions = @(
    ".cs",
    ".csproj",
    ".css",
    ".html",
    ".json",
    ".md",
    ".mjs",
    ".props",
    ".ps1",
    ".sln",
    ".targets",
    ".txt",
    ".xml",
    ".yaml",
    ".yml"
)

foreach ($trackedFile in $trackedFiles) {
    $lowerTrackedFile = $trackedFile.ToLowerInvariant()
    foreach ($extension in $forbiddenTrackedExtensions) {
        if ($lowerTrackedFile.EndsWith($extension, [StringComparison]::Ordinal)) {
            throw "Proprietary/generated binary archive is tracked: '$trackedFile'."
        }
    }

    $extension = [IO.Path]::GetExtension($trackedFile).ToLowerInvariant()
    if ($textExtensions -contains $extension) {
        $fullPath = Join-Path $repositoryRoot $trackedFile
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            # A locally deleted tracked file remains in `git ls-files` until
            # staged. Whitespace checks already cover the deletion diff.
            continue
        }

        $content = Get-Content -LiteralPath $fullPath -Raw
        if ($content -match '(?i)[A-Z]:[\\/](?:Users[\\/][^\\/\s"''`]+|SPT(?:[\\/]|$))') {
            throw "Tracked build/source file contains a user-specific or live-SPT absolute path: '$trackedFile'."
        }

        if ($content -match '(?i)-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----' -or
            $content -match '(?i)\b(?:ghp_|github_pat_)[A-Za-z0-9_]{20,}') {
            throw "Tracked text file appears to contain a private key or access token: '$trackedFile'."
        }
    }
}

& (Join-Path $PSScriptRoot "Test-PackageLayout.ps1") -ValidateSourceInputs
if (-not $?) {
    throw "Package allowlist source validation failed."
}

Write-Host "Running proprietary-free regression suite."
if (-not (Test-Path -LiteralPath $regressionProject -PathType Leaf)) {
    throw "Regression runner project was not found: '$regressionProject'."
}

Invoke-Checked -FilePath "dotnet" -Arguments @(
    "run",
    "--project",
    $regressionProject,
    "--configuration",
    "Release"
)

Write-Host "CI-safe verification passed."
