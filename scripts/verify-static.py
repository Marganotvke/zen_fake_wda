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

    if BROWSER_XHTML.exists():
        xhtml = BROWSER_XHTML.read_text()
        for id_ in (
            "main-window",
            "zen-main-app-wrapper",
            "navigator-toolbox",
            "tabbrowser-tabpanels",
        ):
            if f'id="{id_}"' not in xhtml:
                WARNINGS.append(f'id="{id_}" not found in browser.xhtml')

    if "theme-Fake-Transparency" not in css:
        ERRORS.append("CSS must reference hidden mod div id theme-Fake-Transparency")

    if "--zen-fake_transparency-wallpaper_url" not in css:
        ERRORS.append("CSS missing --zen-fake_transparency-wallpaper_url")

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

    for fn in (
        "Find-ZenProfile",
        "Get-WallpaperEnginePath",
        "Set-ZenPref",
        "ConvertTo-CssFileUrl",
    ):
        if fn not in ps:
            ERRORS.append(f"sync-wallpaper.ps1 missing function {fn}")

    for p in prefs:
        if p["type"] == "checkbox" and p["property"] not in css:
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
