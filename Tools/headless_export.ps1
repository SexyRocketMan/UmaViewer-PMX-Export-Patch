<#
.SYNOPSIS
    Export a PMX model, a VMD motion, or prop/scene info from UmaViewer without the GUI.

.DESCRIPTION
    Drives Assets/Editor/UmaHeadlessExport.cs through Unity in batch mode: the viewer scene is
    opened, its normal startup runs in play mode, then the requested asset is loaded and exported
    exactly like the in-app buttons do - just without any clicking.

.EXAMPLE
    ./Tools/headless_export.ps1 -ListChars
    ./Tools/headless_export.ps1 -Char 1001 -Costume 00 -Out D:/out/1001_00.pmx
    ./Tools/headless_export.ps1 -Char 1001 -Motion anm_rac_type01_run02_stride -RecordVmd D:/out/run.vmd
    ./Tools/headless_export.ps1 -ListProps home10001
    ./Tools/headless_export.ps1 -Prop pfb_env_home10001_main000_000 -DumpMaterials -Variant 214 -Out D:/out/home.pmx
#>
[CmdletBinding()]
param(
    [int]$Char = -1,
    [string]$Costume = "",
    [string]$Out = "",
    [string]$Motion = "",
    [string]$Prop = "",
    [string]$Variant = "",
    [string]$Scene = "",
    [switch]$ListChars,
    [switch]$ListCostumes,
    [switch]$ListProps,
    [string]$ListPropsFilter = "",
    [string]$ScanProps = "",
    [int]$ScanCount = 10,
    [switch]$DumpMaterials,
    [string]$RecordVmd = "",
    [int]$RecordFps = 30,
    [int]$RecordReduction = 0,
    [ValidateSet("deterministic", "realtime")][string]$RecordMode = "deterministic",
    [int]$MorphNameMode = -1,
    [switch]$APose,
    [int]$Timeout = 600,
    [int]$ExtraFrames = 30,
    [string]$ProjectPath = (Split-Path -Parent $PSScriptRoot),
    [string]$UnityExe = "",
    [string]$LogPath = ""
)

$ErrorActionPreference = "Stop"

function Find-UnityExe {
    param([string]$Project)
    $versionFile = Join-Path $Project "ProjectSettings/ProjectVersion.txt"
    $version = $null
    if (Test-Path $versionFile) {
        $match = Select-String -Path $versionFile -Pattern 'm_EditorVersion:\s*(\S+)' | Select-Object -First 1
        if ($match) { $version = $match.Matches[0].Groups[1].Value }
    }
    $candidates = @()
    if ($version) { $candidates += "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe" }
    $candidates += (Get-ChildItem "C:\Program Files\Unity\Hub\Editor" -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending | ForEach-Object { Join-Path $_.FullName "Editor\Unity.exe" })
    foreach ($candidate in $candidates) { if (Test-Path $candidate) { return $candidate } }
    throw "Could not find Unity.exe. Pass -UnityExe explicitly."
}

function Get-ProjectUnityProcesses {
    param([string]$Project)
    Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -and $_.CommandLine -like "*$Project*" }
}

function Clear-StaleUnityLock {
    param([string]$Project)
    $lock = Join-Path $Project "Temp/UnityLockfile"
    if (-not (Test-Path $lock)) { return }
    if (Get-ProjectUnityProcesses -Project $Project) { return }
    Remove-Item $lock -Force -ErrorAction SilentlyContinue
    Write-Host "Removed a stale Unity lockfile left by an earlier run." -ForegroundColor Yellow
}

function Wait-UnityExit {
    <#
      Unity's launcher returns (and the runner writes its result) before the editor process has fully
      released the project. Starting the next run while that is still happening fails with "project
      already open in another instance", so wait for it when chaining exports.
    #>
    param([string]$Project, [int]$Seconds = 120)
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        $running = Get-ProjectUnityProcesses -Project $Project
        $lock = Join-Path $Project "Temp/UnityLockfile"
        if (-not $running -and -not (Test-Path $lock)) { return $true }
        Start-Sleep -Seconds 2
    }
    Write-Host "A Unity process still holds $Project after $Seconds s." -ForegroundColor Yellow
    return $false
}

if (-not $UnityExe) { $UnityExe = Find-UnityExe -Project $ProjectPath }
if (-not (Test-Path $UnityExe)) { throw "Unity not found at $UnityExe" }
Clear-StaleUnityLock -Project $ProjectPath

