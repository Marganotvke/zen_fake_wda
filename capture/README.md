# Build (Windows only)

```powershell
cd capture\ZenFakeCapture
dotnet publish -c Release -r win-x64 --self-contained false -o ..\ZenFakeCapture.exe
```

Output: `capture/ZenFakeCapture.exe/ZenFakeCapture.exe` (folder from publish `-o`).

Copy `ZenFakeCapture.exe` to a permanent path and set **Capture executable path** in mod settings, or run `scripts/windows/install-capture.ps1`.

## CLI

```
ZenFakeCapture.exe --watch-pid <zen-pid> --profile <zen-profile-dir> --no-tray
  [--host 127.0.0.1] [--port 8765] [--fps 15] [--scale 50]
```

- **GET /stream** — MJPEG multipart stream for `<img src=".../stream">`
- **POST /shutdown** — graceful exit

## Hole buffer

Each frame:

1. Capture primary monitor at `capture_scale` (default 50%).
2. Find Zen main window rect for `--watch-pid`.
3. Copy fresh pixels **outside** the window into a persistent buffer.
4. Leave pixels **inside** the hole unchanged (avoids mirroring Zen chrome).

Area under an unmoved window may stay wallpaper-seeded until the window moves.

## Phase 0 spike checklist (Win10)

- [ ] No visible mirroring when Zen is over a solid-color desktop region
- [ ] 50% scale + 15 fps acceptable on 1080p / 1440p
- [ ] HWND mapping updates on move/resize
- [ ] Companion exits when Zen quits (`--watch-pid`)
- [ ] Theme isolation: switch workspace accent — capture tint unchanged (mod CSS only)

## Performance and disk I/O

- **Strip capture:** each frame copies only monitor regions *outside* the Zen window (not full-screen). Biggest win when Zen is maximized.
- **Reuse:** bitmaps, JPEG encoder, and memory stream are reused across frames; bilinear scaling (blur hides artifacts).
- **Pause when minimized:** capture loop idles at 500 ms; last MJPEG frame is kept.
- **HWND cache:** window lookup cached ~400 ms to avoid repeated `EnumWindows`.
- **prefs.js:** companion polls file mtime at most every **5 s**, reads only `zen.fake_transparency.*` lines (no full-profile parse every frame).
- **index.js:** ignores CSS-only pref changes; debounces resize (300 ms) to avoid companion restarts on every pixel of drag.

## v0.4 capture backend

Uses GDI `CopyFromScreen` with hole-buffer compositing. WGC upgrade path is reserved for v0.5 (lower latency, better multi-monitor).
