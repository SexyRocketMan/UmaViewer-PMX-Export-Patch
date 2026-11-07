<#
.SYNOPSIS
    Checks that the viewer binds the keys in Config.json, and that each of them is suspended in the modes that
    own the keyboard.

.DESCRIPTION
    3.5 shipped rebindable keybinds that did nothing. The shortcuts were built in one component's Awake and read
    the config through `Config.Instance`, which another component's Awake had not created yet, so they silently
    bound the built-in defaults - and the startup log still printed a plausible binding list, because the list it
    printed came from those same defaults. Every manual check compared that log to the shipped Config.json, which
    happens to equal the defaults, so nothing anywhere disagreed.

    This is that comparison, as a command. It writes a probe Config.json beside the player (rebound keys, plus one
    action the build does not have and one key Unity does not have), runs the player with UMA_SHORTCUT_PROBE=1,
    and asserts on the log:

      * the bound keys are the ones in the file - not the built-in defaults;
      * an unknown action name, and an unknown key name, are both reported rather than dropped in silence;
      * pose mode suspends every binding, the free camera suspends only the keys it flies with, and with neither
        context every binding fires.

    The player's own Config.json is put back whatever happens, including a failure.

.EXAMPLE
    ./Tools/verify_keybinds.ps1

.EXAMPLE
    # the packaged hotfix rather than the build folder
    ./Tools/verify_keybinds.ps1 -Player BuildDev/UmaViewer-3.5-hotfix-2026-11-07/UmaViewer-3.5-win64/UmaViewer.exe
#>
[CmdletBinding()]
param(
    [string]$Player = "BuildDev/uma-cli/UmaViewer.exe",
    [switch]$KeepLog
)

$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot

if (-not [System.IO.Path]::IsPathRooted($Player)) { $Player = Join-Path $repo $Player }
if (-not (Test-Path $Player)) {
    Write-Host "FAIL  no player at $Player"
    Write-Host "      build one: Unity.exe -batchmode -projectPath . -executeMethod UmaPlayerBuild.BuildWindows"
    exit 1
}

$configPath = Join-Path (Split-Path -Parent $Player) "Config.json"
$backupPath = "$configPath.verify_keybinds_backup"
$logPath = Join-Path ([System.IO.Path]::GetTempPath()) "verify_keybinds_$(Get-Date -Format 'HHmmss').log"

# The probe: two keys the defaults do not use, one key the camera flies with (so the free-camera rule has
# something to suspend), one action this build does not know, and one key Unity does not know. A partial
# Config.json is enough - JsonUtility leaves every field it does not see at its default.
$probe = @'
{
    "ViewerShortcuts": [
        { "Name": "ExportModel", "Key": "Keypad1", "Alt": false },
        { "Name": "ListUp",      "Key": "Keypad2", "Alt": false },
        { "Name": "ListDown",    "Key": "W",       "Alt": false },
        { "Name": "AutoRecord",  "Key": "Keypad3", "Alt": false },
        { "Name": "RainLook",    "Key": "NotAKey", "Alt": false },
        { "Name": "BogusProbe",  "Key": "Keypad4", "Alt": false }
    ]
}
'@

$hadConfig = Test-Path $configPath
if ($hadConfig) { Copy-Item $configPath $backupPath -Force }

$results = New-Object System.Collections.Generic.List[object]
function Add-Check([string]$Name, [bool]$Pass, [string]$Detail) {
    $results.Add([pscustomobject]@{ Name = $Name; Pass = $Pass; Detail = $Detail })
}

$fatal = $null
$log = @()

try {
    Set-Content -Path $configPath -Value $probe -NoNewline

    $previous = $env:UMA_SHORTCUT_PROBE
    $env:UMA_SHORTCUT_PROBE = "1"
    try {
        $process = Start-Process -FilePath $Player -ArgumentList '--help', '-logFile', "`"$logPath`"" `
            -PassThru -WindowStyle Hidden
        if (-not $process.WaitForExit(180000)) {
            $process.Kill()
            $fatal = "the player did not exit within 180 s"
        }
    }
    finally {
        $env:UMA_SHORTCUT_PROBE = $previous
    }

    if (-not $fatal) {
        if (-not (Test-Path $logPath)) {
            $fatal = "the player wrote no log at $logPath"
        }
        else {
            $log = Get-Content $logPath
        }
    }
}
finally {
    if (Test-Path $backupPath) {
        Copy-Item $backupPath $configPath -Force
        Remove-Item $backupPath -Force
    }
    elseif ((-not $hadConfig) -and (Test-Path $configPath)) {
        Remove-Item $configPath -Force
    }
}

if ($fatal) {
    Write-Host "FAIL  $fatal"
    exit 1
}

