[CmdletBinding()]
param(
    [string] $RepositoryRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Assert-Condition {
    param(
        [Parameter(Mandatory)]
        [bool] $Condition,

        [Parameter(Mandatory)]
        [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Get-RequiredFileText {
    param(
        [Parameter(Mandatory)]
        [string] $RelativePath
    )

    $path = Join-Path $script:ResolvedRepositoryRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required release metadata file is missing: '$RelativePath'."
    }

    return Get-Content -LiteralPath $path -Raw
}

function Get-RequiredXmlValue {
    param(
        [Parameter(Mandatory)]
        [xml] $Document,

        [Parameter(Mandatory)]
        [string] $XPath,

        [Parameter(Mandatory)]
        [string] $Description
    )

    $nodes = @($Document.SelectNodes($XPath))
    if ($nodes.Count -ne 1) {
        throw "Expected exactly one $Description at XPath '$XPath'; found $($nodes.Count)."
    }

    $value = [string] $nodes[0].InnerText
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "$Description must not be empty."
    }

    return $value.Trim()
}

function Assert-TextMatch {
    param(
        [Parameter(Mandatory)]
        [string] $Text,

        [Parameter(Mandatory)]
        [string] $Pattern,

        [Parameter(Mandatory)]
        [string] $Description
    )

    if (-not [regex]::IsMatch(
        $Text,
        $Pattern,
        [System.Text.RegularExpressions.RegexOptions]::Multiline -bor
            [System.Text.RegularExpressions.RegexOptions]::Singleline)) {
        throw "Release metadata check failed: $Description."
    }
}

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Split-Path -Parent $PSScriptRoot
}

$ResolvedRepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$propsRelativePath = "Directory.Build.props"
$propsText = Get-RequiredFileText -RelativePath $propsRelativePath

try {
    [xml] $props = $propsText
}
catch {
    throw "'$propsRelativePath' is not valid XML: $($_.Exception.Message)"
}

$globalJsonRelativePath = "global.json"
try {
    $globalJson =
        Get-RequiredFileText -RelativePath $globalJsonRelativePath |
            ConvertFrom-Json
}
catch {
    throw "'$globalJsonRelativePath' is not valid JSON: $($_.Exception.Message)"
}

$sdkVersion = [string] $globalJson.sdk.version
Assert-Condition `
    -Condition ($sdkVersion -match "^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$") `
    -Message "global.json must pin an exact three-part .NET SDK version."
Assert-Condition `
    -Condition ([string] $globalJson.sdk.rollForward -eq "disable") `
    -Message "global.json must disable SDK roll-forward for reproducible release builds."
Assert-Condition `
    -Condition ($globalJson.sdk.allowPrerelease -eq $false) `
    -Message "global.json must disable prerelease SDK selection."

$verifyWorkflowText =
    Get-RequiredFileText -RelativePath ".github/workflows/verify.yml"
Assert-TextMatch `
    -Text $verifyWorkflowText `
    -Pattern (
        "(?m)^\s*dotnet-version:\s*" +
        [regex]::Escape($sdkVersion) +
        "\s*$"
    ) `
    -Description (
        "the verification workflow must install the exact global.json SDK"
    )

$version = Get-RequiredXmlValue `
    -Document $props `
    -XPath "/Project/PropertyGroup[not(@Condition)]/Version" `
    -Description "unconditional Version property"
$targetSptVersion = Get-RequiredXmlValue `
    -Document $props `
    -XPath "/Project/PropertyGroup[not(@Condition)]/TargetSptVersion" `
    -Description "unconditional TargetSptVersion property"
$solutionName = Get-RequiredXmlValue `
    -Document $props `
    -XPath "/Project/PropertyGroup[not(@Condition)]/SolutionName" `
    -Description "unconditional SolutionName property"
$assemblyVersion = Get-RequiredXmlValue `
    -Document $props `
    -XPath "/Project/PropertyGroup[not(@Condition)]/AssemblyVersion" `
    -Description "unconditional AssemblyVersion property"
$fileVersion = Get-RequiredXmlValue `
    -Document $props `
    -XPath "/Project/PropertyGroup[not(@Condition)]/FileVersion" `
    -Description "unconditional FileVersion property"
$releaseArchiveTemplate = Get-RequiredXmlValue `
    -Document $props `
    -XPath "/Project/PropertyGroup[not(@Condition)]/ReleaseArchive" `
    -Description "unconditional ReleaseArchive property"

Assert-Condition `
    -Condition ($version -match "^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$") `
    -Message "Version '$version' must be a three-part numeric release version."
Assert-Condition `
    -Condition ($targetSptVersion -match "^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$") `
    -Message "TargetSptVersion '$targetSptVersion' must be a three-part numeric version."
Assert-Condition `
    -Condition ($assemblyVersion -eq '$(Version)') `
    -Message "AssemblyVersion must derive from Version; found '$assemblyVersion'."
Assert-Condition `
    -Condition ($fileVersion -eq '$(Version)') `
    -Message "FileVersion must derive from Version; found '$fileVersion'."

$expectedArchiveTemplate =
    '$(DistributionDir)$(SolutionName)-v$(Version)-SPT$(TargetSptVersion).zip'
Assert-Condition `
    -Condition ($releaseArchiveTemplate -eq $expectedArchiveTemplate) `
    -Message (
        "ReleaseArchive must derive from DistributionDir, SolutionName, Version, " +
        "and TargetSptVersion. Found '$releaseArchiveTemplate'."
    )

$releaseArchiveName = $releaseArchiveTemplate.
    Replace('$(DistributionDir)', '').
    Replace('$(SolutionName)', $solutionName).
    Replace('$(Version)', $version).
    Replace('$(TargetSptVersion)', $targetSptVersion)
$expectedArchiveName = "$solutionName-v$version-SPT$targetSptVersion.zip"
Assert-Condition `
    -Condition ($releaseArchiveName -eq $expectedArchiveName) `
    -Message "Derived archive name '$releaseArchiveName' does not equal '$expectedArchiveName'."

$metadataTextValues = @(
    $props.SelectNodes(
        "/Project/Target[@Name='GenerateModMetadata']/ItemGroup/GeneratedText"
    ) | ForEach-Object {
        [string] $_.GetAttribute("Include")
    }
)
Assert-Condition `
    -Condition (
        @(
            $metadataTextValues |
                Where-Object {
                    $_ -like '*public const string VERSION = "$(Version)"*'
                }
        ).Count -eq 1
    ) `
    -Message "GenerateModMetadata must emit VERSION from the Version property exactly once."
Assert-Condition `
    -Condition (
        @(
            $metadataTextValues |
                Where-Object {
                    $_ -like '*public const string TARGET_SPT_VERSION = "$(TargetSptVersion)"*'
                }
        ).Count -eq 1
    ) `
    -Message (
        "GenerateModMetadata must emit TARGET_SPT_VERSION from the " +
        "TargetSptVersion property exactly once."
    )

$corePluginText =
    Get-RequiredFileText -RelativePath "project/SamSWAT.FireSupport/FireSupportPlugin.cs"
Assert-TextMatch `
    -Text $corePluginText `
    -Pattern '\[BepInPlugin\([^\]]*ModMetadata\.VERSION\s*\)\]' `
    -Description "the Core BepInPlugin version must consume ModMetadata.VERSION"

$fikaPluginText =
    Get-RequiredFileText -RelativePath "project/SamSWAT.FireSupport.Fika/FireSupportFikaPlugin.cs"
Assert-TextMatch `
    -Text $fikaPluginText `
    -Pattern '\[BepInPlugin\([^\]]*ModMetadata\.VERSION\s*\)\]' `
    -Description "the Fika BepInPlugin version must consume ModMetadata.VERSION"
Assert-TextMatch `
    -Text $fikaPluginText `
    -Pattern (
        '\[BepInDependency\(\s*"com\.tylevo\.tacticalservicescontrol"\s*,\s*' +
        'ModMetadata\.VERSION\s*\)\]'
    ) `
    -Description "the Fika Core dependency version must consume ModMetadata.VERSION"

$serverModText =
    Get-RequiredFileText -RelativePath "project/SamSWAT.FireSupport.Server/ServerMod.cs"
Assert-TextMatch `
    -Text $serverModText `
    -Pattern (
        'public\s+override\s+Version\s+Version\s*\{[^}]*\}\s*=\s*' +
        'new\s*\(\s*ModMetadata\.VERSION\s*\)\s*;'
    ) `
    -Description "the server mod version must consume ModMetadata.VERSION"

$changelogText = Get-RequiredFileText -RelativePath "CHANGELOG.md"
$changelogHeadings = @(
    [regex]::Matches($changelogText, '(?m)^##\s+(.+?)\s*$')
)
Assert-Condition `
    -Condition ($changelogHeadings.Count -gt 0) `
    -Message "CHANGELOG.md must contain at least one level-two release heading."
$currentChangelogHeading = $changelogHeadings[0].Groups[1].Value.Trim()
$expectedChangelogHeading = "$version - Public Beta"
$allowedChangelogHeadingPattern =
    "^$([regex]::Escape($expectedChangelogHeading))(?: \(unreleased\))?$"
Assert-Condition `
    -Condition ($currentChangelogHeading -match $allowedChangelogHeadingPattern) `
    -Message (
        "The current CHANGELOG heading must be '$expectedChangelogHeading' " +
        "with an optional '(unreleased)' marker; " +
        "found '$currentChangelogHeading'."
    )

$escapedVersion = [regex]::Escape($version)
$escapedTargetSptVersion = [regex]::Escape($targetSptVersion)
$releaseNotesRelativePath = "docs/release-notes-v$version.md"
$releaseNotesText = Get-RequiredFileText -RelativePath $releaseNotesRelativePath
Assert-TextMatch `
    -Text $releaseNotesText `
    -Pattern (
        "(?m)^#\s+Tylevo's Tactical Services Control v$escapedVersion " +
        "Public Beta\s*$"
    ) `
    -Description (
        "'$releaseNotesRelativePath' must have the versioned public-beta title"
    )
Assert-TextMatch `
    -Text $releaseNotesText `
    -Pattern "(?<![0-9])SPT\s+$escapedTargetSptVersion(?![0-9])" `
    -Description (
        "'$releaseNotesRelativePath' must identify target SPT $targetSptVersion"
    )

$forgeDescriptionRelativePath = "docs/forge-description-v$version.md"
$forgeDescriptionText =
    Get-RequiredFileText -RelativePath $forgeDescriptionRelativePath
Assert-TextMatch `
    -Text $forgeDescriptionText `
    -Pattern "Tylevo's Tactical Services Control" `
    -Description (
        "'$forgeDescriptionRelativePath' must identify Tactical Services Control"
    )
Assert-TextMatch `
    -Text $forgeDescriptionText `
    -Pattern "(?<![0-9])v$escapedVersion(?![0-9])" `
    -Description (
        "'$forgeDescriptionRelativePath' must identify release v$version"
    )
Assert-TextMatch `
    -Text $forgeDescriptionText `
    -Pattern "(?<![0-9])SPT\s+$escapedTargetSptVersion(?![0-9])" `
    -Description (
        "'$forgeDescriptionRelativePath' must identify target SPT $targetSptVersion"
    )

Write-Host "Release metadata verification passed."
Write-Host "  Version: $version"
Write-Host "  Target SPT: $targetSptVersion"
Write-Host "  .NET SDK: $sdkVersion"
Write-Host "  Archive: $releaseArchiveName"
