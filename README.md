# zen_fake

FlexFox-style **fake transparency** for [Zen Browser](https://zen-browser.app/) on **Windows 10/11** (mod version **0.3.0**). Paints a wallpaper image (or live HTML/WebGL page) behind semi-transparent chrome with optional acrylic blur — no native Mica required.

## What it does

- Hides Zen's opaque gradient background
- **Static mode:** paints a wallpaper on `#main-window` (cover, fixed attachment)
- **Live mode (v0.3):** embeds HTML/WebGL in `#zen-browser-background` via Sine + `index.js`
- Applies a light/dark tint mask (adjustable transparency level)
- Optional `backdrop-filter` blur on wallpaper, URL bar, and menus
- Optional transparent web content area (with caveats)

Static mode does **not** capture the live desktop — it mirrors a **wallpaper file path**. Live mode runs a page inside Zen (generic HTML/WebGL; most WE web wallpapers need WE APIs or the v0.4 `playInWindow` prototype).

## Install

### Option A — Sine sideload (recommended for live backgrounds)

1. Install [Sine](https://github.com/CosmoCreeper/Sine) and [fx-autoconfig](https://github.com/MrOtherGuy/fx-autoconfig) for your Zen version; restart Zen.
2. Sine settings → sideload: `Marganotvke/zen_fake/theme`
3. On Windows, run `scripts/windows/sync-wallpaper.ps1` (Sine does not run this).
4. Enable **Fake Transparency** in Sine/Zen mod settings.

Sine path sideloads `theme/theme.json` with `chrome.css`, `preferences.json`, and `index.js`.

### Option B — Zen native mods (CSS-only static mode)

Copy `theme/` to:

```
%APPDATA%\zen\Profiles\<your-profile>\chrome\zen-themes\f4e8c2a1-9b3d-4e5f-a6c7-d8e9f0a1b2c3\
```

Add the entry from `install/zen-themes.json.snippet` to `zen-themes.json`. Live background requires Sine; native install keeps static CSS mode only.

Restart Zen and enable **Fake Transparency** in mod settings.

### Sync wallpaper (Windows)

```powershell
cd scripts\windows
.\sync-wallpaper.ps1 -EnableMod
```

Restart Zen.

**Resolution order:**

1. Wallpaper Engine `wallpaper64.exe -control getWallpaper` (if installed)
2. Windows registry / `SPI_GETDESKWALLPAPER`

If the WE source is `index.html`, the script also sets `live_background_url` and enables live mode (requires Sine). Static images still populate `wallpaper_url` for CSS fallback.

**Optional — keep in sync:**

```powershell
.\watch-wallpaper.ps1 -IntervalMinutes 30
```

### WE scene wallpapers (v0.4 prototype)

For `project.json` / scene wallpapers that cannot run in a Firefox `<browser>`, see [`docs/WINDOWS-WE-PLAYINWINDOW.md`](docs/WINDOWS-WE-PLAYINWINDOW.md) and:

```powershell
.\sync-live-window.ps1
```

### Optional prefs

If you enable **Transparent web content area**, set in `about:config`:

```
browser.tabs.allow_transparent_browser = true
```

See `install/user.js.snippet`.

## Mod settings

| Setting | Description |
|---------|-------------|
| Enable fake transparency | Master toggle (Windows only) |
| Wallpaper CSS url() | Static image; set by sync script |
| Live HTML/WebGL background | Requires Sine + fx-autoconfig |
| Live background URI | `file://` or `https://`; set by sync for `index.html` |
| Mask transparency | 0 = most opaque, 4 = clearest wallpaper |
| Wallpaper alignment | Left / center / right |
| Acrylic blur | Blur on wallpaper layer and popups |
| Disable blur | Solid tint only |
| Transparent content | Show wallpaper through tab content (fragile) |

Settings use `disabledOn: ["macos","linux"]` in `preferences.json`. CSS is gated by `@media (-moz-platform: windows)`.

## Compatibility

| Source | Static CSS | Live embed (Sine) | WE playInWindow (prototype) |
|--------|------------|-------------------|----------------------------|
| Windows JPG/PNG | Yes | Optional fallback | N/A |
| WE video `.mp4` | Partial | No | Yes |
| WE web `index.html` | No | Partial (no WE APIs) | Yes |
| WE scene `project.json` | No | No | Yes |
| Custom HTML/WebGL URL | No | Yes | N/A |

## Limitations

- **Sine vs Zen native:** JS live background needs Sine/fx-autoconfig.
- **Mod name:** keep `name: "Fake Transparency"` in `theme.json` — CSS uses `#theme-Fake-Transparency`.
- **Not live desktop capture** — icons and windows behind Zen are not shown (except WE playInWindow prototype).
- **Security:** live mode loads arbitrary URIs in a chrome `<browser>`; sync script defaults to local paths only.
- **DWMBlurGlass:** only affects the native Windows title bar; this mod handles browser chrome separately.

## Runtime caveats

### Live mode (`index.js`)

- **`file://` pages** may fail to load due to Firefox origin restrictions. If a synced WE `index.html` shows blank, try `privacy.file_unique_origin = true` in `about:config`, or host the page via local HTTP instead of `file://`.
- **Performance:** live HTML/WebGL runs in the chrome process and adds GPU/CPU use. Disable **Live HTML/WebGL background** in mod settings to fall back to static CSS wallpaper.
- **Fullscreen:** the live embed is removed while Zen is in fullscreen (`inFullscreen`); static tint/CSS behavior resumes until you exit fullscreen.
- **WE web wallpapers:** most WE `index.html` assets expect WE-injected APIs (`window.wallpaper*`, property system, asset paths). Generic HTML/WebGL URLs work; typical WE web packs often do not — use `sync-live-window.ps1` instead.

### WE `playInWindow` prototype (`sync-live-window.ps1`)

Not production-ready. See [`docs/WINDOWS-WE-PLAYINWINDOW.md`](docs/WINDOWS-WE-PLAYINWINDOW.md) for details.

- **Two apps** — Wallpaper Engine must stay running alongside Zen.
- **No move/resize sync** — repositioning or resizing Zen desyncs the WE window until a watcher is added.
- **Fragile window matching** — WE window title may not match `-playInWindow` on all builds; HWND discovery may need manual tuning.
- **Multi-monitor / fullscreen** — not handled; alt-tab and focus behavior may be awkward.

### Zen updates

`#zen-browser-background` and related gradient hooks are internal Zen DOM. Pin the Zen version you tested when reporting issues.


```
theme/              chrome.css, preferences.json, theme.json, index.js
scripts/windows/    sync-wallpaper.ps1, sync-live-window.ps1 (prototype)
docs/               WINDOWS-WE-PLAYINWINDOW.md
install/            zen-themes.json snippet, user.js snippet
```

## Tested Zen version

No specific build is pinned yet — report issues with your Zen version. Internal hooks (`#zen-browser-background`, gradient layer) may break on Zen updates.

## License

MPL-2.0
