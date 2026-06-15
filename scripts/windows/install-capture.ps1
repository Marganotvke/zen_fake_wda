#Requires -Version 5.1
<#
.SYNOPSIS
  One-time setup for zen_fake desktop capture (ZenFakeCapture.exe).

.DESCRIPTION
  Builds or locates ZenFakeCapture.exe and writes zen.fake_transparency.capture_exe_path
  in the Zen profile prefs.js. Does not start the companion — index.js spawns it when
  desktop capture mode is enabled and Zen is running.

.PARAMETER ProfilePath
  Zen profile directory. Auto-detected if omitted.

.PARAMETER ExePath
  Path to an existing ZenFakeCapture.exe. If omitted, builds from capture/ZenFakeCapture.

.PARAMETER SkipBuild
  Do not run dotnet publish; fail if ExePath is not provided and no built exe exists.

.EXAMPLE
  .\install-capture.ps1
#>
[CmdletBinding()]
param(
  [string]$ProfilePath,
  [string]$ExePath,
  [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

$PREF_CAPTURE_EXE = "zen.fake_transparency.capture_exe_path"
$PREF_CAPTURE_URL = "zen.fake_transparency.capture_helper_url"
$PREF_MODE = "zen.fake_transparency.background_mode"

function Find-ZenProfile {
  param([string]$Explicit)
  if ($Explicit -and (Test-Path $Explicit)) {
    return (Resolve-Path $Explicit).Path
  }

  $base = Join-Path $env:APPDATA "zen\Profiles"
  if (-not (Test-Path $base)) {
    throw "Zen profiles folder not found: $base"
  }

  $candidates = Get-ChildItem -Path $base -Directory |
    Where-Object { $_.Name -match '\.(release|default)' } |
    Sort-Object LastWriteTime -Descending

  if (-not $candidates) {
    throw "No Zen profile folders found under $base"
  }

  return $candidates[0].FullName
}

function Set-ZenPref {
  param(
    [string]$Profile,
    [string]$Name,
    [string]$Value,
    [ValidateSet("string", "bool")]
    [string]$Type = "string"
  )

  $prefsPath = Join-Path $Profile "prefs.js"
  if (-not (Test-Path $prefsPath)) {
    New-Item -Path $prefsPath -ItemType File -Force | Out-Null
  }

  $content = Get-Content -Path $prefsPath -Raw -ErrorAction SilentlyContinue
  if (-not $content) { $content = "" }

  $escaped = $Value -replace '\\', '\\\\' -replace '"', '\"'
  $line = if ($Type -eq "bool") {
    "user_pref(`"$Name`", $(if ($Value -eq 'true') { 'true' } else { 'false' }));"
  } else {
    "user_pref(`"$Name`", `"$escaped`");"
  }

  $pattern = [regex]::Escape("user_pref(`"$Name`",")
  if ($content -match $pattern) {
    $content = [regex]::Replace(
      $content,
      "user_pref\(`"$([regex]::Escape($Name))`",\s*(?:true|false|\""[^\""]*\"")\);",
      $line
    )
  } else {
    if ($content -and -not $content.EndsWith("`n")) { $content += "`n" }
    $content += "$line`n"
  }

  Set-Content -Path $prefsPath -Value $content -Encoding UTF8 -NoNewline
  Write-Host "Set $Name"
}

function Resolve-CaptureExe {
  param([string]$Explicit, [switch]$NoBuild)

  if ($Explicit) {
    if (-not (Test-Path $Explicit)) {
      throw "ExePath not found: $Explicit"
    }
    return (Resolve-Path $Explicit).Path
  }

  $repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
  $projectDir = Join-Path $repoRoot "capture\ZenFakeCapture"
  $publishDir = Join-Path $repoRoot "capture\publish"
  $publishedExe = Join-Path $publishDir "ZenFakeCapture.exe"

  if (Test-Path $publishedExe) {
    return (Resolve-Path $publishedExe).Path
  }

  if ($NoBuild) {
    throw "ZenFakeCapture.exe not found. Build with: dotnet publish -c Release -r win-x64 -o capture\publish"
  }

  if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK not found. Install .NET 8 SDK or pass -ExePath to a built ZenFakeCapture.exe"
  }

  Write-Host "Building ZenFakeCapture..."
  Push-Location $projectDir
  try {
    dotnet publish -c Release -r win-x64 --self-contained false -o $publishDir
  } finally {
    Pop-Location
  }

  if (-not (Test-Path $publishedExe)) {
    throw "Build failed: $publishedExe not created"
  }

  return (Resolve-Path $publishedExe).Path
}

$profile = Find-ZenProfile -Explicit $ProfilePath
$exe = Resolve-CaptureExe -Explicit $ExePath -NoBuild:$SkipBuild

Set-ZenPref -Profile $profile -Name $PREF_CAPTURE_EXE -Value $exe -Type string
Set-ZenPref -Profile $profile -Name $PREF_CAPTURE_URL -Value "http://127.0.0.1:8765" -Type string

Write-Host ""
Write-Host "Desktop capture helper installed."
Write-Host "  Profile: $profile"
Write-Host "  Exe:     $exe"
Write-Host ""
Write-Host "In Zen mod settings: set Background mode to 'Live desktop capture' and enable Fake Transparency."
Write-Host "Restart Zen. The companion starts automatically while Zen is open."
