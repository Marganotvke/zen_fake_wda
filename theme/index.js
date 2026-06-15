// Zen Fake Transparency — live HTML/WebGL background loader (Sine / fx-autoconfig)
// Embeds a <browser> in #zen-browser-background when live_background_enabled is true.

(function () {
  const PREF_ENABLED = "zen.fake_transparency.enabled";
  const PREF_LIVE_ENABLED = "zen.fake_transparency.live_background_enabled";
  const PREF_LIVE_URL = "zen.fake_transparency.live_background_url";
  const BROWSER_ID = "zen-fake-transparency-live-browser";
  const MAX_MOUNT_RETRIES = 120;
  let mountRetries = 0;

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

  function normalizeLiveUrl(raw) {
    if (!raw) {
      return "";
    }
    const trimmed = raw.trim();
    const cssUrlMatch = trimmed.match(/^url\(["']?(.+?)["']?\)$/i);
    return cssUrlMatch ? cssUrlMatch[1] : trimmed;
  }

  function removeLiveBrowser() {
    const existing = document.getElementById(BROWSER_ID);
    if (existing) {
      existing.remove();
    }
  }

  function shouldRun() {
    return (
      prefBool(PREF_ENABLED, false) &&
      prefBool(PREF_LIVE_ENABLED, false) &&
      !document.documentElement.hasAttribute("inFullscreen")
    );
  }

  function mountLiveBrowser() {
    const bg = document.getElementById("zen-browser-background");
    if (!bg) {
      return;
    }

    const liveUrl = normalizeLiveUrl(prefString(PREF_LIVE_URL, ""));
    if (!liveUrl) {
      removeLiveBrowser();
      return;
    }

    let browser = document.getElementById(BROWSER_ID);
    if (!browser) {
      browser = document.createXULElement("browser");
      browser.id = BROWSER_ID;
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

  function refresh() {
    if (!shouldRun()) {
      removeLiveBrowser();
      return;
    }
    if (!document.getElementById("zen-browser-background")) {
      if (mountRetries++ < MAX_MOUNT_RETRIES) {
        requestAnimationFrame(refresh);
      }
      return;
    }
    mountRetries = 0;
    mountLiveBrowser();
  }

  const prefObserver = {
    observe(_subject, topic) {
      if (topic === "nsPref:changed") {
        refresh();
      }
    },
  };

  const PREF_BRANCH = "zen.fake_transparency.";

  function observePrefs() {
    Services.prefs.addObserver(PREF_BRANCH, prefObserver);
    window.addEventListener("unload", () => {
      Services.prefs.removeObserver(PREF_BRANCH, prefObserver);
    });
  }

  function observeFullscreen() {
    const observer = new MutationObserver(refresh);
    observer.observe(document.documentElement, {
      attributes: true,
      attributeFilter: ["inFullscreen"],
    });
    window.addEventListener("unload", () => observer.disconnect());
  }

  if (document.readyState === "complete") {
    refresh();
  } else {
    window.addEventListener("load", refresh, { once: true });
  }

  window.addEventListener("resize", refresh);
  observePrefs();
  observeFullscreen();
})();
