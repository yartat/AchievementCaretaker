# AchievementCaretaker
# Copyright (c) 2026 Yaroslav V Tatarenko
#
# See LICENSE in the repository root.
#
# Chocolatey removes the package folder and the shim by itself. The shortcut is
# removed here explicitly. Nothing under ~/.sac is touched: settings, caches and
# saved libraries belong to the user, not to the package.

$ErrorActionPreference = 'Continue'

$startMenu = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs'
$shortcut  = Join-Path $startMenu 'Achievement Caretaker.lnk'
if (Test-Path -LiteralPath $shortcut) {
  Remove-Item -LiteralPath $shortcut -Force
}
