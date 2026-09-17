<#
.SYNOPSIS
    Export a model and a motion from UmaViewer, import them in Blender and render a coloured clip.

.DESCRIPTION
    The whole chain in one call, each step verified:
      1. headless PMX export        (Unity + UmaHeadlessExport)
      2. headless VMD recording     (one clean loop of the loaded motion)
      3. checks                     (morph names fit a vmd, motion actually moves, loop closes)
      4. Blender render             (mmd_tools import, camera + 3 point light, EEVEE)
      5. ffmpeg encode              (mp4)
      6. render checks              (frame is not black/white/empty, motion plays, loop closes)
    Any failing step stops the script with a non zero exit code.

.EXAMPLE
    ./Tools/run_workflow.ps1 -Char 1001 -Costume 00 -Motion anm_rac_type01_run02_stride -View full

.EXAMPLE
    ./Tools/run_workflow.ps1 -Char 1002 -Costume 00 -MorphNameMode 0 -Name ogcompat

.EXAMPLE
    # model exported with the arms already in the A-pose, so the render poses nothing by hand
    ./Tools/run_workflow.ps1 -Char 1001 -Costume 00 -Motion anm_rac_type01_run02_stride -APose -Name apose

.EXAMPLE
    # plain MMD materials, for a model that is meant to be used without the uma addon
    ./Tools/run_workflow.ps1 -Char 1001 -Costume 00 -PlainMaterials -Name plain
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$Char,
    [string]$Costume = "00",
    [string]$Motion = "",
    [ValidateSet(-1, 0, 1, 2, 3)][int]$MorphNameMode = 3,
    [switch]$APose,
    [switch]$PlainMaterials,
    [int]$Fps = 30,
    [ValidateSet("upper", "head", "full")][string]$View = "full",
    [int]$Width = 640,
    [int]$Height = 640,
    [int]$Samples = 24,
    [ValidateSet("eevee", "workbench", "cycles")][string]$Engine = "eevee",
    [string]$OutDir = "",
    [string]$Name = "",
    [switch]$SkipRender,
    [string]$BlenderExe = ""
)

$ErrorActionPreference = "Stop"
$tools = $PSScriptRoot

function Find-BlenderExe {
    if ($BlenderExe) { return $BlenderExe }
    $candidates = @()
    $candidates += (Get-ChildItem "C:\Program Files\Blender Foundation" -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending | ForEach-Object { Join-Path $_.FullName "blender.exe" })
    $cmd = Get-Command blender -ErrorAction SilentlyContinue
    if ($cmd) { $candidates += $cmd.Source }
    foreach ($candidate in $candidates) { if ($candidate -and (Test-Path $candidate)) { return $candidate } }
    throw "Could not find blender.exe. Pass -BlenderExe explicitly."
}

function Invoke-Step {
    param([string]$Title, [scriptblock]$Body)
    Write-Host ""
    Write-Host "=== $Title ===" -ForegroundColor Cyan
    & $Body
    if ($LASTEXITCODE -ne 0 -and $null -ne $LASTEXITCODE) {
        throw "$Title failed (exit $LASTEXITCODE)"
    }
}

$stem = if ($Name) { $Name } else { "${Char}_${Costume}" }
if (-not $OutDir) { $OutDir = Join-Path (Split-Path -Parent $tools) "HeadlessExports" }
$OutDir = (New-Item -ItemType Directory -Force -Path $OutDir).FullName

$pmx = Join-Path $OutDir "$stem.pmx"
$vmd = Join-Path $OutDir "$stem.vmd"
$framesDir = Join-Path $OutDir "${stem}_frames"
$still = Join-Path $OutDir "${stem}_still.png"
$video = Join-Path $OutDir "$stem.mp4"

Write-Host "Character : $Char/$Costume  motion: $(if ($Motion) { $Motion } else { '<default idle>' })"
Write-Host "Naming    : mode $MorphNameMode"
Write-Host "Rest pose : $(if ($APose) { 'A-pose (arms 38.5 degrees down)' } else { 'T-pose' })"
Write-Host "Materials : $(if ($PlainMaterials) { 'plain MMD (no uma settings, usable without the addon)' } else { 'with the uma shader settings in the comment' })"
Write-Host "Output    : $OutDir"

