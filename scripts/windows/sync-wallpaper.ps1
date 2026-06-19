#Requires -Version 5.1
<#
.SYNOPSIS
  Syncs the current desktop wallpaper into Zen's fake-transparency mod prefs.

.DESCRIPTION
  Resolution order:
    1. Wallpaper Engine CLI (getWallpaper) — returns source asset path
    2. Windows registry wallpaper path (HKCU\Control Panel\Desktop\Wallpaper)
    3. SystemParametersInfo via .NET (SPI_GETDESKWALLPAPER)

  For static images/video: writes zen.fake_transparency.wallpaper_url as CSS url("file:///...").
  For web wallpapers (index.html): also sets zen.fake_transparency.live_background_url and
  enables zen.fake_transparency.live_background_enabled (requires Sine + index.js).

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
$PREF_LIVE_ENABLED = "zen.fake_transparency.live_background_enabled"
$PREF_LIVE_URL = "zen.fake_transparency.live_background_url"
$PREF_BACKGROUND_MODE = "zen.fake_transparency.background_mode"

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

function Get-SteamLibraryRoots {
  $roots = @()
  foreach ($base in @(
    "${env:ProgramFiles(x86)}\Steam",
    "$env:ProgramFiles\Steam"
  )) {
    if ($base -and (Test-Path $base)) { $roots += $base }
  }

  foreach ($drive in Get-PSDrive -PSProvider FileSystem) {
    $steamRoot = Join-Path $drive.Root "Steam"
    $steamLibraryRoot = Join-Path $drive.Root "SteamLibrary"
    if (Test-Path $steamRoot) { $roots += $steamRoot }
    if (Test-Path $steamLibraryRoot) { $roots += $steamLibraryRoot }
  }

  $vdfFiles = @()
  foreach ($root in @($roots | Select-Object -Unique)) {
    $vdfFiles += Join-Path (Join-Path $root "steamapps") "libraryfolders.vdf"
  }
  foreach ($vdf in @($vdfFiles | Where-Object { Test-Path $_ } | Select-Object -Unique)) {
    $content = Get-Content -Path $vdf -Raw
    foreach ($match in [regex]::Matches($content, '"path"\s+"((?:[^"\\]|\\.)+)"')) {
      $path = $match.Groups[1].Value -replace '\\\\', '\'
      if (Test-Path $path) { $roots += $path }
    }
  }

  return @($roots | Select-Object -Unique)
}

function Find-WallpaperEngine {
  param([string]$Explicit)
  if ($Explicit -and (Test-Path $Explicit)) {
    return (Resolve-Path $Explicit).Path
  }

  foreach ($root in (Get-SteamLibraryRoots)) {
    $p = Join-Path $root "steamapps\common\wallpaper_engine\wallpaper64.exe"
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
  $stdout = $proc.StandardOutput.ReadToEnd()
  $stderr = $proc.StandardError.ReadToEnd()
  $proc.WaitForExit()

  foreach ($line in @($stdout, $stderr)) {
    foreach ($candidate in @($line -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ })) {
      if (Test-Path $candidate) {
        Write-Verbose "Wallpaper Engine: $candidate"
        return (Resolve-Path $candidate).Path
      }
    }
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

function ConvertTo-FileUri {
  param([string]$FilePath)
  return [System.Uri]::new((Resolve-Path $FilePath).Path).AbsoluteUri
}

function Resolve-LiveBackgroundPath {
  param([string]$Path)
  if (-not $Path) { return $null }

  $resolved = if (Test-Path $Path) {
    (Resolve-Path $Path).Path
  } else {
    return $null
  }

  $ext = [System.IO.Path]::GetExtension($resolved).ToLowerInvariant()
  $dir = [System.IO.Path]::GetDirectoryName($resolved)

  if ($ext -in @(".html", ".htm")) {
    return $resolved
  }

  if ($ext -eq ".json" -and [System.IO.Path]::GetFileName($resolved) -eq "project.json") {
    foreach ($candidate in @("index.html", "index.htm")) {
      $html = Join-Path $dir $candidate
      if (Test-Path $html) {
        return (Resolve-Path $html).Path
      }
    }
    Write-Warning "project.json found but no index.html in '$dir'. Scene wallpapers need sync-live-window.ps1 (WE playInWindow)."
    return $null
  }

  if ((Test-Path $resolved -PathType Container)) {
    foreach ($candidate in @("index.html", "index.htm")) {
      $html = Join-Path $resolved $candidate
      if (Test-Path $html) {
        return (Resolve-Path $html).Path
      }
    }
  }

  return $null
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

$livePath = Resolve-LiveBackgroundPath -Path $wallpaperPath
if ($livePath) {
  $liveUri = ConvertTo-FileUri -FilePath $livePath
  Set-ZenPref -PrefsFile $prefsFile -Name $PREF_LIVE_URL -Value $liveUri
  Set-ZenPref -PrefsFile $prefsFile -Name $PREF_LIVE_ENABLED -Value "true" -Type bool
  Set-ZenPref -PrefsFile $prefsFile -Name $PREF_BACKGROUND_MODE -Value "live"
  Write-Host "Live background detected:"
  Write-Host "  HTML: $livePath"
  Write-Host "  Pref: $PREF_LIVE_URL = $liveUri"
  Write-Host "  Pref: $PREF_LIVE_ENABLED = true"
  Write-Host "  Note: requires Sine + fx-autoconfig for index.js. Most WE web wallpapers need WE APIs."
} else {
  Set-ZenPref -PrefsFile $prefsFile -Name $PREF_LIVE_ENABLED -Value "false" -Type bool
  if (-not (Select-String -Path $prefsFile -Pattern 'zen\.fake_transparency\.background_mode' -Quiet)) {
    Set-ZenPref -PrefsFile $prefsFile -Name $PREF_BACKGROUND_MODE -Value "static"
  }
}

if (Test-WallpaperUsableInCss -Path $wallpaperPath) {
  $cssUrl = ConvertTo-CssFileUrl -FilePath $wallpaperPath
  Set-ZenPref -PrefsFile $prefsFile -Name $PREF_WALLPAPER -Value $cssUrl
  Write-Host "Static wallpaper synced ($source):"
  Write-Host "  File: $wallpaperPath"
  Write-Host "  Pref: $PREF_WALLPAPER = $cssUrl"
} elseif (-not $livePath) {
  Write-Warning @"
Wallpaper source is '$wallpaperPath' ($source).
Scene/web wallpapers (project.json without index.html) cannot be used as CSS backgrounds.
Run scripts/windows/sync-live-window.ps1 for WE scene wallpapers (playInWindow prototype).
"@
}

if ($EnableMod) {
  Set-ZenPref -PrefsFile $prefsFile -Name $PREF_ENABLED -Value "true" -Type bool
  Write-Host "  Pref: $PREF_ENABLED = true"
}

Write-Host ""
Write-Host "Restart Zen for changes to take effect."
