Fake transparency for Zen on Windows. Paints a synced wallpaper (static CSS or live HTML/WebGL via Sine) behind semi-transparent chrome (FlexFox-style), without native Mica.

- **Static:** `scripts/windows/sync-wallpaper.ps1` → `wallpaper_url` pref
- **Live (Sine):** same script detects `index.html` → `live_background_url` + `index.js`
- **WE scenes:** prototype `scripts/windows/sync-live-window.ps1` — see `docs/WINDOWS-WE-PLAYINWINDOW.md`
