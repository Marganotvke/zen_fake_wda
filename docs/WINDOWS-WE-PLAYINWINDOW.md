# Wallpaper Engine `playInWindow` spike (v0.4)

Prototype script: [`scripts/windows/sync-live-window.ps1`](../scripts/windows/sync-live-window.ps1)

## Goal

Render **native WE wallpapers** (scene `project.json`, web with WE APIs, WebGL) in a separate borderless WE window positioned behind Zen — without embedding HTML inside Zen.

## Approach (B1)

```
wallpaper64.exe -control openWallpaper -file "<path>" -playInWindow "ZenFakeBg" -borderless -width W -height H -x X -y Y
```

Zen mod stays transparent (CSS fake transparency). WE draws the animated background in `ZenFakeBg`. A Win32 helper syncs position/size with Zen's main HWND via `SetWindowPos`.

## Spike results (design-time, Win10)

| Area | Status | Notes |
|------|--------|-------|
| WE CLI `openWallpaper` | Expected OK | Standard WE integration path |
| `playInWindow` borderless | Expected OK | Documented WE feature |
| Find Zen HWND by process | Prototype OK | Largest titled `zen` process window |
| Find WE window by title | Fragile | Title may not equal `-playInWindow` name on all builds |
| Z-order behind Zen | Prototype OK | `HWND_BOTTOM` + matching rect |
| Move/resize sync | Not implemented | Needs timer hook or WinEvent hook |
| Multi-monitor | Not implemented | Must use per-monitor rects |
| Focus / alt-tab | Risk | Two top-level windows; WE window should not take focus (`SWP_NOACTIVATE`) |
| Zen fullscreen | Not implemented | Should hide or pause WE window |

## When to use

| Wallpaper type | Phase 2a (Sine `index.js`) | B1 `playInWindow` |
|----------------|------------------------------|-------------------|
| Static JPG/PNG | CSS only | N/A |
| Custom HTML/WebGL URL | Yes | N/A |
| WE web `index.html` | Partial (no WE APIs) | Yes |
| WE scene `project.json` | No | Yes |
| WE video `.mp4` | No (`<video>` in custom HTML only) | Yes |

## Usage (prototype)

1. Enable fake transparency in Zen (Sine or native mod).
2. Start Zen (maximized or sized as desired).
3. PowerShell:

```powershell
cd scripts\windows
.\sync-live-window.ps1
```

4. Manually verify WE animation appears behind transparent chrome.

## Known limitations

- **Not production-ready** — no resize watcher, no clean shutdown, no WE process lifecycle management.
- **HWND discovery** may need adjustment per WE/Zen versions.
- **Two apps** — user must keep WE running; higher battery/GPU use than CSS static mode.
- **DWMBlurGlass / native title bar** — independent; only affects non-client area.

## Next steps before v0.4 release

1. WinEvent `EVENT_SYSTEM_MOVESIZEEND` listener to re-sync on Zen move/resize.
2. Hide WE window when Zen minimizes or enters fullscreen (`:root[inFullscreen]` cannot reach external HWND — use process/window state).
3. Config file for window title, monitor index, and wallpaper path (share with `sync-wallpaper.ps1`).
4. Optional: single-instance mutex so re-run does not spawn duplicate WE windows.

## Alternatives considered

- **B2 WE API shim** — high maintenance; rejected unless B1 UX fails.
- **B3 Frame capture** — highest complexity; long-term only.
