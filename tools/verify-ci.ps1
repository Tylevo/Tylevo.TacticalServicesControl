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

$coreGuid = "7090B555-280C-4839-A367-C874414EC11F"
$serverGuid = "0C20B0FC-EC60-40AD-8BA9-6FFF5C084849"
$interopGuid = "FC4AB935-71F3-48C2-A7A6-B2396E39BF41"
$bootstrapGuid = "E3D5AAB7-5B3B-4F3B-918F-93ECDA9EAA45"
$testsGuid = "2A1C7E6D-8D6F-4B81-A625-3DC7E3E0D44C"

Assert-SolutionMapping -SolutionText $solutionText -ProjectGuid $coreGuid -SolutionConfiguration "SPT-4.1 Release" -ProjectConfiguration "SPT-4.1 Release"
Assert-SolutionMapping -SolutionText $solutionText -ProjectGuid $serverGuid -SolutionConfiguration "SPT-4.1 Release" -ProjectConfiguration "SPT-4.1 Release"
Assert-SolutionMapping -SolutionText $solutionText -ProjectGuid $interopGuid -SolutionConfiguration "Release" -ProjectConfiguration "Release"
Assert-SolutionMapping -SolutionText $solutionText -ProjectGuid $interopGuid -SolutionConfiguration "SPT-4.0 Release" -ProjectConfiguration "SPT-4.0 Release"
Assert-SolutionMapping -SolutionText $solutionText -ProjectGuid $interopGuid -SolutionConfiguration "SPT-4.1 Release" -ProjectConfiguration "SPT-4.1 Release"
foreach ($configuration in @("Debug", "SPT-3.10 Release", "SPT-3.11 Release", "SPT-4.0 Release", "SPT-4.1 Release", "Release")) {
    Assert-SolutionMapping -SolutionText $solutionText -ProjectGuid $bootstrapGuid -SolutionConfiguration $configuration -ProjectConfiguration $configuration
}

