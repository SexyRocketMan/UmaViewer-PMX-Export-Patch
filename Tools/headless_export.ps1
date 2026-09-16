<#
.SYNOPSIS
    Export a PMX model (or list characters/costumes) from UmaViewer without the GUI.

.DESCRIPTION
    Drives Assets/Editor/UmaHeadlessExport.cs through Unity in batch mode: the viewer scene is
    opened, its normal startup runs in play mode, the requested character/costume is loaded and
    written to a .pmx, exactly like the "Export Model" button does - just without any clicking.

.EXAMPLE
    ./Tools/headless_export.ps1 -Char 1001 -Costume 00 -Out D:/out/1001_00.pmx

.EXAMPLE
    ./Tools/headless_export.ps1 -ListChars
#>
[CmdletBinding()]
param(
    [int]$Char = -1,
    [string]$Costume = "",
    [string]$Out = "",
    [string]$Scene = "",
    [switch]$ListChars,
    [switch]$ListCostumes,
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

if (-not $UnityExe) { $UnityExe = Find-UnityExe -Project $ProjectPath }
if (-not (Test-Path $UnityExe)) { throw "Unity not found at $UnityExe" }

if (-not $ListChars -and -not $ListCostumes -and $Char -lt 0) {
    throw "Pass -Char <id>, or -ListChars / -ListCostumes."
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
if ($Out) { $arguments += @("-umaOut", (New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Out)).FullName + "\" + (Split-Path -Leaf $Out)) }
if ($Scene) { $arguments += @("-umaScene", $Scene) }
if ($ListChars) { $arguments += "-umaListChars" }
if ($ListCostumes) { $arguments += "-umaListCostumes" }

Write-Host "Unity  : $UnityExe"
Write-Host "Project: $ProjectPath"
Write-Host "Log    : $LogPath"
Write-Host ""

& $UnityExe @arguments
$exit = $LASTEXITCODE

if (Test-Path $LogPath) {
    Select-String -Path $LogPath -Pattern "\[UmaHeadlessExport\]" |
        Where-Object { $_.Line -notmatch "StackTraceUtility|DebugLogHandler|Logger:Log|Debug:Log" } |
        ForEach-Object { $_.Line.Trim() }
}
Write-Host ""
Write-Host "Unity exit code: $exit"
exit $exit
