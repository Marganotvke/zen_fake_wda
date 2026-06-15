// Zen Fake Transparency — background controller (Sine / fx-autoconfig)
// Modes: static (CSS wallpaper), live embed, desktop capture (ZenFakeCapture.exe).

(function () {
  const { Subprocess } = ChromeUtils.importESModule(
    "resource://gre/modules/Subprocess.sys.mjs"
  );

  const PREF_BRANCH = "zen.fake_transparency.";
  const PREF_ENABLED = "zen.fake_transparency.enabled";
  const PREF_MODE = "zen.fake_transparency.background_mode";
  const PREF_LIVE_ENABLED = "zen.fake_transparency.live_background_enabled";
  const PREF_DESKTOP_ENABLED = "zen.fake_transparency.desktop_capture_enabled";
  const PREF_LIVE_URL = "zen.fake_transparency.live_background_url";
  const PREF_CAPTURE_EXE = "zen.fake_transparency.capture_exe_path";
  const PREF_CAPTURE_URL = "zen.fake_transparency.capture_helper_url";
  const PREF_FPS = "zen.fake_transparency.background_fps";

  const MODE_STATIC = "static";
  const MODE_LIVE = "live";
  const MODE_DESKTOP = "desktop";

  const LIVE_BROWSER_ID = "zen-fake-transparency-live-browser";
  const CAPTURE_IMG_ID = "zen-fake-transparency-capture-img";
  const MAX_MOUNT_RETRIES = 120;
  const RESIZE_DEBOUNCE_MS = 300;

  // Prefs that require index.js to remount/spawn (CSS-only prefs are ignored).
  const JS_RELEVANT_PREFS = new Set([
    "enabled",
    "background_mode",
    "live_background_enabled",
    "desktop_capture_enabled",
    "live_background_url",
    "capture_exe_path",
    "capture_helper_url",
    "background_fps",
    "capture_scale",
  ]);

  let mountRetries = 0;
  let captureProcess = null;
  let modeSyncInProgress = false;
  let lastCaptureSignature = "";

  function prefBool(name, fallback = false) {
    try {
      return Services.prefs.getBoolPref(name, fallback);
    } catch {
      return fallback;
    }
  }

  function prefString(name, fallback = "") {
    try {
      return Services.prefs.getStringPref(name, fallback);
    } catch {
      return fallback;
    }
  }

  function prefInt(name, fallback) {
    try {
      return Services.prefs.getIntPref(name, fallback);
    } catch {
      const parsed = parseInt(prefString(name, String(fallback)), 10);
      return Number.isFinite(parsed) ? parsed : fallback;
    }
  }

  function setBoolPref(name, value) {
    try {
      try {
        if (Services.prefs.getBoolPref(name) === value) {
          return;
        }
      } catch {
        // Pref not set yet — write below.
      }
      Services.prefs.setBoolPref(name, value);
    } catch (ex) {
      console.error("zen_fake: failed to set pref", name, ex);
    }
  }

  function getProfilePath() {
    return Services.dirsvc.get("ProfD", Ci.nsIFile).path;
  }

  function normalizeUrl(raw) {
    if (!raw) {
      return "";
    }
    const trimmed = raw.trim();
    const cssUrlMatch = trimmed.match(/^url\(["']?(.+?)["']?\)$/i);
    return cssUrlMatch ? cssUrlMatch[1] : trimmed;
  }

  function resolveBackgroundMode() {
    const mode = prefString(PREF_MODE, "");
    if (mode === MODE_LIVE || mode === MODE_DESKTOP || mode === MODE_STATIC) {
      return mode;
    }
    if (prefBool(PREF_DESKTOP_ENABLED, false)) {
      return MODE_DESKTOP;
    }
    if (prefBool(PREF_LIVE_ENABLED, false)) {
      return MODE_LIVE;
    }
    return MODE_STATIC;
  }

  function syncModePrefsFromBackgroundMode() {
    if (modeSyncInProgress) {
      return;
    }
    modeSyncInProgress = true;
    const mode = resolveBackgroundMode();
    setBoolPref(PREF_LIVE_ENABLED, mode === MODE_LIVE);
    setBoolPref(PREF_DESKTOP_ENABLED, mode === MODE_DESKTOP);
    modeSyncInProgress = false;
  }

  function migrateLegacyModePref() {
    const mode = prefString(PREF_MODE, "");
    if (mode) {
      return;
    }
    if (prefBool(PREF_DESKTOP_ENABLED, false)) {
      Services.prefs.setStringPref(PREF_MODE, MODE_DESKTOP);
    } else if (prefBool(PREF_LIVE_ENABLED, false)) {
      Services.prefs.setStringPref(PREF_MODE, MODE_LIVE);
    } else {
      Services.prefs.setStringPref(PREF_MODE, MODE_STATIC);
    }
  }

  function removeLiveBrowser() {
    document.getElementById(LIVE_BROWSER_ID)?.remove();
  }

  function removeCaptureImg() {
    document.getElementById(CAPTURE_IMG_ID)?.remove();
  }

  async function shutdownCaptureCompanion() {
    const streamUrl = normalizeUrl(prefString(PREF_CAPTURE_URL, "http://127.0.0.1:8765"));
    try {
      await fetch(`${streamUrl}/shutdown`, { method: "POST", mode: "no-cors" });
    } catch {
      // companion may already be gone
    }

    if (captureProcess) {
      try {
        captureProcess.kill();
      } catch {
        // ignored
      }
      captureProcess = null;
    }
  }

  function parseCaptureStreamUrl() {
    const raw = normalizeUrl(prefString(PREF_CAPTURE_URL, "http://127.0.0.1:8765"));
    const base = raw.replace(/\/+$/, "");
    return `${base}/stream`;
  }

  function captureSignature() {
    return [
      prefString(PREF_CAPTURE_EXE, ""),
      prefString(PREF_CAPTURE_URL, "http://127.0.0.1:8765"),
      prefInt(PREF_FPS, 15),
      prefInt("zen.fake_transparency.capture_scale", 50),
    ].join("|");
  }

  function parseCaptureEndpoint(baseUrl) {
    try {
      const url = new URL(baseUrl);
      return {
        host: url.hostname || "127.0.0.1",
        port: url.port || (url.protocol === "https:" ? "443" : "8765"),
      };
    } catch {
      return { host: "127.0.0.1", port: "8765" };
    }
  }

  function watchCaptureProcessExit(proc) {
    proc.wait().then(
      () => {
        if (captureProcess === proc) {
          captureProcess = null;
        }
      },
      () => {
        if (captureProcess === proc) {
          captureProcess = null;
        }
      }
    );
  }

  async function spawnCaptureCompanion() {
    const exePath = prefString(PREF_CAPTURE_EXE, "");
    if (!exePath) {
      console.warn("zen_fake: desktop capture enabled but capture_exe_path is empty");
      return;
    }

    if (captureProcess) {
      return;
    }

    const watchPid = Services.appinfo.processID;
    const profilePath = getProfilePath();
    const streamUrl = normalizeUrl(prefString(PREF_CAPTURE_URL, "http://127.0.0.1:8765"));
    const { host, port } = parseCaptureEndpoint(streamUrl);
    const fps = prefInt(PREF_FPS, 15);
    const scale = prefInt("zen.fake_transparency.capture_scale", 50);

    const args = [
      "--watch-pid",
      String(watchPid),
      "--profile",
      profilePath,
      "--no-tray",
      "--host",
      host,
      "--port",
      port,
      "--fps",
      String(fps),
      "--scale",
      String(scale),
    ];

    try {
      const proc = await Subprocess.call({
        command: exePath,
        arguments: args,
        stderr: "stdout",
        workdir: Services.dirsvc.get("TmpD", Ci.nsIFile).path,
      });
      captureProcess = proc;
      watchCaptureProcessExit(proc);
      proc.stdout.read().catch(() => {});
    } catch (ex) {
      console.error("zen_fake: failed to spawn ZenFakeCapture", ex);
      captureProcess = null;
    }
  }

  function mountLiveBrowser(bg) {
    const liveUrl = normalizeLiveUrl(prefString(PREF_LIVE_URL, ""));
    if (!liveUrl) {
      removeLiveBrowser();
      return;
    }

    let browser = document.getElementById(LIVE_BROWSER_ID);
    if (!browser) {
      browser = document.createXULElement("browser");
      browser.id = LIVE_BROWSER_ID;
      browser.setAttribute("type", "content");
      browser.setAttribute("disablehistory", "true");
      browser.setAttribute("disablefullscreen", "true");
      browser.setAttribute("autoscroll", "false");
      browser.setAttribute("tooltip", "aHTMLTooltip");
      browser.setAttribute("flex", "1");
      browser.setAttribute("maychangeremoteness", "true");
      bg.appendChild(browser);
    }

    if (browser.getAttribute("src") !== liveUrl) {
      browser.setAttribute("src", liveUrl);
    }
  }

  function normalizeLiveUrl(raw) {
    return normalizeUrl(raw);
  }

  function mountCaptureImg(bg) {
    const streamUrl = parseCaptureStreamUrl();
    let img = document.getElementById(CAPTURE_IMG_ID);
    if (!img) {
      img = document.createElement("img");
      img.id = CAPTURE_IMG_ID;
      img.alt = "";
      img.setAttribute("aria-hidden", "true");
      bg.appendChild(img);
    }

    if (!img.src || !img.src.startsWith(streamUrl.split("?")[0])) {
      img.src = `${streamUrl}?t=${Date.now()}`;
    }
  }

  function shouldRun() {
    return (
      prefBool(PREF_ENABLED, false) &&
      !document.documentElement.hasAttribute("inFullscreen")
    );
  }

  let refreshInFlight = null;

  async function refresh() {
    if (refreshInFlight) {
      return refreshInFlight;
    }
    refreshInFlight = refreshInner();
    try {
      await refreshInFlight;
    } finally {
      refreshInFlight = null;
    }
  }

  async function refreshInner() {
    const mode = resolveBackgroundMode();

    if (!shouldRun() || mode === MODE_STATIC) {
      removeLiveBrowser();
      removeCaptureImg();
      await shutdownCaptureCompanion();
      return;
    }

    const bg = document.getElementById("zen-browser-background");
    if (!bg) {
      if (mountRetries++ < MAX_MOUNT_RETRIES) {
        requestAnimationFrame(refresh);
      }
      return;
    }
    mountRetries = 0;

    if (mode === MODE_LIVE) {
      await shutdownCaptureCompanion();
      removeCaptureImg();
      mountLiveBrowser(bg);
      return;
    }

    if (mode === MODE_DESKTOP) {
      removeLiveBrowser();
      const sig = captureSignature();
      if (captureProcess && sig !== lastCaptureSignature) {
        await shutdownCaptureCompanion();
      }
      lastCaptureSignature = sig;
      await spawnCaptureCompanion();
      if (captureProcess) {
        mountCaptureImg(bg);
      } else {
        removeCaptureImg();
      }
    }
  }

  const prefObserver = {
    observe(_subject, topic, data) {
      if (topic !== "nsPref:changed") {
        return;
      }
      if (data && !JS_RELEVANT_PREFS.has(data)) {
        return;
      }
      if (data === "background_mode") {
        syncModePrefsFromBackgroundMode();
      }
      scheduleRefresh();
    },
  };

  let refreshTimer = null;
  let resizeTimer = null;

  function scheduleRefresh(delayMs = 0) {
    if (refreshTimer) {
      clearTimeout(refreshTimer);
    }
    refreshTimer = setTimeout(() => {
      refreshTimer = null;
      refresh();
    }, delayMs);
  }

  function observePrefs() {
    Services.prefs.addObserver(PREF_BRANCH, prefObserver);
    window.addEventListener("unload", async () => {
      Services.prefs.removeObserver(PREF_BRANCH, prefObserver);
      await shutdownCaptureCompanion();
    });
  }

  function observeFullscreen() {
    const observer = new MutationObserver(scheduleRefresh);
    observer.observe(document.documentElement, {
      attributes: true,
      attributeFilter: ["inFullscreen"],
    });
    window.addEventListener("unload", () => observer.disconnect());
  }

  migrateLegacyModePref();
  syncModePrefsFromBackgroundMode();

  if (document.readyState === "complete") {
    refresh();
  } else {
    window.addEventListener("load", () => refresh(), { once: true });
  }

  window.addEventListener("resize", () => {
    if (resizeTimer) {
      clearTimeout(resizeTimer);
    }
    resizeTimer = setTimeout(() => {
      resizeTimer = null;
      scheduleRefresh();
    }, RESIZE_DEBOUNCE_MS);
  });
  observePrefs();
  observeFullscreen();
})();