$doingSomething = $ListChars -or $ListCostumes -or $ListProps -or $ScanProps -or $Char -ge 0 -or $Prop -or $RecordVmd
if (-not $doingSomething) {
    throw "Nothing to do: pass -Char <id>, -Prop <path>, -RecordVmd <path>, or a -List* switch."
}
if (-not $LogPath) {
    $logDir = Join-Path $ProjectPath "Logs"
    New-Item -ItemType Directory -Force -Path $logDir | Out-Null
    $LogPath = Join-Path $logDir ("headless_export_{0:yyyyMMdd_HHmmss}.log" -f (Get-Date))
}

$arguments = @(
    "-batchmode",
    "-projectPath", $ProjectPath,
    "-executeMethod", "UmaHeadlessExport.Run",
    "-umaTimeout", $Timeout,
    "-umaExtraFrames", $ExtraFrames,
    "-logFile", $LogPath
)
if ($Char -ge 0) { $arguments += @("-umaChar", $Char) }
if ($Costume) { $arguments += @("-umaCostume", $Costume) }
if ($Out) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Out) | Out-Null
    $arguments += @("-umaOut", (Join-Path (Resolve-Path (Split-Path -Parent $Out)) (Split-Path -Leaf $Out)))
}
if ($Motion) { $arguments += @("-umaMotion", $Motion) }
if ($Prop) { $arguments += @("-umaProp", $Prop) }
if ($Variant) { $arguments += @("-umaVariant", $Variant) }
if ($Scene) { $arguments += @("-umaScene", $Scene) }
if ($ListChars) { $arguments += "-umaListChars" }
if ($ListCostumes) { $arguments += "-umaListCostumes" }
if ($ListProps) {
    $arguments += "-umaListProps"
    if ($ListPropsFilter) { $arguments += $ListPropsFilter }
}
if ($ScanProps) {
    $arguments += @("-umaScanProps", $ScanProps, "-umaScanCount", $ScanCount)
}
if ($DumpMaterials) { $arguments += "-umaDumpMaterials" }
if ($RecordVmd) { $arguments += @("-umaRecordVmd", $RecordVmd) }
if ($RecordFps -gt 0) { $arguments += @("-umaRecordFps", $RecordFps) }
if ($RecordReduction -gt 0) { $arguments += @("-umaRecordReduction", $RecordReduction) }
if ($RecordMode) { $arguments += @("-umaRecordMode", $RecordMode) }
if ($MorphNameMode -ge 0) { $arguments += @("-umaMorphNameMode", $MorphNameMode) }
if ($APose) { $arguments += "-umaAPose" }

Write-Host "Unity  : $UnityExe"
Write-Host "Project: $ProjectPath"
Write-Host "Log    : $LogPath"
Write-Host ""

& $UnityExe @arguments | Out-Null
$launcherExit = $LASTEXITCODE

# Unity's launcher process can exit before the editor it spawned has finished, so the process exit
# code is not proof of anything: wait for the runner's own result marker in the log instead.
$marker = $null
$deadline = (Get-Date).AddSeconds($Timeout + 120)
while ((Get-Date) -lt $deadline) {
    if (Test-Path $LogPath) {
        $marker = Select-String -Path $LogPath -Pattern "\[UmaHeadlessExport\] (OK|FAILED)" -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($marker) { break }
    }
    Start-Sleep -Seconds 2
}

if (Test-Path $LogPath) {
    Select-String -Path $LogPath -Pattern "\[UmaHeadlessExport\]|\[VMD\]|\[UmaEnvTextureSet\]|\[UISettingsModel\]" |
        Where-Object { $_.Line -notmatch "StackTraceUtility|DebugLogHandler|Logger:Log|Debug:Log" } |
        ForEach-Object { $_.Line.Trim() }
}

Write-Host ""
if (-not $marker) {
    Write-Host "No result marker in the log (timeout after $($Timeout + 120)s, launcher exit $launcherExit)." -ForegroundColor Red
    Write-Host "See $LogPath" -ForegroundColor Red
    exit 1
}

if ($marker.Line -match "FAILED") {
    Write-Host "The export FAILED, see the log above and $LogPath" -ForegroundColor Red
    exit 1
}

# leave the project free for the next run in a chain (run_workflow.ps1 starts several)
[void](Wait-UnityExit -Project $ProjectPath)
exit 0
