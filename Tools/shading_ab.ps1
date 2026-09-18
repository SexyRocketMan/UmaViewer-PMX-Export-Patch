<#
.SYNOPSIS
Render a model twice - legacy shading and the new face shading - with a morph maxed out, and tile the pairs.

.DESCRIPTION
Both runs use the same camera, the same light and the same morph, so each row of the output is one view with
legacy on the left and the new shading on the right. That is the comparison that decides whether the new face
shading adds anything, and the maxed morph is there to make the artefacts large enough to judge.

.EXAMPLE
./Tools/shading_ab.ps1 -Pmx D:/out/1001_00.pmx -OutDir D:/out/ab -Morph Mouth_WaraiA=1.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Pmx,
    [Parameter(Mandatory = $true)][string]$OutDir,
    [string]$Yaws = "0,45,-45",
    [string]$Azimuths = "0,60,-60",
    [string[]]$Morph = @(),
    [double]$LightEnergy = 2.0,
    [int]$Size = 440,
    [string]$Blender = "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"
)

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$morphArgs = @()
foreach ($entry in $Morph) { $morphArgs += @("--morph", $entry) }

foreach ($mode in @(@("legacy", "--legacy"), @("new", "--new-face"))) {
    $name = $mode[0]
    $flag = $mode[1]
    $arguments = @("--background", "--factory-startup", "--python", "Tools\blender_render_angles.py", "--",
                   "--pmx", $Pmx, "--out-dir", (Join-Path $OutDir $name), "--yaws", $Yaws,
                   "--apply-shader", $flag, "--light-energy", $LightEnergy, "--size", $Size,
                   "--grid", $Azimuths) + $morphArgs
    Write-Host "--- $name"
    & $Blender @arguments 2>&1 | Select-String -Pattern "morph |legacy shading|Nars face|montage" | ForEach-Object { "    $($_.Line)" }
}

# one row per view, legacy on the left and the new shading on the right
$ordered = @()
foreach ($azimuth in $Azimuths.Split(",")) {
    foreach ($yaw in $Yaws.Split(",")) {
        # the renderer names a zero yaw "0" but a zero azimuth "+0" - two different labels for the same value,
        # which is what the comparisons kept tripping over
        $label = if ([double]$yaw -eq 0) { "0" } else { "{0:+0;-0}" -f [double]$yaw }
        $azLabel = "{0:+0;-0;+0}" -f [double]$azimuth
        $ordered += (Join-Path $OutDir "legacy\grid_az$azLabel`_yaw$label.png")
        $ordered += (Join-Path $OutDir "new\grid_az$azLabel`_yaw$label.png")
    }
}
$missing = $ordered | Where-Object { -not (Test-Path $_) }
if ($missing) {
    Write-Host "!! missing $($missing.Count) render(s), first: $($missing[0])"
    exit 1
}
$sheet = Join-Path $OutDir "ab.png"
uv run --with pillow Tools\montage.py $sheet 2 $ordered | ForEach-Object { $_ }
Write-Host ""
Write-Host "legacy | new, one row per view:  $sheet"
