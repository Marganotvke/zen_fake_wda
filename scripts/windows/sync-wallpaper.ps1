#Requires -Version 5.1
<#
.SYNOPSIS
  Syncs the current desktop wallpaper into Zen's fake-transparency mod pref.

.DESCRIPTION
  Resolution order:
    1. Wallpaper Engine CLI (getWallpaper) — returns source asset path (.mp4, project.json, etc.)
    2. Windows registry wallpaper path (HKCU\Control Panel\Desktop\Wallpaper)
    3. SystemParametersInfo via .NET (SPI_GETDESKWALLPAPER)

  Writes zen.fake_transparency.wallpaper_url as a CSS url("file:///...") value into prefs.js.

.PARAMETER ProfilePath
  Zen profile directory. Auto-detected from %APPDATA%\zen\Profiles if omitted.

.PARAMETER WallpaperEnginePath
  Path to wallpaper64.exe. Auto-detected from default Steam location if present.

.PARAMETER EnableMod
  Also set zen.fake_transparency.enabled to true.

.EXAMPLE
  .\sync-wallpaper.ps1 -EnableMod
#>
[CmdletBinding()]
param(
  [string]$ProfilePath,
  [string]$WallpaperEnginePath,
  [switch]$EnableMod
)

$ErrorActionPreference = "Stop"

$PREF_WALLPAPER = "zen.fake_transparency.wallpaper_url"
$PREF_ENABLED = "zen.fake_transparency.enabled"

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

function Find-WallpaperEngine {
  param([string]$Explicit)
  if ($Explicit -and (Test-Path $Explicit)) {
    return (Resolve-Path $Explicit).Path
  }

  $steamRoots = @(
    "${env:ProgramFiles(x86)}\Steam\steamapps\common\wallpaper_engine\wallpaper64.exe",
    "$env:ProgramFiles\Steam\steamapps\common\wallpaper_engine\wallpaper64.exe"
  )

  foreach ($p in $steamRoots) {
    if (Test-Path $p) { return (Resolve-Path $p).Path }
  }

  return $null
}

function Get-WallpaperEnginePath {
  param([string]$ExePath)
  if (-not $ExePath) { return $null }

  $psi = New-Object System.Diagnostics.ProcessStartInfo
  $psi.FileName = $ExePath
  $psi.Arguments = "-control getWallpaper"
  $psi.UseShellExecute = $false
  $psi.RedirectStandardOutput = $true
  $psi.RedirectStandardError = $true
  $psi.CreateNoWindow = $true

  $proc = [System.Diagnostics.Process]::Start($psi)
  $stdout = $proc.StandardOutput.ReadToEnd().Trim()
  $proc.WaitForExit()

  if ($proc.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($stdout)) {
    return $null
  }

  if (Test-Path $stdout) {
    Write-Verbose "Wallpaper Engine: $stdout"
    return $stdout
  }

  return $null
}

function Get-WindowsWallpaperPath {
  $regPath = "HKCU:\Control Panel\Desktop"
  $fromReg = (Get-ItemProperty -Path $regPath -Name Wallpaper -ErrorAction SilentlyContinue).Wallpaper
  if ($fromReg -and (Test-Path $fromReg)) {
    Write-Verbose "Registry wallpaper: $fromReg"
    return $fromReg
  }

  Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class DesktopWallpaper {
  [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
  public static extern bool SystemParametersInfo(int uiAction, int uiParam, StringBuilder pvParam, int fWinIni);
  public static string Get() {
    var sb = new StringBuilder(512);
    if (SystemParametersInfo(0x0073, sb.Capacity, sb, 0)) return sb.ToString();
    return null;
  }
}
"@

  $fromSpi = [DesktopWallpaper]::Get()
  if ($fromSpi -and (Test-Path $fromSpi)) {
    Write-Verbose "SPI wallpaper: $fromSpi"
    return $fromSpi
  }

  return $null
}

function ConvertTo-CssFileUrl {
  param([string]$FilePath)
  $uri = [System.Uri]::new((Resolve-Path $FilePath).Path).AbsoluteUri
  return "url(`"$uri`")"
}

function Set-ZenPref {
  param(
    [string]$PrefsFile,
    [string]$Name,
    [string]$Value,
    [ValidateSet("string", "bool")]
    [string]$Type = "string"
  )

  $line = if ($Type -eq "bool") {
    $bool = if ($Value -match '^(true|1|yes)$') { "true" } else { "false" }
    "user_pref(`"$Name`", $bool);"
  } else {
    $escaped = $Value -replace '\\', '\\\\' -replace '"', '\"'
    "user_pref(`"$Name`", `"$escaped`");"
  }

  $content = if (Test-Path $PrefsFile) { Get-Content -Path $PrefsFile -Raw } else { "" }

  if ($content -match "user_pref\(`"$([regex]::Escape($Name))`"") {
    $content = [regex]::Replace(
      $content,
      "user_pref\(`"$([regex]::Escape($Name))`",[^)]*\);",
      $line
    )
  } else {
    if ($content -and -not $content.EndsWith("`n")) { $content += "`n" }
    $content += $line + "`n"
  }

  Set-Content -Path $PrefsFile -Value $content -Encoding UTF8 -NoNewline
}

function Test-WallpaperUsableInCss {
  param([string]$Path)
  $ext = [System.IO.Path]::GetExtension($Path).ToLowerInvariant()
  $ok = @(".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".avif", ".apng", ".mp4", ".webm")
  return $ok -contains $ext
}

# --- main ---

$profile = Find-ZenProfile -Explicit $ProfilePath
$prefsFile = Join-Path $profile "prefs.js"
$weExe = Find-WallpaperEngine -Explicit $WallpaperEnginePath

Write-Host "Zen profile: $profile"

$wallpaperPath = Get-WallpaperEnginePath -ExePath $weExe
$source = "Wallpaper Engine"

if (-not $wallpaperPath) {
  $wallpaperPath = Get-WindowsWallpaperPath
  $source = "Windows desktop"
}

if (-not $wallpaperPath) {
  throw "No wallpaper path found. Set a Windows wallpaper or run Wallpaper Engine first."
}

if (-not (Test-WallpaperUsableInCss -Path $wallpaperPath)) {
  Write-Warning @"
Wallpaper source is '$wallpaperPath' ($source).
Scene/web wallpapers (project.json, index.html) cannot be used as a CSS background.
For video wallpapers (.mp4), Zen may not animate them in CSS — a static first frame is not guaranteed.
Consider using a static Windows wallpaper or a video file path and accept approximate matching.
"@
}

$cssUrl = ConvertTo-CssFileUrl -FilePath $wallpaperPath
Set-ZenPref -PrefsFile $prefsFile -Name $PREF_WALLPAPER -Value $cssUrl

if ($EnableMod) {
  Set-ZenPref -PrefsFile $prefsFile -Name $PREF_ENABLED -Value "true" -Type bool
}

Write-Host "Synced ($source):"
Write-Host "  File: $wallpaperPath"
Write-Host "  Pref: $PREF_WALLPAPER = $cssUrl"
if ($EnableMod) {
  Write-Host "  Pref: $PREF_ENABLED = true"
}
Write-Host ""
Write-Host "Restart Zen for changes to take effect."
