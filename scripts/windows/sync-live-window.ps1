#Requires -Version 5.1
<#
.SYNOPSIS
  Prototype: render a Wallpaper Engine wallpaper in a borderless window behind Zen (playInWindow).

.DESCRIPTION
  v0.4 spike — not production-ready. Opens WE via CLI into a named borderless window and
  attempts to position it behind the Zen main window using Win32 SetWindowPos.

  Use when the wallpaper is a WE scene (project.json) or a web wallpaper that needs WE runtime APIs.

.PARAMETER WallpaperPath
  Path to WE wallpaper folder, project.json, or index.html. Auto-detected from WE getWallpaper if omitted.

.PARAMETER WallpaperEnginePath
  Path to wallpaper64.exe. Auto-detected from Steam default if present.

.PARAMETER WindowTitle
  WE playInWindow name (must match -playInWindow argument).

.PARAMETER ZenProcessName
  Process name used to find Zen HWND (default: zen).

.EXAMPLE
  .\sync-live-window.ps1
#>
[CmdletBinding()]
param(
  [string]$WallpaperPath,
  [string]$WallpaperEnginePath,
  [string]$WindowTitle = "ZenFakeBg",
  [string]$ZenProcessName = "zen"
)

$ErrorActionPreference = "Stop"

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class Win32Window {
  public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
  public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
  public const uint SWP_NOACTIVATE = 0x0010;
  public const uint SWP_SHOWWINDOW = 0x0040;
}
"@

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

function Get-WallpaperEngineCandidates {
  param([string[]]$SteamRoots)
  $candidates = @()
  foreach ($root in $SteamRoots) {
    $candidates += Join-Path $root "steamapps\common\wallpaper_engine\wallpaper64.exe"
  }
  return @($candidates | Where-Object { Test-Path $_ } | Select-Object -Unique)
}

function Find-WallpaperEngine {
  param([string]$Explicit)
  if ($Explicit -and (Test-Path $Explicit)) {
    return (Resolve-Path $Explicit).Path
  }
  foreach ($p in (Get-WallpaperEngineCandidates -SteamRoots (Get-SteamLibraryRoots))) {
    return (Resolve-Path $p).Path
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
      if (Test-Path $candidate) { return (Resolve-Path $candidate).Path }
    }
  }
  return $null
}

function Resolve-WallpaperFile {
  param([string]$Path)
  if (-not $Path) { return $null }
  $resolved = (Resolve-Path $Path).Path
  if (Test-Path $resolved -PathType Leaf) {
    $dir = Split-Path $resolved -Parent
    if ((Split-Path $resolved -Leaf) -eq "project.json") { return $resolved }
    if ((Split-Path $resolved -Leaf) -match '\.(html?|json)$') { return $resolved }
    return $resolved
  }
  $project = Join-Path $resolved "project.json"
  if (Test-Path $project) { return (Resolve-Path $project).Path }
  $index = Join-Path $resolved "index.html"
  if (Test-Path $index) { return (Resolve-Path $index).Path }
  return $resolved
}

function Get-MainWindowHandle {
  param([string]$ProcessName)
  $targets = @()
  [Win32Window]::EnumWindows({
    param($hWnd, $lParam)
    if (-not [Win32Window]::IsWindowVisible($hWnd)) { return $true }
    $procId = 0
    [void][Win32Window]::GetWindowThreadProcessId($hWnd, [ref]$procId)
    try {
      $proc = Get-Process -Id $procId -ErrorAction Stop
    } catch { return $true }
    if ($proc.ProcessName -ne $ProcessName) { return $true }
    $sb = New-Object System.Text.StringBuilder 256
    [void][Win32Window]::GetWindowText($hWnd, $sb, 256)
    $title = $sb.ToString()
    if ($title) {
      $script:targets += [pscustomobject]@{ Handle = $hWnd; Title = $title; Area = 0 }
      $rect = New-Object Win32Window+RECT
      if ([Win32Window]::GetWindowRect($hWnd, [ref]$rect)) {
        $script:targets[-1].Area = ($rect.Right - $rect.Left) * ($rect.Bottom - $rect.Top)
      }
    }
    return $true
  }, [IntPtr]::Zero) | Out-Null
  return ($targets | Sort-Object Area -Descending | Select-Object -First 1)
}

function Find-WindowByTitle {
  param([string]$TitleFragment)
  $found = $null
  [Win32Window]::EnumWindows({
    param($hWnd, $lParam)
    $sb = New-Object System.Text.StringBuilder 256
    [void][Win32Window]::GetWindowText($hWnd, $sb, 256)
    if ($sb.ToString() -like "*$TitleFragment*") {
      $script:found = $hWnd
      return $false
    }
    return $true
  }, [IntPtr]::Zero) | Out-Null
  return $found
}

function Sync-WeBehindZen {
  param(
    [IntPtr]$ZenHwnd,
    [IntPtr]$WeHwnd
  )
  $zenRect = New-Object Win32Window+RECT
  if (-not [Win32Window]::GetWindowRect($ZenHwnd, [ref]$zenRect)) {
    throw "Could not read Zen window rect"
  }
  $w = $zenRect.Right - $zenRect.Left
  $h = $zenRect.Bottom - $zenRect.Top
  $flags = [Win32Window]::SWP_NOACTIVATE -bor [Win32Window]::SWP_SHOWWINDOW
  [void][Win32Window]::SetWindowPos(
    $WeHwnd,
    [Win32Window]::HWND_BOTTOM,
    $zenRect.Left,
    $zenRect.Top,
    $w,
    $h,
    $flags
  )
}

# --- main ---

$weExe = Find-WallpaperEngine -Explicit $WallpaperEnginePath
if (-not $weExe) {
  throw "wallpaper64.exe not found. Install Wallpaper Engine or pass -WallpaperEnginePath."
}

if (-not $WallpaperPath) {
  $WallpaperPath = Get-WallpaperEnginePath -ExePath $weExe
}
if (-not $WallpaperPath) {
  throw "No wallpaper path. Pass -WallpaperPath or run a WE wallpaper first."
}

$wallpaperFile = Resolve-WallpaperFile -Path $WallpaperPath
Write-Host "WE wallpaper file: $wallpaperFile"

$zen = Get-MainWindowHandle -ProcessName $ZenProcessName
if (-not $zen) {
  throw "Zen main window not found (process: $ZenProcessName). Start Zen first."
}
Write-Host "Zen window: $($zen.Title)"

$weArgs = @(
  "-control", "openWallpaper",
  "-file", $wallpaperFile,
  "-playInWindow", $WindowTitle,
  "-borderless",
  "-width", "800",
  "-height", "600"
)
Write-Host "Launching WE: $weExe $($weArgs -join ' ')"
Start-Process -FilePath $weExe -ArgumentList $weArgs -WindowStyle Hidden | Out-Null

Start-Sleep -Seconds 3
$weHwnd = Find-WindowByTitle -TitleFragment $WindowTitle
if (-not $weHwnd) {
  Write-Warning "WE playInWindow '$WindowTitle' not found yet. Window title may differ — check Task Manager."
  Write-Host "Feasibility: CLI launch works; HWND matching needs manual title tuning per WE build."
  exit 1
}

Sync-WeBehindZen -ZenHwnd $zen.Handle -WeHwnd $weHwnd
Write-Host "Positioned WE window behind Zen (prototype). Move/resize Zen will desync until a watcher is added."
Write-Host "See docs/WINDOWS-WE-PLAYINWINDOW.md for full v0.4 spike notes."
