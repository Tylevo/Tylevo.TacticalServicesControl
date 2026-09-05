# Shared by the source-only checker and supported packager. The manifest pins
# bytes; this fixed inventory limits dependency exceptions to reviewed paths.
function Get-BundledDependencyContract {
    param([Parameter(Mandatory)][object] $Manifest)

    $pluginRoot = 'BepInEx/plugins/UnityToolkit'
    $patcherRoot = 'BepInEx/patchers/UnityToolkit'
    $expectedPaths = @(
        "$patcherRoot/System.Runtime.CompilerServices.Unsafe.dll",
        "$patcherRoot/UnityToolkit-Prepatcher.dll",
        "$pluginRoot/Assemblies.jsonc",
        "$pluginRoot/UniTask.Addressables.dll",
        "$pluginRoot/UniTask.dll",
        "$pluginRoot/UniTask.DOTween.dll",
        "$pluginRoot/UniTask.Linq.dll",
        "$pluginRoot/UniTask.TextMeshPro.dll",
        "$pluginRoot/Unity.Collections.dll",
        "$pluginRoot/UnityToolkit.dll",
        "$pluginRoot/VContainer.dll",
        "$pluginRoot/ZLinq.dll",
        "$pluginRoot/ZLinq.Unity.dll",
        "$pluginRoot/ZLinq.Unity.UnityCollectoins.dll",
        "$pluginRoot/ZString.dll"
    )
    $expectedRoots = @(
        'BepInEx/plugins/Tylevo.TacticalServicesControl',
        'SPT_Runtime/user/mods/Tylevo.TacticalServicesControl',
        $pluginRoot, $patcherRoot
    )
    if (@($Manifest.installRoots).Count -ne $expectedRoots.Count -or
        @($Manifest.installRoots | Where-Object { $expectedRoots -cnotcontains $_ }).Count -ne 0 -or
        @($Manifest.installRoots | Select-Object -Unique).Count -ne $expectedRoots.Count) {
        throw 'Install roots must be exactly the two TSC and two UnityToolkit directories.'
    }
    $dependencies = @($Manifest.bundledDependencies)
    if ($dependencies.Count -ne 1 -or $dependencies[0].id -cne 'unitytoolkit') {
        throw 'Exactly one reviewed UnityToolkit bundled dependency is supported.'
    }
    $dependency = $dependencies[0]
    if ($dependency.version -cne '2.0.1' -or
        $dependency.sourceCommit -notmatch '^[0-9a-f]{40}$' -or
        $dependency.upstreamArchive.fileName -cne 'UnityToolkit-v2.0.1.7z' -or
        [long] $dependency.upstreamArchive.length -le 0 -or
        $dependency.upstreamArchive.sha256 -cnotmatch '^[0-9A-F]{64}$' -or
        $dependency.compatibilityPatch.source -cne 'tools/dependencies/unitytoolkit/UnityToolkit-v2.0.1-SPT4.1-compat.patch' -or
        $dependency.compatibilityPatch.sha256 -cnotmatch '^[0-9A-F]{64}$') {
        throw 'UnityToolkit must declare its version, source commit, upstream archive and pinned compatibility patch.'
    }
    $files = @($dependency.files)
    if ($files.Count -ne $expectedPaths.Count) { throw 'UnityToolkit requires the exact 15-file upstream/overlay inventory.' }
    $records = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $files) {
        $path = [string] $file.path
        if ($expectedPaths -cnotcontains $path -or $records.ContainsKey($path)) {
            throw "Unknown or duplicate bundled dependency path: '$path'."
        }
        if ([long] $file.length -le 0 -or $file.sha256 -cnotmatch '^[0-9A-F]{64}$' -or
            [long] $file.upstreamLength -le 0 -or $file.upstreamSha256 -cnotmatch '^[0-9A-F]{64}$') {
            throw "Bundled dependency '$path' requires positive lengths and exact SHA-256 pins."
        }
        $isOverlay = $path -ceq "$pluginRoot/UnityToolkit.dll" -or $path -ceq "$patcherRoot/UnityToolkit-Prepatcher.dll"
        if ($isOverlay) {
            if ($file.origin -cne 'spt41-overlay') { throw "Missing compatibility-overlay provenance for '$path'." }
        } elseif ($file.origin -cne 'upstream-release' -or
            $file.sha256 -cne $file.upstreamSha256 -or [long] $file.length -ne [long] $file.upstreamLength) {
            throw "Toolkit companion '$path' must remain byte-identical to its upstream pin."
        }
        $records.Add($path, $file)
    }
    $referencePin = @($Manifest.criticalReferencePins | Where-Object fileName -CEQ 'UnityToolkit.dll')
    if ($referencePin.Count -ne 1 -or $referencePin[0].sha256 -cne $records["$pluginRoot/UnityToolkit.dll"].sha256) {
        throw 'Bundled UnityToolkit plugin must match the critical compile-reference pin.'
    }
    if ([int] $Manifest.exactCounts.builtDlls -ne 4 -or [int] $Manifest.exactCounts.bundledDlls -ne 14 -or
        [int] $Manifest.exactCounts.'.dll' -ne 18) {
        throw 'DLL counts must distinguish four built TSC DLLs and fourteen bundled Toolkit DLLs (eighteen total).'
    }
    foreach ($root in @($pluginRoot, $patcherRoot)) {
        $notice = @($Manifest.copiedFiles | Where-Object {
            $_.source -ceq 'tools/dependencies/unitytoolkit/THIRD_PARTY_NOTICES.txt' -and
            $_.destination -ceq "$root/THIRD_PARTY_NOTICES.txt"
        })
        if ($notice.Count -ne 1) { throw "Toolkit license notice must be copied into '$root'." }
    }
    return ,$records
}