# Both spellings of the binding log: the 3.5 build printed `[Shortcuts] N binding(s): ...` with no file named,
# which is itself part of the defect. The gate must read that line and fail on the keys it binds, not on the
# wording - a gate that only matches the fixed format cannot show what it is guarding against.
$bindings = @($log | Where-Object { $_ -like '`[Shortcuts`]*binding(s)*' })
Add-Check "the player logged a binding list" ($bindings.Count -eq 1) "found $($bindings.Count) line(s)"
$list = if ($bindings.Count -gt 0) { $bindings[0] } else { "" }

# 1. The keys are the file's, not the defaults. E and LeftArrow are in the built-in defaults and in nothing the
#    probe asks for, so either one appearing means the file was not read.
$fromFile = ($list -like '*Keypad1 -> ExportModel*') -and ($list -like '*Keypad2 -> ListUp*') -and
            ($list -like '*W -> ListDown*') -and ($list -like '*Keypad3 -> AutoRecord*')
Add-Check "the bound keys are the ones in Config.json" $fromFile `
    $(if ($fromFile) { "the four probe keys are bound" }
      else { "binding list was: $list" })

$noDefaults = ($list -notlike '*E -> ExportModel*') -and ($list -notlike '*LeftArrow -> PanelPrev*') -and
              ($list -notlike '*RainLook*')
Add-Check "no binding came from the built-in defaults" $noDefaults `
    $(if ($noDefaults) { "E, LeftArrow and RainLook are absent, as the file asks" }
      else { "a default binding is still there: $list" })

$counts = ($list -match '\[Shortcuts\] (\d+) binding') -and ($Matches[1] -eq '4')
Add-Check "the two bad entries are skipped, the four good ones bind" $counts `
    $(if ($counts) { "4 binding(s)" } else { "expected 4 binding(s): $list" })

# 2. Both kinds of bad entry are reported. Silence here is how a typo becomes "the viewer ignores my config" for
#    a user who never opens Player.log.
$unknownAction = @($log | Where-Object { $_ -like "*names an action this build does not have: 'BogusProbe'*" })
Add-Check "an unknown action name is reported" ($unknownAction.Count -eq 1) `
    "found $($unknownAction.Count) report(s)"

$unknownKey = @($log | Where-Object { $_ -like "*gives 'RainLook' an unknown key 'NotAKey'*" })
Add-Check "an unknown key name is reported" ($unknownKey.Count -eq 1) `
    "found $($unknownKey.Count) report(s)"

# 3. The suspension rules: the probe prints one line per binding per context, so the rule is the line.
$probeLines = @($log | Where-Object { $_ -like '`[Shortcuts`] probe *' })
Add-Check "every context was probed" ($probeLines.Count -eq 12) "found $($probeLines.Count) of 12 line(s)"

$idle = @($probeLines | Where-Object { $_ -like '`[Shortcuts`] probe idle *' })
$idleFires = @($idle | Where-Object { $_ -like '* FIRE' })
Add-Check "with no context, every binding fires" (($idle.Count -eq 4) -and ($idleFires.Count -eq 4)) `
    "$($idleFires.Count) of $($idle.Count) fire"

$posing = @($probeLines | Where-Object { $_ -like '`[Shortcuts`] probe posing *' })
$posingSuspended = @($posing | Where-Object { $_ -like '* SUSPENDED' })
Add-Check "pose mode suspends every binding, export and the rain look included" `
    (($posing.Count -eq 4) -and ($posingSuspended.Count -eq 4)) `
    "$($posingSuspended.Count) of $($posing.Count) suspended"

$flyingText = @($probeLines | Where-Object { $_ -like '`[Shortcuts`] probe flying *' }) -join "`n"
$cameraSuspended = $flyingText -like '*W -> ListDown SUSPENDED*'
$keypadsFire = ($flyingText -like '*Keypad1 -> ExportModel FIRE*') -and
               ($flyingText -like '*Keypad2 -> ListUp FIRE*') -and
               ($flyingText -like '*Keypad3 -> AutoRecord FIRE*')
Add-Check "the free camera suspends the key it flies with, and only that" ($cameraSuspended -and $keypadsFire) `
    "W suspended: $cameraSuspended, keys the camera does not fly with still fire: $keypadsFire"

$failed = @($results | Where-Object { -not $_.Pass })
foreach ($result in $results) {
    $status = if ($result.Pass) { "ok  " } else { "FAIL" }
    Write-Host ("{0}  {1}" -f $status, $result.Name)
    if (-not $result.Pass) { Write-Host "      $($result.Detail)" }
}

if ($KeepLog) { Write-Host "`nlog: $logPath" } else { Remove-Item $logPath -Force -ErrorAction SilentlyContinue }

if ($failed.Count -gt 0) {
    Write-Host "`n$($failed.Count) of $($results.Count) check(s) failed"
    exit 1
}

Write-Host "`n$($results.Count) of $($results.Count) check(s) passed"
exit 0
