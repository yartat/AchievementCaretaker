# AchievementCaretaker
# Copyright (c) 2026 Yaroslav V Tatarenko
#
# See LICENSE in the repository root.
#
# Checks that the Windows installer removes only what it installed. Silent installs and
# uninstalls into throw-away folders under %TEMP%; nothing else is touched.
#
#   pwsh -File packaging/windows/test-installer.ps1 -Setup dist/achievement-caretaker-X.Y.Z-win-x64-setup.exe
#
# Refuses to run where the application is installed: the installer and the uninstaller
# write the same registry keys an install uses.

param([Parameter(Mandatory)][string]$Setup)

$ErrorActionPreference = 'Stop'
$key = 'HKCU:\Software\AchievementCaretaker'
if (Test-Path $key) { throw 'Achievement Caretaker is installed on this machine: not testing here.' }
$Setup = (Resolve-Path -LiteralPath $Setup).Path
$root = Join-Path $env:TEMP ('sac-installer-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $root | Out-Null
$script:failed = 0

function Check([string]$name, [bool]$ok) {
    if ($ok) { "  PASS  $name" } else { "  FAIL  $name"; $script:failed++ }
}

function Install([string]$dir) {
    $p = Start-Process -FilePath $Setup -ArgumentList "/S /D=$dir" -PassThru -Wait
    if ($p.ExitCode -ne 0) { throw "install exited with $($p.ExitCode)" }
}

function Uninstall {
    $dir = (Get-ItemProperty $key).InstallDir
    $null = Start-Process -FilePath (Join-Path $dir 'Uninstall.exe') -ArgumentList "/S _?=$dir" -PassThru -Wait
    # The uninstaller removes itself and its folder from a detached command, a moment later.
    Start-Sleep -Seconds 4
}

function Has([string]$path) { Test-Path -LiteralPath $path }

function Only([string]$dir, [string[]]$names) {
    if (-not (Has $dir)) { return $false }
    $left = @(Get-ChildItem -LiteralPath $dir -Force -Recurse | ForEach-Object { $_.FullName.Substring($dir.Length + 1) } | Sort-Object)
    return ($left -join '|') -eq (($names | Sort-Object) -join '|')
}

try {
    '1. a folder that already holds a file of the user'
    $x = Join-Path $root 'x'
    New-Item -ItemType Directory $x | Out-Null
    Set-Content -LiteralPath (Join-Path $x 'keep.txt') 'mine'
    Install $x
    Check 'the application goes into its own subfolder' (Has (Join-Path $x 'AchievementCaretaker\AchievementCaretaker.exe'))
    Uninstall
    Check 'only the user file is left' (Only $x @('keep.txt'))

    '2. a folder named AchievementCaretaker holding an unzipped copy and a file of the user'
    $y = Join-Path $root 'y\AchievementCaretaker'
    New-Item -ItemType Directory $y | Out-Null
    Set-Content -LiteralPath (Join-Path $y 'AchievementCaretaker.exe') 'portable'
    Set-Content -LiteralPath (Join-Path $y 'keep.txt') 'mine'
    Install $y
    Check 'the user file survives the install' (Has (Join-Path $y 'keep.txt'))
    Install $y
    Check 'the user file survives a second install' (Has (Join-Path $y 'keep.txt'))
    Uninstall
    Check 'after the uninstall only the user file is left' (Only $y @('keep.txt'))

    '3. an upgrade of a folder of its own'
    $u = Join-Path $root 'u\AchievementCaretaker'
    Install $u
    Set-Content -LiteralPath (Join-Path $u 'stale.dll') 'old'
    Install $u
    Check 'a stale file is cleared by the upgrade' (-not (Has (Join-Path $u 'stale.dll')))
    Uninstall
    Check 'the folder is removed' (-not (Has $u))

    '4. an upgrade from 2.2.0, which wrote no marker'
    $o = Join-Path $root 'o\AchievementCaretaker'
    Install $o
    Remove-Item -LiteralPath (Join-Path $o '.achievement-caretaker-install') -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $o '.achievement-caretaker-files') -Force -ErrorAction SilentlyContinue
    Remove-ItemProperty -Path $key -Name 'Layout' -ErrorAction SilentlyContinue
    Set-Content -LiteralPath (Join-Path $o 'stale.dll') 'old'
    Install $o
    Check 'its files are cleared' (-not (Has (Join-Path $o 'stale.dll')))
    Uninstall
    Check 'and it uninstalls completely' (-not (Has $o))

    '5. parent folders whose names cmd would read'
    foreach ($name in 'a&b', 'p%OS%q') {
        $a = Join-Path $root "$name\AchievementCaretaker"
        Install $a
        Uninstall
        Check "$name : the application folder is removed" (-not (Has $a))
        Check "$name : the parent folder stays" (Has (Join-Path $root $name))
    }
}
finally {
    if (Test-Path $key) { Uninstall }
    Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
}

"failed checks: $script:failed"
exit $script:failed
