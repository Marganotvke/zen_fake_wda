# zen_fake_wda — WDA capture experiment fork

Experimental fork of [zen_fake](https://github.com/Marganotvke/zen_fake) that uses **`SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`** on the Zen window instead of hole-buffer strip compositing.

## Why this fork?

| Main repo (`zen_fake`) | This fork (`zen_fake_wda`) |
|------------------------|----------------------------|
| Hole-buffer strip capture | **WDA exclude + full monitor blit** |
| Works without Win32 affinity | Requires Win10 2004+ (build 19041+) |
| More CPU when maximized | Usually simpler/faster per frame |
| No HWND affinity side effects | Sets/clears WDA on Zen HWND |

If `WDA_EXCLUDEFROMCAPTURE` fails on Zen's HWND, the companion **automatically falls back** to hole-buffer capture (same as main repo).

## Sine sideload

```
Marganotvke/zen_fake_wda/theme
```

## Desktop capture

```powershell
cd scripts\windows
.\install-capture.ps1
```

`index.js` spawns the companion with `--wda` (default). To force hole-buffer:

```text
ZenFakeCapture.exe ... --hole-buffer
```

## Win10 spike checklist (WDA-specific)

- [ ] Companion log shows `mode=wda` (not `hole-buffer`)
- [ ] Desktop visible through Zen chrome without mirror artifacts
- [ ] Zen still visible on physical monitor (WDA excludes from capture only)
- [ ] Companion exit clears WDA affinity (no stuck exclude state)
- [ ] Screen share / OBS still sees Zen normally on monitor, excluded from our capture

---

# Zen Fake Transparency — mod v0.4

FlexFox-style **fake transparency** for [Zen Browser](https://zen-browser.app/) on **Windows 10/11**. Paints a wallpaper, live HTML page, or **live desktop capture** behind semi-transparent chrome with optional acrylic blur — no native Mica required.

## What it does

- Hides Zen's opaque gradient / accent theme layers in dynamic modes
- **Static mode:** wallpaper on `#main-window` (CSS)
- **Live mode:** HTML/WebGL in `#zen-browser-background` via Sine + `index.js`
- **Desktop capture (v0.4):** live desktop MJPEG stream via `ZenFakeCapture.exe` (auto-spawned while Zen is open)
- Mod-only frosted tint mask (not Zen workspace accent wash)
- Optional `backdrop-filter` blur on chrome, URL bar, and menus

## Install

### Sine sideload (required for live + desktop modes)

1. Install [Sine](https://github.com/CosmoCreeper/Sine) and [fx-autoconfig](https://github.com/MrOtherGuy/fx-autoconfig); restart Zen.
2. Sine settings → sideload: `Marganotvke/zen_fake_wda/theme`
3. On Windows, run `scripts/windows/sync-wallpaper.ps1` for static/live wallpaper prefs.
4. For desktop capture, run once: `scripts/windows/install-capture.ps1`
5. Enable **Fake Transparency**; choose **Background mode** in mod settings.

### Zen native mods (static CSS only)

Copy `theme/` to:

```
%APPDATA%\zen\Profiles\<profile>\chrome\zen-themes\f4e8c2a1-9b3d-4e5f-a6c7-d8e9f0a1b2c3\
```

Live embed and desktop capture require Sine + `index.js`.

## Background modes

| Mode | User setting | Companion exe |
|------|--------------|---------------|
| Static | Background mode → Static | No |
| Live embed | Background mode → Live HTML/WebGL | No |
| Desktop capture | Background mode → Live desktop capture | Yes (auto, Zen-attached) |

When Zen is closed, **no companion process** runs (no tray, no login startup).

### Desktop capture setup

```powershell
cd scripts\windows
.\install-capture.ps1
```

Build manually (Windows + .NET 8 SDK):

```powershell
cd capture\ZenFakeCapture
dotnet publish -c Release -r win-x64 -o ..\publish
```

Set **Background mode** to **Live desktop capture**, restart Zen.

### Sync wallpaper (static / live)

```powershell
.\sync-wallpaper.ps1 -EnableMod
```

## Mod settings (v0.4)

| Setting | Description |
|---------|-------------|
| Background mode | static / live / desktop (one active) |
| Capture executable path | Set by `install-capture.ps1` |
| Background FPS | 10–30; companion reads from prefs.js |
| Capture resolution scale | 25–100% (default 50%) |
| Capture layer blur | CSS blur on MJPEG img (default 24px) |
| Mask transparency | Frosted tint over background |
| Acrylic blur | Blur on chrome layer and popups |

## Theme color isolation

Zen normally composites workspace accent colors onto `.zen-browser-generic-background` via `::before`/`::after` with `background-blend-mode: screen`. In live and desktop modes, the mod suppresses **all** Zen theme gradient layers (including `#zen-toolbar-background`) and applies only the mod `--zf-*` frosted mask — so captured desktop pixels are not tinted by workspace theme changes.

## Performance and disk health

- Desktop capture uses **strip-only GDI** compositing (not full-screen grabs each frame when Zen covers most of the monitor).
- Companion **never writes to disk**; reads `prefs.js` at most once every 5 seconds and only `zen.fake_transparency.*` lines.
- `index.js` avoids redundant pref writes and ignores CSS-only setting changes (transparency level, blur, etc.).
- Default **50% scale / 15 fps** balances CPU, memory bandwidth, and SSD wear from Zen's own profile I/O.

## Limitations

- **Hole buffer:** area under an unmoved window may look stale until Zen moves (see `capture/README.md`)
- **Win10 capture:** uses GDI hole-buffer compositing in v0.4; not pixel-perfect mirror
- **Sine required** for live/desktop JS features
- **Mod name** must stay `Fake Transparency` in `theme.json` (CSS `#theme-Fake-Transparency`)
- Some apps block screen capture (black regions)
- **Security:** live mode loads arbitrary URIs; desktop capture reads the screen

## Layout

```
theme/              chrome.css, index.js, preferences.json, theme.json
capture/            ZenFakeCapture (C# .NET 8, Windows)
scripts/windows/    sync-wallpaper.ps1, install-capture.ps1, sync-live-window.ps1
docs/               WINDOWS-WE-PLAYINWINDOW.md
```

## Verify (any OS)

```bash
python3 scripts/verify-static.py
```

## License

MPL-2.0
