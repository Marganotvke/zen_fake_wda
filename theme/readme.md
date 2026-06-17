# Zen Fake Transparency (v0.4 WDA)

Windows-only mod: static wallpaper, live HTML/WebGL embed, or live desktop capture behind frosted Zen chrome.

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

## Background modes

| Mode | Setting | Requires |
|------|---------|----------|
| Static | Background mode → Static | CSS only (or Sine) |
| Live embed | Background mode → Live HTML/WebGL | Sine + fx-autoconfig |
| Desktop capture | Background mode → Live desktop capture | Sine + ZenFakeCapture.exe |

Only one mode is active at a time. `index.uc.js` syncs bool prefs for CSS gating.

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