# ---------------------------------------------------------------- 1. model
Invoke-Step "1/6 export model" {
    $exportArgs = @{ Char = $Char; Costume = $Costume; Out = $pmx; MorphNameMode = $MorphNameMode
                     Motion = $Motion; Timeout = 900 }
    if ($APose) { $exportArgs.APose = $true }
    if ($PlainMaterials) { $exportArgs.PlainMaterials = $true }
    & (Join-Path $tools "headless_export.ps1") @exportArgs
}
if (-not (Test-Path $pmx)) { throw "no pmx at $pmx" }

# ---------------------------------------------------------------- 2. motion
Invoke-Step "2/6 record motion" {
    & (Join-Path $tools "headless_export.ps1") -Char $Char -Costume $Costume -RecordVmd $vmd `
        -RecordFps $Fps -MorphNameMode $MorphNameMode -Motion $Motion -Timeout 900
}
if (-not (Test-Path $vmd)) { throw "no vmd at $vmd" }

# ---------------------------------------------------------------- 3. checks
Invoke-Step "3/6 check the export" {
    $require = if ($MorphNameMode -eq 3) { "Eye_XRange_L,Eye_XRange_R,Eye_YRange_L,Eye_YRange_R" } else { "" }
    if ($require) {
        uv run --script (Join-Path $tools "pmx_inspect.py") names $pmx --require $require
        if ($LASTEXITCODE -ne 0) { throw "morph names do not fit a vmd" }
    } else {
        uv run --script (Join-Path $tools "pmx_inspect.py") names $pmx
    }
    uv run --script (Join-Path $tools "vmd_inspect.py") motion $vmd
    if ($LASTEXITCODE -ne 0) { throw "the recorded motion does not move" }
    uv run --script (Join-Path $tools "vmd_inspect.py") loop $vmd
    if ($LASTEXITCODE -ne 0) { throw "the recorded motion does not loop" }
}

if ($SkipRender) {
    Write-Host ""
    Write-Host "Skipping the render. Model: $pmx  Motion: $vmd" -ForegroundColor Yellow
    exit 0
}

# ---------------------------------------------------------------- 4. render
$blender = Find-BlenderExe
Invoke-Step "4/6 render frames" {
    # A model exported in the A-pose already has the rest pose the motion is relative to, so nothing may
    # be posed here and the vmd must be imported without "Treat Current Pose as Rest Pose" - that is the
    # whole point of the export option.
    $aposeDegrees = if ($APose) { 0 } else { 38.5 }
    $usePoseMode = if ($APose) { 0 } else { 1 }
    Write-Host "   arms posed in blender by $aposeDegrees degrees, use_pose_mode=$usePoseMode"
    & $blender --background --factory-startup --python (Join-Path $tools "blender_render_motion.py") -- `
        --pmx $pmx --vmd $vmd --frames-dir $framesDir --still $still --view $View `
        --engine $Engine --width $Width --height $Height --samples $Samples --fps $Fps `
        --apose $aposeDegrees --use-pose-mode $usePoseMode
}
$rendered = @(Get-ChildItem $framesDir -Filter *.png -ErrorAction SilentlyContinue)
if ($rendered.Count -eq 0) { throw "no frames were rendered into $framesDir" }

# ---------------------------------------------------------------- 5. encode
$ffmpeg = (Get-Command ffmpeg -ErrorAction SilentlyContinue).Source
Invoke-Step "5/6 encode video ($($rendered.Count) frames)" {
    if (-not $ffmpeg) {
        Write-Host "ffmpeg not found on PATH - skipping the encode, frames are in $framesDir" -ForegroundColor Yellow
        $global:LASTEXITCODE = 0
        return
    }
    & $ffmpeg -y -loglevel error -framerate $Fps -i (Join-Path $framesDir "frame_%04d.png") `
        -c:v libx264 -pix_fmt yuv420p -crf 18 $video
}

# ---------------------------------------------------------------- 6. verify
Invoke-Step "6/6 check the render" {
    uv run --script (Join-Path $tools "render_stats.py") frames $framesDir
}
if ($LASTEXITCODE -ne 0) { throw "the rendered frames did not pass the checks" }

Write-Host ""
Write-Host "=== done ===" -ForegroundColor Green
Write-Host " model  : $pmx"
Write-Host " motion : $vmd"
if (Test-Path $video) { Write-Host " video  : $video" }
Write-Host " frames : $framesDir ($($rendered.Count) png)"
Write-Host " still  : $still"
