# Optional: run sync-wallpaper.ps1 every 30 minutes
#Requires -Version 5.1
param(
  [int]$IntervalMinutes = 30,
  [string]$ProfilePath,
  [switch]$EnableMod
)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$sync = Join-Path $scriptDir "sync-wallpaper.ps1"

if (-not (Test-Path $sync)) {
  throw "sync-wallpaper.ps1 not found beside this script."
}

Write-Host "Watching wallpaper changes every $IntervalMinutes minute(s). Press Ctrl+C to stop."

while ($true) {
  try {
    $args = @()
    if ($ProfilePath) { $args += "-ProfilePath", $ProfilePath }
    if ($EnableMod) { $args += "-EnableMod" }
    & $sync @args
  } catch {
    Write-Warning $_.Exception.Message
  }
  Start-Sleep -Seconds ($IntervalMinutes * 60)
}
