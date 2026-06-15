#!/usr/bin/env python3
"""Static checks for zen_fake (run on any OS)."""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
BROWSER_XHTML = (
    Path(__file__).resolve().parents[2]
    / ".browser-omni/chrome/browser/content/browser/browser.xhtml"
)

ERRORS: list[str] = []
WARNINGS: list[str] = []


def main() -> int:
    prefs = json.loads((ROOT / "theme/preferences.json").read_text())
    css = (ROOT / "theme/chrome.css").read_text()
    ps = (ROOT / "scripts/windows/sync-wallpaper.ps1").read_text()
    theme_json_path = ROOT / "theme/theme.json"

    if not theme_json_path.exists():
        ERRORS.append("theme/theme.json missing (required for Sine sideload)")
    else:
        theme = json.loads(theme_json_path.read_text())
        if theme.get("style", {}).get("chrome") != "chrome.css":
            ERRORS.append("theme.json style.chrome must reference chrome.css")
        if theme.get("preferences") != "preferences.json":
            ERRORS.append("theme.json preferences must reference preferences.json")
        if theme.get("name") != "Fake Transparency":
            ERRORS.append('theme.json name must stay "Fake Transparency" (CSS #theme-Fake-Transparency)')
        scripts = theme.get("scripts", {})
        if "index.js" not in scripts:
            WARNINGS.append("theme.json has no scripts.index.js (live background needs Sine)")

    index_js = ROOT / "theme/index.js"
    if not index_js.exists():
        ERRORS.append("theme/index.js missing (live background loader)")
    else:
        index_src = index_js.read_text()
        for needle in (
            "zen-browser-background",
            "zen-fake-transparency-live-browser",
            "zen.fake_transparency.live_background_enabled",
        ):
            if needle not in index_src:
                ERRORS.append(f"index.js missing reference: {needle}")

    if BROWSER_XHTML.exists():
        xhtml = BROWSER_XHTML.read_text()
        for id_ in (
            "main-window",
            "zen-main-app-wrapper",
            "navigator-toolbox",
            "tabbrowser-tabpanels",
            "zen-browser-background",
        ):
            if f'id="{id_}"' not in xhtml:
                WARNINGS.append(f'id="{id_}" not found in browser.xhtml')

    if "theme-Fake-Transparency" not in css:
        ERRORS.append("CSS must reference hidden mod div id theme-Fake-Transparency")

    if "--zen-fake_transparency-wallpaper_url" not in css:
        ERRORS.append("CSS missing --zen-fake_transparency-wallpaper_url")

    if "#zen-fake-transparency-live-browser" not in css:
        ERRORS.append("CSS missing #zen-fake-transparency-live-browser live background styles")

    if re.search(
        r'-moz-pref\("zen\.fake_transparency\.transparency_level",\s*\d+\)',
        css,
    ):
        ERRORS.append("transparency_level must not use numeric -moz-pref (Zen uses string dropdown prefs)")

    if "background-color:" in css and re.search(
        r"background-image:\s*var\(--zen-fake_transparency-wallpaper_url",
        css,
    ):
        ERRORS.append("opaque wallpaper would hide tint; use layered background-image")

    pref_props = {p["property"] for p in prefs}
    for required in (
        "zen.fake_transparency.live_background_enabled",
        "zen.fake_transparency.live_background_url",
    ):
        if required not in pref_props:
            ERRORS.append(f"preferences.json missing {required}")

    for fn in (
        "Find-ZenProfile",
        "Get-WallpaperEnginePath",
        "Set-ZenPref",
        "ConvertTo-CssFileUrl",
        "Resolve-LiveBackgroundPath",
        "ConvertTo-FileUri",
    ):
        if fn not in ps:
            ERRORS.append(f"sync-wallpaper.ps1 missing function {fn}")

    live_window_ps = ROOT / "scripts/windows/sync-live-window.ps1"
    if not live_window_ps.exists():
        ERRORS.append("scripts/windows/sync-live-window.ps1 missing (WE playInWindow spike)")
    else:
        lw = live_window_ps.read_text()
        if "playInWindow" not in lw:
            ERRORS.append("sync-live-window.ps1 must reference playInWindow")

    for p in prefs:
        if p["type"] == "checkbox" and p["property"] not in css and p["property"] not in (
            "zen.fake_transparency.enabled",
            "zen.fake_transparency.live_background_enabled",
        ):
            WARNINGS.append(f"checkbox pref not in CSS: {p['property']}")

    print(f"errors: {len(ERRORS)}")
    for e in ERRORS:
        print(f"  ERROR: {e}")
    print(f"warnings: {len(WARNINGS)}")
    for w in WARNINGS:
        print(f"  WARN: {w}")

    return 1 if ERRORS else 0


if __name__ == "__main__":
    sys.exit(main())
