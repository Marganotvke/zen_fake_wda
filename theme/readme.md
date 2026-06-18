# Zen Fake Transparency (v0.4 WDA)

Windows-only mod: static wallpaper, live HTML/WebGL embed, or live desktop capture behind frosted Zen chrome.

## Mod settings explained

### Background mode (dropdown)

Pick **one** background source:

| Option | What it does |
|--------|----------------|
| **Static wallpaper** | CSS wallpaper on `#main-window` (sync via `sync-wallpaper.ps1`) |
| **Live HTML/WebGL embed** | Loads a URI into a background `<browser>` (needs Sine + fx-autoconfig) |
| **Live desktop capture** | Spawns `ZenFakeCapture.exe`, streams MJPEG into the mod (needs install-capture.ps1) |

You only need to set this dropdown. Older builds also showed **Live HTML/WebGL background** and **Desktop capture** checkboxes below — those were internal sync flags for CSS (`-moz-bool-pref`) and are **no longer shown**. `index.uc.js` sets them automatically from your dropdown choice.

### When Zen is not focused (dropdown)

| Option | Behavior |
|--------|----------|
| **Keep transparency active** | Default — effects and capture keep running while alt-tabbed |
| **Pause desktop capture only** | Stops `ZenFakeCapture.exe` when unfocused; frosted chrome stays (saves CPU) |
| **Pause all transparency** | Turns off mod visuals and stops capture/live embed until Zen is focused again |

## Sine sideload

```
Marganotvke/zen_fake_wda/tree/main/theme
```

Do **not** use `Marganotvke/zen_fake_wda/theme` — Sine requires the `/tree/<branch>/<folder>` format.

### Sine prerequisites

- GitHub repo must be **public** (private repos fail zip download)
- Sine: enable **installing JS from unofficial sources**
- `about:config`: `toolkit.legacyUserProfileCustomizations.stylesheets` = true
- fx-autoconfig installed

## Desktop capture setup

1. Install Sine + fx-autoconfig; sideload `Marganotvke/zen_fake_wda/tree/main/theme`
2. Run once:

```powershell
cd scripts\windows
.\install-capture.ps1
```

3. Enable **Fake Transparency**; set **Background mode** to **Live desktop capture**
4. Restart Zen — companion starts automatically; exits when Zen closes

See [`capture/README.md`](../capture/README.md) for build details.

## Theme color isolation

Zen normally paints workspace accent gradients on `.zen-browser-generic-background` pseudo-elements (`background-blend-mode: screen`). In live/desktop modes, the mod suppresses all Zen theme layers (including `#zen-toolbar-background`) and applies only the mod frosted mask (`--zf-*`).
