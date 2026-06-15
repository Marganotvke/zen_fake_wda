# zen_fake

FlexFox-style **fake transparency** for [Zen Browser](https://zen-browser.app/) on **Windows 10/11**. Paints a wallpaper image behind semi-transparent chrome with optional acrylic blur — no native Mica required.

This is **phase 1**: wallpaper sync (Windows desktop or Wallpaper Engine source path). Phase 2 (live desktop capture) is planned separately.

## What it does

- Hides Zen's opaque gradient background
- Paints a wallpaper on `#main-window` (cover, fixed attachment)
- Applies a light/dark tint mask (adjustable transparency level)
- Optional `backdrop-filter` blur on wallpaper, URL bar, and menus
- Optional transparent web content area (with caveats)

This does **not** capture the live desktop. It mirrors a **wallpaper file path** into Zen.

## Install

### 1. Install the Zen mod

**Option A — From GitHub (recommended)**

1. Open Zen → Settings → Zen Mods
2. Install from URL using the raw `theme/chrome.css` link, or merge `install/zen-themes.json.snippet` into your profile `zen-themes.json` and copy `theme/` into `%APPDATA%\zen\Profiles\<profile>\chrome\zen-themes\f4e8c2a1-9b3d-4e5f-a6c7-d8e9f0a1b2c3\`

**Option B — Manual copy**

Copy the `theme/` folder to:

```
%APPDATA%\zen\Profiles\<your-profile>\chrome\zen-themes\f4e8c2a1-9b3d-4e5f-a6c7-d8e9f0a1b2c3\
```

Add the entry from `install/zen-themes.json.snippet` to `zen-themes.json` in the same profile.

Restart Zen and enable **Fake Transparency** in the mod settings.

### 2. Sync wallpaper (Windows)

In PowerShell:

```powershell
cd scripts\windows
.\sync-wallpaper.ps1 -EnableMod
```

Restart Zen.

**Resolution order:**

1. Wallpaper Engine `wallpaper64.exe -control getWallpaper` (if installed)
2. Windows registry / `SPI_GETDESKWALLPAPER`

**Optional — keep in sync:**

```powershell
.\watch-wallpaper.ps1 -IntervalMinutes 30
```

### 3. Optional prefs

If you enable **Transparent web content area** in mod settings, also set in `about:config`:

```
browser.tabs.allow_transparent_browser = true
```

See `install/user.js.snippet`.

## Mod settings

| Setting | Description |
|---------|-------------|
| Enable fake transparency | Master toggle (Windows only) |
| Wallpaper CSS url() | Set by sync script; manual override possible |
| Mask transparency | 0 = most opaque, 4 = clearest wallpaper |
| Wallpaper alignment | Left / center / right |
| Acrylic blur | Blur on wallpaper layer and popups |
| Disable blur | Solid tint only |
| Transparent content | Show wallpaper through tab content (fragile) |

## Limitations

- **Wallpaper Engine scene/web wallpapers** (`project.json`, `index.html`) cannot be used as CSS backgrounds — only image/video file paths work, and video may not animate in CSS.
- **Not live desktop capture** — icons and windows behind Zen are not shown.
- **Win10 only for this mod** — macOS/Linux use native Zen transparency instead.
- Combines with **DWMBlurGlass** for the native title bar only.

## Repository layout

```
theme/           Zen mod (chrome.css, preferences.json)
scripts/windows/ Wallpaper sync helpers (PowerShell)
install/         zen-themes.json snippet, user.js snippet
```

## License

MPL-2.0 (match Zen/FlexFox ecosystem conventions)