function Assert-DependencyPathAncestors {
    param([Parameter(Mandatory)][string] $Path)
    $cursor = [IO.Path]::GetFullPath($Path)
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            if (((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Dependency path or ancestor is a reparse point: '$cursor'."
            }
        }
        $cursor = Split-Path -Path $cursor -Parent
    }
}

function Assert-BundledDependencyStream {
    param(
        [Parameter(Mandatory)][IO.Stream] $Stream,
        [Parameter(Mandatory)][long] $Length,
        [Parameter(Mandatory)][object] $Record
    )
    if ($Length -ne [long] $Record.length) { throw "Bundled dependency byte-length mismatch: '$($Record.path)'." }
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        $hash = ([BitConverter]::ToString($algorithm.ComputeHash($Stream))).Replace('-', '')
        if ($hash -cne $Record.sha256) { throw "Bundled dependency SHA-256 mismatch: '$($Record.path)'." }
    } finally { $algorithm.Dispose() }
}

function Assert-BundledDependencyFile {
    param([Parameter(Mandatory)][string] $Path, [Parameter(Mandatory)][object] $Record)
    Assert-DependencyPathAncestors -Path $Path
    $stream = [IO.File]::OpenRead($Path)
    try { Assert-BundledDependencyStream -Stream $stream -Length $stream.Length -Record $Record }
    finally { $stream.Dispose() }
}

function Get-VerifiedBundledDependencyFiles {
    param(
        [Parameter(Mandatory)][string] $Root,
        [Parameter(Mandatory)][Collections.Generic.Dictionary[string, object]] $Contract
    )
    $resolvedRoot = (Resolve-Path -LiteralPath $Root).Path.TrimEnd('\', '/')
    if (-not (Test-Path -LiteralPath $resolvedRoot -PathType Container)) { throw 'Dependency input must be one directory.' }
    Assert-DependencyPathAncestors -Path $resolvedRoot
    $pending = [Collections.Generic.Queue[string]]::new()
    $pending.Enqueue($resolvedRoot)
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $verified = [Collections.Generic.List[object]]::new()
    while ($pending.Count -gt 0) {
        foreach ($item in Get-ChildItem -LiteralPath $pending.Dequeue() -Force) {
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'Dependency input must not contain symlinks or junctions.'
            }
            $relative = $item.FullName.Substring($resolvedRoot.Length + 1).Replace('\', '/')
            if ($item.PSIsContainer) {
                $prefix = $relative + '/'
                if (@($Contract.Keys | Where-Object { $_.StartsWith($prefix, [StringComparison]::Ordinal) }).Count -eq 0) {
                    throw "Unexpected dependency directory: '$relative'."
                }
                $pending.Enqueue($item.FullName)
                continue
            }
            if (-not $Contract.ContainsKey($relative) -or $Contract[$relative].path -cne $relative -or -not $seen.Add($relative)) {
                throw "Unexpected, duplicate or incorrectly cased dependency file: '$relative'."
            }
            Assert-BundledDependencyFile -Path $item.FullName -Record $Contract[$relative]
            $verified.Add([pscustomobject]@{ Source=$item.FullName; Destination=$relative; Pin=$Contract[$relative] })
        }
    }
    if ($seen.Count -ne $Contract.Count) { throw 'Dependency directory is missing one or more of the exact 15 pinned files.' }
    return $verified.ToArray()
}
