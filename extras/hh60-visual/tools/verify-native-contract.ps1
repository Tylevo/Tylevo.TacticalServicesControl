[CmdletBinding(DefaultParameterSetName = 'Candidate')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Candidate')][string] $CompanionDll,
    [Parameter(Mandatory = $true, ParameterSetName = 'AuditCore')][switch] $AuditCore,
    [Parameter(Mandatory = $true)][string] $CoreDll,
    [Parameter(Mandatory = $true)][string] $GameRoot,
    [string] $Report
)

# Metadata inspection only. Does not load these assemblies for execution, patch
# a process, emit projectiles, or modify the game. Keep reports outside the repo.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$companionPath = if ($AuditCore) { $null } else { (Resolve-Path -LiteralPath $CompanionDll).Path }
$corePath = (Resolve-Path -LiteralPath $CoreDll).Path
$gamePath = (Resolve-Path -LiteralPath $GameRoot).Path
$managed = Join-Path $gamePath 'EscapeFromTarkov_Data/Managed'
$gameAssemblyPath = Join-Path $managed 'Assembly-CSharp.dll'
$cecilPath = Join-Path $gamePath 'BepInEx/core/Mono.Cecil.dll'
foreach ($path in @($companionPath, $corePath, $gameAssemblyPath, $cecilPath) | Where-Object { $_ }) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required local assembly is missing: $path" }
}
Add-Type -Path $cecilPath
$resolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
foreach ($path in @($managed, (Join-Path $gamePath 'BepInEx/core'),
        (Join-Path $gamePath 'BepInEx/plugins/UnityToolkit'),
        $(if ($companionPath) { Split-Path $companionPath }), (Split-Path $corePath)) | Where-Object { $_ }) {
    $resolver.AddSearchDirectory($path)
}
$reader = [Mono.Cecil.ReaderParameters]::new()
$reader.AssemblyResolver = $resolver
$assemblies = [Collections.Generic.List[IDisposable]]::new()
$checks = [Collections.Generic.List[string]]::new()

function Require([bool] $Condition, [string] $Name) {
    if (-not $Condition) { throw "Native contract failed after $($checks.Count) checks: $Name" }
    $checks.Add($Name)
}
function Read-Assembly([string] $Path) {
    $assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($Path, $reader)
    $assemblies.Add($assembly)
    return $assembly
}
function Get-Type($Assembly, [string] $Name) {
    $type = $Assembly.MainModule.GetType($Name)
    Require ($null -ne $type) "Type exists: $Name"
    return $type
}
function Get-UniqueMethod($Type, [string] $Name) {
    $methods = @($Type.Methods | Where-Object Name -eq $Name)
    Require ($methods.Count -eq 1) "Unique method: $($Type.FullName).$Name"
    return $methods[0]
}
function Get-ExactMethod($Type, [string] $Name, [string] $ReturnType, [string[]] $ParameterTypes) {
    $signature = $ParameterTypes -join '|'
    $methods = @($Type.Methods | Where-Object {
        $_.Name -eq $Name -and $_.ReturnType.FullName -eq $ReturnType -and
        (($_.Parameters | ForEach-Object { $_.ParameterType.FullName }) -join '|') -eq $signature
    })
    Require ($methods.Count -eq 1) "Exact signature: $($Type.FullName).$Name($($ParameterTypes -join ', ')) -> $ReturnType"
    return $methods[0]
}
function Get-Field($Type, [string] $Name, [string] $TypeName) {
    $fields = @($Type.Fields | Where-Object Name -eq $Name)
    Require ($fields.Count -eq 1) "Unique request field: $Name"
    if ($TypeName) { Require ($fields[0].FieldType.FullName -eq $TypeName) "Request field type: $Name -> $TypeName" }
    return $fields[0]
}
function Test-Calls($Method, [string] $Name, [string] $DeclaringType = '') {
    return @($Method.Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq $Name -and
        (-not $DeclaringType -or $_.Operand.DeclaringType.FullName -eq $DeclaringType)
    }).Count -gt 0
}
function Get-Strings($Method) {
    return @($Method.Body.Instructions | Where-Object { $_.OpCode.Name -eq 'ldstr' } | ForEach-Object Operand)
}
function Test-Injectable($Original, $Patch) {
    Require $Patch.IsStatic "Static Harmony callback: $($Patch.Name)"
    foreach ($argument in $Patch.Parameters) {
        if ($argument.Name -eq '__instance') {
            Require (-not $Original.IsStatic -and $argument.ParameterType.FullName -eq 'UnityEngine.Component') "Component instance callback: $($Patch.Name)"
        } elseif ($argument.Name -eq '__result') {
            Require ($argument.ParameterType.FullName -eq $Original.ReturnType.FullName) "Return callback: $($Patch.Name)"
        } else {
            $matching = @($Original.Parameters | Where-Object Name -eq $argument.Name)
            Require ($matching.Count -eq 1 -and $matching[0].ParameterType.FullName -eq $argument.ParameterType.FullName) "Named native argument: $($Patch.Name).$($argument.Name)"
        }
    }
    Require ($Patch.Body.ExceptionHandlers.Count -gt 0) "Optional callback contains errors: $($Patch.Name)"
}

