<#
.SYNOPSIS
    Verify exported PMX files with Blender + mmd_tools + uma_addon, headlessly.

.DESCRIPTION
    Imports each PMX, checks the morph-name contract the uma_addon "Refine Structure" operator
    depends on, then runs that operator and checks that the eye bones still deform the mesh.

.EXAMPLE
    ./Tools/verify_export.ps1 -Pmx D:/out/1001_00.pmx
    ./Tools/verify_export.ps1 -Pmx D:/out/1001_00.pmx -Mode unified
    ./Tools/verify_export.ps1 -Pmx a.pmx, b.pmx -Mode blender -Json report.json
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string[]]$Pmx,
    # unified is what the exporter writes by default; the other spellings are for PmxMorphNameMode 0/1/2
    [ValidateSet("unified", "blender", "tagged", "short", "both")][string]$Mode = "unified",
    [string]$Bone = "Eye_L",
    [string]$Json = "",
    [switch]$SkipRefine,
    [string]$BlenderExe = ""
)

$ErrorActionPreference = "Stop"

function Find-BlenderExe {
    $candidates = @()
    $candidates += (Get-ChildItem "C:\Program Files\Blender Foundation" -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending |
        ForEach-Object { Join-Path $_.FullName "blender.exe" })
    $cmd = Get-Command blender -ErrorAction SilentlyContinue
    if ($cmd) { $candidates += $cmd.Source }
    foreach ($candidate in $candidates) { if ($candidate -and (Test-Path $candidate)) { return $candidate } }
    throw "Could not find blender.exe. Pass -BlenderExe explicitly."
}

if (-not $BlenderExe) { $BlenderExe = Find-BlenderExe }

foreach ($file in $Pmx) {
    if (-not (Test-Path $file)) { throw "PMX not found: $file" }
}

$script = Join-Path $PSScriptRoot "blender_verify_pmx.py"
$arguments = @("--background", "--factory-startup", "--python", $script, "--", "--mode", $Mode, "--bone", $Bone)
if ($Json) { $arguments += @("--json", $Json) }
if ($SkipRefine) { $arguments += "--skip-refine" }
$arguments += $Pmx

Write-Host "Blender: $BlenderExe"
Write-Host ""

& $BlenderExe @arguments
exit $LASTEXITCODE