Assert-SolutionMapping -SolutionText $solutionText -ProjectGuid $testsGuid -SolutionConfiguration "Debug" -ProjectConfiguration "Debug"
Assert-SolutionMapping -SolutionText $solutionText -ProjectGuid $testsGuid -SolutionConfiguration "Release" -ProjectConfiguration "Release"
foreach ($configuration in @("SPT-3.10 Release", "SPT-3.11 Release", "SPT-4.0 Release", "SPT-4.1 Release")) {
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

$serverProjectPath = Join-Path $repositoryRoot "project\SamSWAT.FireSupport.Server\SamSWAT.FireSupport.Server.csproj"
[xml] $serverProjectXml = Get-Content -LiteralPath $serverProjectPath -Raw
$serverPostBuildTargets = @(
    $serverProjectXml.Project.Target |
        Where-Object { ([string] $_.Name).Equals("PostBuild", [StringComparison]::Ordinal) }
)
if ($serverPostBuildTargets.Count -ne 1) {
    throw "Expected exactly one Server PostBuild target."
}

$serverPostBuildCommand = [string] $serverPostBuildTargets[0].Exec.Command
if ($serverPostBuildCommand -notmatch '(?i)\brobocopy\b.+?/XF\b[^\r\n]*\btsc-config\.json\b') {
    throw "Server PostBuild must exclude mutable tsc-config.json from developer deployment."
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
$inputManagerSourcePath = "project\SamSWAT.FireSupport\Utils\InputManagerUtil.cs"
$inputManagerSource = Get-NormalizedCSharpSource -RelativePath $inputManagerSourcePath
Assert-SourceWiring `
    -RelativePath $inputManagerSourcePath `
    -NormalizedSource $inputManagerSource `
    -Pattern 'AccessTools\s*\.\s*DeclaredMethod\s*\(\s*typeof\s*\(\s*InputManager\s*\)\s*,\s*nameof\s*\(\s*InputManager\s*\.\s*Create\s*\)\s*,\s*\[\s*typeof\s*\(\s*KeyGroup\s*\[\s*\]\s*\)\s*,\s*typeof\s*\(\s*AxisGroup\s*\[\s*\]\s*\)\s*,\s*typeof\s*\(\s*float\s*\)\s*,\s*typeof\s*\(\s*bool\s*\)\s*\]\s*\)' `
    -Expectation "The SPT 4.1 input-manager patch must select the exact four-parameter Create overload instead of performing an ambiguous name-only lookup."

$mainMenuControllerSourcePath = "project\SamSWAT.FireSupport\Unity\MainMenuPurchaseController.TaskBar.cs"
$mainMenuControllerSource = Get-NormalizedCSharpSource -RelativePath $mainMenuControllerSourcePath
Assert-SourceWiring `
    -RelativePath $mainMenuControllerSourcePath `
    -NormalizedSource $mainMenuControllerSource `
    -Pattern 'PreloaderUI\s*\.\s*Instance\s*\?\s*\.\s*MenuTaskBar' `
    -Expectation "The Uplink shortcut must resolve EFT's persistent bottom bar through PreloaderUI instead of inserting or repositioning center-menu rows."

$globalUsingsSourcePath = "project\SamSWAT.FireSupport\GlobalUsings.cs"
$globalUsingsSource = Get-NormalizedCSharpSource -RelativePath $globalUsingsSourcePath
Assert-SourceWiring `
    -RelativePath $globalUsingsSourcePath `
    -NormalizedSource $globalUsingsSource `
    -Pattern 'global\s+using\s+IBattleUIScreenController\s*=\s*EFT\.UI\.IBattleUIScreenController\s*;' `
    -Expectation "The battle-screen compatibility alias must target EFT.UI.IBattleUIScreenController."

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

$cargoPointSourcePath = "project\SamSWAT.FireSupport\Unity\HeliCargoTransferPoint.cs"
$cargoPointSource = Get-NormalizedCSharpSource -RelativePath $cargoPointSourcePath
foreach ($check in @(
    @{
        Pattern = 'class\s+HeliCargoTransferPoint\b'
        Expectation = "Cargo must own a dedicated HeliCargoTransferPoint component."
    },
    @{
        Pattern = 'FireSupportItemTransfer\s*\.\s*EnterZone\s*\('
        Expectation = "The Cargo point must register its requester-local transfer interaction."
    },
    @{
        Pattern = 'FireSupportItemTransfer\s*\.\s*PointDestroyed\s*\('
        Expectation = "The Cargo point must close transfer state when the helicopter departs."
    }
)) {
    Assert-SourceWiring `
        -RelativePath $cargoPointSourcePath `
        -NormalizedSource $cargoPointSource `
        -Pattern $check.Pattern `
        -Expectation $check.Expectation
}

if ($cargoPointSource -match
    'HeliExfiltrationPoint|ExtractionCountdownClock|BattleUIPanelExitTrigger|' +
    'FireSupportExtraction|TryOverrideExtract|ISessionStopper|StopSession|' +
    'ExitStatus|extractTime') {
    throw "HeliCargoTransferPoint must contain no extraction countdown or raid-ending wiring."
}
if ($heliSource -match
    'PriorityExfil|FireSupportItemTransfer|HeliCargoTransferPoint|GInterface177') {
    throw "HeliExfiltrationPoint must remain a standard-Extraction-only component."
}

$uh60SourcePath = "project\SamSWAT.FireSupport\Unity\Vehicles\UH60Behaviour.cs"
$uh60Source = Get-NormalizedCSharpSource -RelativePath $uh60SourcePath
foreach ($check in @(
    @{
        Pattern = 'CreateLandingPoint\s*\([^)]*\)\s*\{.{0,700}?requestSupportType\s*==\s*ESupportType\s*\.\s*PriorityExfil.{0,300}?CreateCargoTransferPoint\s*\(.{0,500}?CreateExtractionPoint\s*\('
        Expectation = "UH60Behaviour must route PriorityExfil to Cargo and ordinary Extract to separate landing-point factories."
    },
    @{
        Pattern = 'CreateCargoTransferPoint\s*\([^)]*\)\s*\{.{0,700}?AddComponent\s*<\s*HeliCargoTransferPoint\s*>\s*\('
        Expectation = "The Cargo factory must instantiate only the Cargo interaction component."
    },
    @{
        Pattern = 'CreateExtractionPoint\s*\([^)]*\)\s*\{.{0,700}?AddComponent\s*<\s*HeliExfiltrationPoint\s*>\s*\(.{0,400}?timingSnapshot\s*\.\s*ExtractTimeSeconds'
        Expectation = "The standard extraction factory must own the extraction component and countdown duration."
    }
)) {
    Assert-SourceWiring `
        -RelativePath $uh60SourcePath `
        -NormalizedSource $uh60Source `
        -Pattern $check.Pattern `
        -Expectation $check.Expectation
}

$controllerSourcePath = "project\SamSWAT.FireSupport\Unity\FireSupportController.cs"
$controllerSource = Get-NormalizedCSharpSource -RelativePath $controllerSourcePath
Assert-SourceWiring `
    -RelativePath $controllerSourcePath `
    -NormalizedSource $controllerSource `
    -Pattern 'new\s+HeliCargoTransferService\s*\(' `
    -Expectation "The released PriorityExfil slot must register the dedicated Cargo dispatch service."
if ($controllerSource -match
    'new\s+HeliExfiltrationService\s*\([^;]{0,500}?PriorityExfil') {
    throw "PriorityExfil must not be registered through HeliExfiltrationService."
}

foreach ($bridgeSourcePath in @(
    "project\SamSWAT.FireSupport\Unity\FireSupportItemTransfer.cs",
    "project\SamSWAT.FireSupport\Patches\HelicopterItemTransferInteractionPatches.cs"
)) {
    $bridgeSource = Get-NormalizedCSharpSource -RelativePath $bridgeSourcePath
    Assert-SourceWiring `
        -RelativePath $bridgeSourcePath `
        -NormalizedSource $bridgeSource `
        -Pattern '\bHeliCargoTransferPoint\b' `
        -Expectation "Item-transfer wiring must target the dedicated Cargo point."
    if ($bridgeSource -match '\bHeliExfiltrationPoint\b') {
        throw "$bridgeSourcePath must not bind item transfer to the standard extraction point."
    }
}

$tuningSourcePath = "project\SamSWAT.FireSupport\Unity\FireSupportTuningSettings.cs"
$tuningSource = Get-NormalizedCSharpSource -RelativePath $tuningSourcePath
Assert-SourceWiring `
    -RelativePath $tuningSourcePath `
    -NormalizedSource $tuningSource `
    -Pattern 'CaptureHelicopterTiming\s*\([^)]*\)\s*\{.{0,1800}?CargoTimingPolicy\s*\.\s*CreateRuntimeSnapshot\s*\(.{0,800}?ExtractionTimingPolicy\s*\.\s*CreateRuntimeSnapshot\s*\(' `
    -Expectation "CaptureHelicopterTiming must route Cargo to CargoTimingPolicy and standard Extraction to ExtractionTimingPolicy."
if ($tuningSource -match
    'PriorityExfilHelicopterExtractTime|priorityExfilHelicopterExtractTime') {
    throw "Active runtime tuning must not retain a PriorityExfil extraction-time state or argument."
}

$serverConfigSourcePath = "project\SamSWAT.FireSupport.Server\FireSupportServerConfigService.cs"
$serverConfigSource = Get-NormalizedCSharpSource -RelativePath $serverConfigSourcePath
Assert-SourceWiring `
    -RelativePath $serverConfigSourcePath `
    -NormalizedSource $serverConfigSource `
    -Pattern '\[\s*Injectable\s*\(\s*InjectionType\s*\.\s*Singleton\s*\)\s*\]\s*public\s+sealed\s+class\s+FireSupportServerConfigService\b' `
    -Expectation "The server config service must be a singleton so startup initialization and HTTP requests share the same config and dashboard paths."

$authorizationLedgerSourcePath = "project\SamSWAT.FireSupport.Server\FireSupportAuthorizationLedger.cs"
$authorizationLedgerSource = Get-NormalizedCSharpSource -RelativePath $authorizationLedgerSourcePath
Assert-SourceWiring `
    -RelativePath $authorizationLedgerSourcePath `
    -NormalizedSource $authorizationLedgerSource `
    -Pattern '\[\s*Injectable\s*\(\s*InjectionType\s*\.\s*Singleton\s*\)\s*\]\s*public\s+sealed\s+class\s+FireSupportAuthorizationLedger\b' `
    -Expectation "The authorization ledger must remain a singleton so all request handlers share one initialized persistent state."

$serverWiringChecks = @(
    @{
        Pattern = 'NormalizeConfig\s*\([^)]*\)\s*\{.{0,700}?FireSupportServerConfigMigration\s*\.\s*NormalizePersistedFields\s*\('
        Expectation = "Server config normalization must delegate persisted migration and response-field sanitization to FireSupportServerConfigMigration.NormalizePersistedFields."
    },
    @{
        Pattern = 'TryValidateConfig\s*\([^)]*\)\s*\{.{0,3500}?TryValidateExtractionTiming\s*\(\s*config\s*\.\s*Extraction\b.{0,1200}?TryValidateCargoTiming\s*\(\s*config\s*\.\s*PriorityExfil\b'
        Expectation = "Server config validation must keep standard Extraction on its countdown contract and validate the released priorityExfil path through the Cargo timing contract."
    },
    @{
        Pattern = 'TryValidateExtractionTiming\s*\([^)]*\)\s*\{\s*return\s+ExtractionTimingPolicy\s*\.\s*TryValidate\s*\('
        Expectation = "Server extraction validation must delegate to ExtractionTimingPolicy.TryValidate."
    },
    @{
        Pattern = 'TryValidateCargoTiming\s*\([^)]*\)\s*\{\s*return\s+CargoTimingPolicy\s*\.\s*TryValidate\s*\('
        Expectation = "Server Cargo validation must delegate to CargoTimingPolicy.TryValidate."
    },
    @{
        Pattern = 'RepairInvalidServiceTimings\s*\([^)]*\)\s*\{.{0,1200}?RepairExtractionTiming\s*\(\s*config\s*\.\s*Extraction\b.{0,500}?RepairCargoTiming\s*\(\s*config\s*\.\s*PriorityExfil\b'
        Expectation = "Server migration repair must keep standard Extraction and Cargo on their distinct timing contracts."
    },
    @{
        Pattern = 'RepairExtractionTiming\s*\([^)]*\)\s*\{.{0,800}?ExtractionTimingPolicy\s*\.\s*Repair\s*\('
        Expectation = "Server extraction repair must delegate to ExtractionTimingPolicy.Repair."
    },
    @{
        Pattern = 'RepairCargoTiming\s*\([^)]*\)\s*\{.{0,800}?CargoTimingPolicy\s*\.\s*Repair\s*\('
        Expectation = "Server Cargo repair must delegate to CargoTimingPolicy.Repair."
    },
    @{
        Pattern = 'Field\s*\(\s*"prices\.PriorityExfil"\s*,\s*"Cargo Transfer Price"'
        Expectation = "The released PriorityExfil price path must be labeled Cargo Transfer Price in the dashboard."
    },
    @{
        Pattern = 'Field\s*\(\s*"enabled\.PriorityExfil"\s*,\s*"Cargo Transfer Enabled"'
        Expectation = "The released PriorityExfil enabled path must be labeled Cargo Transfer Enabled in the dashboard."
    },
    @{
        Pattern = 'Section\s*\(\s*"extraction"\s*,\s*"UH-60 Services"'
        Expectation = "The extraction dashboard section must be presented as UH-60 Services."
    },
    @{
        Pattern = 'Field\s*\(\s*"priorityExfil\.dispatchDelaySeconds"\s*,\s*"Cargo Dispatch Delay".{0,800}?Field\s*\(\s*"priorityExfil\.waitTimeSeconds"\s*,\s*"Cargo Wait Time".{0,800}?Field\s*\(\s*"priorityExfil\.speedMultiplier"\s*,\s*"Cargo Speed Multiplier"'
        Expectation = "Cargo dashboard timing must expose the released priorityExfil dispatch, wait, and speed paths with Cargo labels."
    }
)
foreach ($check in $serverWiringChecks) {
    Assert-SourceWiring `
        -RelativePath $serverConfigSourcePath `
        -NormalizedSource $serverConfigSource `
        -Pattern $check.Pattern `
        -Expectation $check.Expectation
}

if ($serverConfigSource -match 'Field\s*\(\s*"priorityExfil\.extractTimeSeconds"') {
    throw "Cargo dashboard schema must not expose the retired priorityExfil extraction-countdown field."
}

if ($serverConfigSource -match '"Priority Exfil (?:Price|Enabled)"' -or
    $serverConfigSource -match '"Priority Extraction Time"') {
    throw "Cargo dashboard schema still contains a retired Priority Exfil display label."
}

if ($serverConfigSource -match 'RepairCargoTiming\s*\([^)]*\)\s*\{.{0,1200}?settings\s*\.\s*ExtractTimeSeconds\s*=') {
    throw "Cargo repair must preserve the stored legacy priorityExfil.extractTimeSeconds value."
}

$cargoTimingSourcePath = "project\SamSWAT.FireSupport\Unity\CargoTimingPolicy.cs"
$cargoTimingSource = Get-NormalizedCSharpSource -RelativePath $cargoTimingSourcePath
Assert-SourceWiring `
    -RelativePath $cargoTimingSourcePath `
    -NormalizedSource $cargoTimingSource `
    -Pattern 'Repair\s*\([^)]*\)\s*\{.{0,2400}?return\s+new\s+ExtractionTimingValues\s*\([^)]*settings\s*\.\s*ExtractTimeSeconds' `
    -Expectation "Cargo repair must carry the stored legacy extractTimeSeconds value through unchanged."
Assert-SourceWiring `
    -RelativePath $cargoTimingSourcePath `
    -NormalizedSource $cargoTimingSource `
    -Pattern 'CreateRuntimeSnapshot\s*\([^)]*\)\s*\{.{0,1000}?new\s+HelicopterTimingSnapshot\s*\(\s*ESupportType\s*\.\s*PriorityExfil.{0,600}?\b0f\s*,\s*Math\s*\.\s*Max\s*\(\s*ExtractionTimingPolicy\s*\.\s*RuntimeMinSpeedMultiplier' `
    -Expectation "Cargo runtime snapshots must zero the dormant extraction-time compatibility field."

$configContractSourcePath = "project\SamSWAT.FireSupport\Unity\RaidOpsFireSupportServerConfig.cs"
$configContractSource = Get-NormalizedCSharpSource -RelativePath $configContractSourcePath
Assert-SourceWiring `
    -RelativePath $configContractSourcePath `
    -NormalizedSource $configContractSource `
    -Pattern 'CargoSettings\s+PriorityExfil\s*\{' `
    -Expectation "The released PriorityExfil config path must use a Cargo-specific settings contract."

$fikaCargoValidation = [regex]::Match(
    $fikaSource,
    'if\s*\(\s*packet\s*\.\s*SupportType\s*==\s*ESupportType\s*\.\s*PriorityExfil\s*&&(?<body>.*?)reason\s*=\s*"InvalidCargoTimingContract"',
    [Text.RegularExpressions.RegexOptions]::Singleline -bor
        [Text.RegularExpressions.RegexOptions]::CultureInvariant
)
if (-not $fikaCargoValidation.Success) {
    throw "Fika request validation must own a distinct InvalidCargoTimingContract branch for wire type 10."
}
if ($fikaCargoValidation.Groups["body"].Value -match 'HelicopterExtractTimeSeconds|MinimumExtractionWindowMarginSeconds') {
    throw "Fika Cargo validation must not inspect the legacy extract-time field or extraction-window relationship."
}
foreach ($requiredCargoField in @(
    "HelicopterDispatchDelaySeconds",
    "HelicopterWaitTimeSeconds",
    "HelicopterSpeedMultiplier"
)) {
    if ($fikaCargoValidation.Groups["body"].Value.IndexOf(
            $requiredCargoField,
            [StringComparison]::Ordinal
        ) -lt 0) {
        throw "Fika Cargo validation does not validate $requiredCargoField."
    }
}

Assert-SourceWiring `
    -RelativePath $fikaSourcePath `
    -NormalizedSource $fikaSource `
    -Pattern 'HelicopterTimingsEqual\s*\([^)]*\)\s*\{.{0,900}?left\s*\.\s*SupportType\s*==\s*ESupportType\s*\.\s*PriorityExfil\s*\|\|.{0,300}?left\s*\.\s*ExtractTimeSeconds\s*\.\s*Equals' `
    -Expectation "Fika host timing equality must ignore the compatibility extract field for Cargo while retaining it for standard Extraction."
Assert-SourceWiring `
    -RelativePath $fikaSourcePath `
    -NormalizedSource $fikaSource `
    -Pattern '_helicopterExtractTimeSeconds\s*=\s*packet\s*\.\s*SupportType\s*==\s*ESupportType\s*\.\s*PriorityExfil\s*\?\s*0f\s*:\s*packet\s*\.\s*HelicopterExtractTimeSeconds' `
    -Expectation "Fika request fingerprints must normalize the ignored Cargo extract field."
Assert-SourceWiring `
    -RelativePath $fikaSourcePath `
    -NormalizedSource $fikaSource `
    -Pattern 'IsStandardExtraction\s*\([^)]*\)\s*\{\s*return\s+supportType\s*==\s*ESupportType\s*\.\s*Extract\s*;' `
    -Expectation "Fika extraction-only behavior must recognize standard Extract and never Cargo."
Assert-SourceWiring `
    -RelativePath $fikaSourcePath `
    -NormalizedSource $fikaSource `
    -Pattern 'request\s*\.\s*SupportType\s*==\s*ESupportType\s*\.\s*PriorityExfil\s*&&\s*\(\s*entry\s*\.\s*OriginPeer\s*!=\s*null\s*\|\|\s*IsFikaHeadlessHost\s*\(\s*\)\s*\).{0,250}?"CargoHostOnly"' `
    -Expectation "Fika authority must reject remote or headless Cargo requests before dispatch."
if ($fikaSource -match '\bIsExtractionType\s*\(') {
    throw "Fika must not classify Cargo through an extraction-type helper."
}

foreach ($packetSourcePath in @(
    "project\SamSWAT.FireSupport.Fika.Interop\FireSupportRequestPacket.cs",
    "project\SamSWAT.FireSupport.Fika.Interop\FireSupportAuthorityResultPacket.cs"
)) {
    $packetSource = Get-NormalizedCSharpSource -RelativePath $packetSourcePath
    Assert-SourceWiring `
        -RelativePath $packetSourcePath `
        -NormalizedSource $packetSource `
        -Pattern 'SupportType\s*==\s*ESupportType\s*\.\s*PriorityExfil\s*\?\s*0f\s*:' `
        -Expectation "Fika Cargo packet runtime consumers must zero the legacy extraction-time slot."
}

Write-Host "Validating tracked JSON and dashboard JavaScript syntax."
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
    (Join-Path $repositoryRoot "project\SamSWAT.FireSupport.Server\ConfigSources")
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

Write-Host "Running dashboard interaction regression tests."
Invoke-Checked -FilePath "node" -Arguments @(
    "--test",
    (Join-Path $PSScriptRoot "tests\dashboard.test.mjs")
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
$reviewedUplinkOverride = "tools/assets/spt-4.1.2/uav_uplink_container.bundle"
$reviewedUplinkOverrideLength = 1167863
$reviewedUplinkOverrideSha256 =
    "8C9F8D8878076D4FFCB2687D62609F606552B3E9F3529FBE584DF79E43365861"

foreach ($trackedFile in $trackedFiles) {
    $lowerTrackedFile = $trackedFile.ToLowerInvariant()
    foreach ($extension in $forbiddenTrackedExtensions) {
        if ($lowerTrackedFile.EndsWith($extension, [StringComparison]::Ordinal)) {
            if ($trackedFile.Equals($reviewedUplinkOverride, [StringComparison]::Ordinal)) {
                $reviewedPath = Join-Path $repositoryRoot $trackedFile
                $reviewedFile = Get-Item -LiteralPath $reviewedPath
                $reviewedHash = (Get-FileHash -LiteralPath $reviewedPath -Algorithm SHA256).Hash.ToUpperInvariant()
                if ($reviewedFile.Length -ne $reviewedUplinkOverrideLength -or
                    $reviewedHash -cne $reviewedUplinkOverrideSha256) {
                    throw "Reviewed SPT 4.1.2 Uplink override pin mismatch: '$trackedFile'."
                }

                break
            }

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

Write-Host "Testing the TSC-only package contract with synthetic directory and ZIP fixtures."
& (Join-Path $PSScriptRoot "tests\package-contract.test.ps1")
if (-not $?) { throw "TSC-only package contract tests failed." }

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