try {
    $candidate = if ($AuditCore) { $null } else { Read-Assembly $companionPath }
    $plugin = $null
    $version = $null
    $core = Read-Assembly $corePath
    $game = Read-Assembly $gameAssemblyPath
    $aircraft = Get-Type $core 'SamSWAT.FireSupport.ArysReloaded.Unity.UH60Behaviour'
    if (-not $AuditCore) {
        $plugin = Get-Type $candidate 'TscHh60Visual.Plugin'
        $metadata = @($plugin.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'BepInEx.BepInPlugin' })
        Require ($metadata.Count -eq 1) 'One BepInEx plugin identity'
        Require ($metadata[0].ConstructorArguments[0].Value -eq 'local.tsc.hh60visual') 'Existing companion config/plugin identity is preserved'
        $version = [version]($metadata[0].ConstructorArguments[2].Value)
        Require ($candidate.Name.Version.Major -eq $version.Major -and $candidate.Name.Version.Minor -eq $version.Minor -and
            $candidate.Name.Version.Build -eq $version.Build) 'Assembly and BepInEx versions agree'
    }

    $support = Get-Field $aircraft '_requestSupportType' ''
    $supportType = $support.FieldType.Resolve()
    Require ($supportType.IsEnum -and @($supportType.Fields | Where-Object Name -eq 'Extract').Count -eq 1) 'Native support enum has Extract'
    $null = Get-Field $aircraft '_allowLocalServicePoint' 'System.Boolean'
    $null = Get-Field $aircraft '_cancellationToken' 'System.Threading.CancellationToken'
    $timing = Get-Field $aircraft '_timingSnapshot' ''
    $timingType = $timing.FieldType.Resolve()
    $speed = @($timingType.Properties | Where-Object Name -eq 'SpeedMultiplier')
    Require ($speed.Count -eq 1 -and $speed[0].PropertyType.FullName -eq 'System.Single' -and
        $null -ne $speed[0].GetMethod -and -not $speed[0].GetMethod.IsStatic) 'Timing snapshot exposes instance float SpeedMultiplier'

    $request = Get-ExactMethod $aircraft 'ProcessRequest' 'System.Void' @('UnityEngine.Vector3','UnityEngine.Vector3','UnityEngine.Vector3','System.Threading.CancellationToken','System.Boolean','System.Int32','System.Int32')
    $create = Get-ExactMethod $aircraft 'CreateExtractionPoint' 'UnityEngine.GameObject' @($timing.FieldType.FullName,'System.Threading.CancellationToken')
    $destroy = Get-ExactMethod $aircraft 'DestroyLandingPoint' 'System.Void' @('UnityEngine.GameObject')
    $cancel = Get-ExactMethod $aircraft 'CancelRequestLifetime' 'System.Void' @()
    $null = Get-ExactMethod $aircraft 'OnAwake' 'System.Void' @()
    Require ($request.Parameters[4].Name -eq 'visualOnly') 'Native request names the visualOnly callback argument'
    Require ($create.Parameters[1].Name -eq 'cancellationToken') 'Native arrival names the cancellationToken callback argument'
    Require ($destroy.Parameters[0].Name -eq 'landingPoint') 'Native cleanup names the landingPoint callback argument'

    $callbacks = @{
        BeginInboundExtractionCover = $request
        BeginExtractionCoverWindow = $create
        EndExtractionCoverWindow = $destroy
        CancelExtractionCoverRequest = $cancel
    }
    if (-not $AuditCore) {
        foreach ($name in $callbacks.Keys | Sort-Object) {
            Test-Injectable $callbacks[$name] (Get-UniqueMethod $plugin $name)
        }
    }
    Require (Test-Calls $request 'CancelRequestLifetime') 'Native new request retires its old lifetime'
    $tokenAssignments = @($request.Body.Instructions | Where-Object {
        $_.OpCode.Name -eq 'stfld' -and $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq '_cancellationToken'
    })
    Require ($tokenAssignments.Count -eq 1) 'Native ProcessRequest assigns its linked cancellation token before postfix completion'
    foreach ($name in @('OnDisable','OnDestroy','ReturnToPoolSafely')) {
        Require (Test-Calls (Get-UniqueMethod $aircraft $name) 'CancelRequestLifetime') "Native $name retires request lifetime"
    }
    $destroyCurrent = Get-ExactMethod $aircraft 'DestroyLandingPoint' 'System.Void' @()
    Require (Test-Calls $destroyCurrent 'DestroyLandingPoint') 'Parameterless cleanup routes through the patched landing-point overload'

    if (-not $AuditCore) {
        $hook = Get-UniqueMethod $plugin 'HookExtractionCover'
        $registrations = @($hook.Body.Instructions | Where-Object {
            $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq 'Patch' -and $_.Operand.DeclaringType.FullName -eq 'HarmonyLib.Harmony'
        })
        Require ($registrations.Count -eq 4) 'Exactly four compiled extraction-cover Harmony registrations'
        $lookups = Get-Strings $hook
        foreach ($name in @('ProcessRequest','CreateExtractionPoint','DestroyLandingPoint','CancelRequestLifetime',
                '_requestSupportType','_allowLocalServicePoint','_cancellationToken','_timingSnapshot','SpeedMultiplier') + @($callbacks.Keys)) {
            Require ($lookups -contains $name) "Compiled hook lookup: $name"
        }
        $awake = Get-UniqueMethod $plugin 'Awake'
        Require (Test-Calls $awake 'BindExtractionCover') 'Plugin binds extraction settings'
        Require (Test-Calls $awake 'HookExtractionCover') 'Plugin installs extraction hooks'
        Require (Test-Calls (Get-UniqueMethod $plugin 'Update') 'TickExtractionCover') 'Plugin updates cover runtime'
        Require (Test-Calls (Get-UniqueMethod $plugin 'OnDestroy') 'ShutdownExtractionCover') 'Plugin shutdown retires cover runtime'
        $window = Get-UniqueMethod $plugin 'get_CoverWindowActive'
        Require (Test-Calls $window 'get_IsCancellationRequested') 'Cover window checks native request cancellation'
        Require (Test-Calls $window 'get_activeInHierarchy') 'Cover window checks active scene ownership'
        foreach ($name in @('BeginInboundExtractionCover','CancelExtractionCoverRequest','EndExtractionCoverWindow')) {
            Require (Test-Calls (Get-UniqueMethod $plugin $name) 'ShutdownExtractionCover') "$name retires previous cover resources"
        }
        $tick = Get-UniqueMethod $plugin 'TickExtractionCover'
        foreach ($name in @('TryGetBenchRaid','TryGetFreeShot','Fire','PlayMuzzleFlash','PlayGunshot')) {
            Require (Test-Calls $tick $name) "Cover calls existing $name contract"
        }
        $raid = Get-UniqueMethod $plugin 'TryGetBenchRaid'
        Require ((Get-Strings $raid) -contains 'com.fika.core') 'Fika restriction remains compiled'
        Require (Test-Calls $raid 'get_IsAI') 'Solo raid guard still distinguishes human actors'
        Require (Test-Calls $raid 'get_IsAlive') 'Solo raid guard still requires a living caller'

    }

    # Resolve compiled external API shapes used for native emission and prediction.
    # These checks do not establish native damage semantics or execute live shots.
    $calculator = Get-Type $game 'EFT.Ballistics.BallisticsCalculator'
    $null = Get-ExactMethod $calculator 'CreateShot' 'EFT.Ballistics.Shot' @('EFT.InventoryLogic.Ammo','UnityEngine.Vector3','UnityEngine.Vector3','System.Int32','System.String','EFT.InventoryLogic.Item','System.Single','System.Int32')
    $null = Get-ExactMethod $calculator 'Shoot' 'System.Void' @('EFT.Ballistics.Shot')
    $trajectory = Get-Type $game 'EFT.Ballistics.TrajectoryCalculator'
    $null = Get-ExactMethod $trajectory 'Initialize' 'System.Void' @('UnityEngine.Vector3','UnityEngine.Vector3','System.Single','System.Single','System.Single','System.Boolean')
    $null = Get-ExactMethod $trajectory 'Next' 'EFT.Ballistics.TrajectoryInfo' @()
    $null = Get-ExactMethod $trajectory 'ClearClass' 'System.Void' @()
    $shot = Get-Type $game 'EFT.Ballistics.Shot'
    $release = Get-ExactMethod $shot 'Release' 'System.Void' @('EFT.Ballistics.Shot')
    Require $release.IsStatic 'Native Shot.Release remains static'
    $world = Get-Type $game 'EFT.GameWorld'
    $null = Get-ExactMethod $world 'ShotDelegate' 'System.Void' @('EFT.Ballistics.Shot')
    $null = Get-ExactMethod $world 'GetEverExistedBridgeByProfileID' 'EFT.IObserverToPlayerBridge' @('System.String')

    $coreHash = (Get-FileHash -LiteralPath $corePath -Algorithm SHA256).Hash
    if (-not $AuditCore) {
        $pin = Get-Field $plugin 'ExpectedTscSha256' 'System.String'
        Require ($pin.HasConstant -and $pin.Constant -eq $coreHash) 'Selected compiled Core matches the companion compatibility hash pin'
    }
    $result = [ordered]@{
        scope = 'Compiled metadata and call-site contracts only; no Harmony execution, native physics, rendering, audio, input, damage, or live extraction acceptance.'
        mode = if ($AuditCore) { 'CoreAudit' } else { 'CandidateWithExactCorePin' }
        companionVersion = if ($version) { $version.ToString() } else { $null }
        companionSha256 = if ($companionPath) { (Get-FileHash -LiteralPath $companionPath -Algorithm SHA256).Hash } else { $null }
        coreSha256 = $coreHash
        gameAssemblySha256 = (Get-FileHash -LiteralPath $gameAssemblyPath -Algorithm SHA256).Hash
        passed = $checks.Count
        checks = @($checks.ToArray())
    }
    $json = $result | ConvertTo-Json -Depth 5
    if ($Report) {
        $reportPath = [IO.Path]::GetFullPath($Report)
        if (@($companionPath, $corePath, $gameAssemblyPath, $cecilPath) -contains $reportPath) { throw 'Report must not overwrite an input assembly.' }
        $json | Set-Content -LiteralPath $reportPath -Encoding UTF8
        Write-Output "PASS: $($checks.Count) compiled HH60 native contract checks. Report: $reportPath"
    } else { Write-Output $json }
} finally {
    foreach ($assembly in $assemblies) { $assembly.Dispose() }
    $resolver.Dispose()
}
