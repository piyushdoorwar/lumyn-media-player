const linuxLink = document.querySelector("#linuxDownloadLink");
const windowsLink = document.querySelector("#windowsDownloadLink");
const heroDownloadLink = document.querySelector("#downloadLink");
const downloadTabs = Array.from(document.querySelectorAll("[data-download-tab]"));
const downloadPanels = Array.from(document.querySelectorAll("[data-download-panel]"));

heroDownloadLink.href = "#download";

function selectDownloadTab(os) {
  downloadTabs.forEach((tab) => {
    const active = tab.dataset.downloadTab === os;
    tab.setAttribute("aria-selected", active ? "true" : "false");
    tab.tabIndex = active ? 0 : -1;
  });

  downloadPanels.forEach((panel) => {
    const active = panel.dataset.downloadPanel === os;
    panel.hidden = !active;
    // Collapse any open terminal-install details when switching away
    if (!active) {
      panel.querySelectorAll("details.terminal-install[open]").forEach(d => d.removeAttribute("open"));
    }
  });
}

function detectedDownloadOS() {
  const platform = [
    navigator.userAgentData?.platform,
    navigator.platform,
    navigator.userAgent,
  ].filter(Boolean).join(" ").toLowerCase();

  if (platform.includes("win")) return "windows";
  return "linux";
}

downloadTabs.forEach((tab, index) => {
  tab.addEventListener("click", () => selectDownloadTab(tab.dataset.downloadTab));
  tab.addEventListener("keydown", (event) => {
    const dir = event.key === "ArrowRight" ? 1 : event.key === "ArrowLeft" ? -1 : 0;
    if (!dir) return;

    event.preventDefault();
    const next = downloadTabs[(index + dir + downloadTabs.length) % downloadTabs.length];
    selectDownloadTab(next.dataset.downloadTab);
    next.focus();
  });
});

selectDownloadTab(detectedDownloadOS());

function enableDownload(link, url) {
  link.href = url;
  link.classList.remove("disabled");
  link.removeAttribute("aria-disabled");
}

function linuxAsset(release) {
  return release.assets.find((asset) => /_amd64\.deb$/i.test(asset.name));
}

function windowsAsset(release) {
  return release.assets.find((asset) => /win-x64.*_setup\.exe$/i.test(asset.name));
}

function latestAssetWithInstaller(releases, findAsset) {
  for (const release of releases) {
    const asset = findAsset(release);
    if (asset?.browser_download_url) return asset;
  }
  return null;
}

async function hydrateDownloadLinks() {
  try {
    const response = await fetch("releases.json");
    if (!response.ok) return;

    const releases = await response.json();
    const stableReleases = releases
      .filter((item) => !item.draft && !item.prerelease && item.assets?.length)
      .sort((a, b) => new Date(b.published_at) - new Date(a.published_at));

    const linux = latestAssetWithInstaller(stableReleases, linuxAsset);
    const windows = latestAssetWithInstaller(stableReleases, windowsAsset);

    if (linux?.browser_download_url) {
      enableDownload(linuxLink, linux.browser_download_url);
    }

    if (windows?.browser_download_url) {
      enableDownload(windowsLink, windows.browser_download_url);
      const u = windows.browser_download_url;
      const fname = u.split("/").pop();
      document.getElementById("win-ps-cmd").textContent =
        `$url = "${u}"
$out = "$env:TEMP\\${fname}"
Invoke-WebRequest $url -OutFile $out
Start-Process $out`;
    }

  } catch {
    // Keep the buttons disabled if GitHub is unreachable or matching assets are absent.
  }
}

hydrateDownloadLinks();

// ── Scroll reveal ─────────────────────────────────────────────────────────
(function () {
  const obs = new IntersectionObserver(
    (entries) => {
      entries.forEach((entry) => {
        if (entry.isIntersecting) {
          entry.target.classList.add("revealed");
          obs.unobserve(entry.target);
        }
      });
    },
    { threshold: 0.1 }
  );
  document.querySelectorAll("[data-reveal]").forEach((el) => obs.observe(el));
})();

// ── Player preview: time ticker + progress bar ────────────────────────────
(function () {
  const timeEl = document.querySelector(".control-row span:first-child");
  const bar = document.querySelector(".timeline span");
  if (!timeEl || !bar) return;

  const startSec = 42;
  const endSec = 340;
  const minPct = 12;
  const maxPct = 55;
  let pos = startSec;

  function fmt(s) {
    const m = Math.floor(s / 60);
    const ss = s % 60;
    return `${String(m).padStart(2, "0")}:${String(ss).padStart(2, "0")}`;
  }

  bar.style.width = `${minPct}%`;

  function tick() {
    pos++;
    if (pos >= endSec) pos = startSec;
    const pct = (pos - startSec) / (endSec - startSec);
    timeEl.textContent = fmt(pos);
    bar.style.width = `${(minPct + pct * (maxPct - minPct)).toFixed(1)}%`;
    bar.style.transition = "width 0.95s linear";
  }

  setInterval(tick, 1000);
})();

// ── Player preview: subtitle rotator ─────────────────────────────────────
(function () {
  const el = document.querySelector(".subtitle");
  if (!el) return;

  const lines = [
    "Clean playback, readable subtitles, no clutter.",
    "Hardware decoded. Silky smooth.",
    "Drag and drop any file to start.",
    "Full subtitle track support built in.",
    "Loop, seek, screenshot — always one key away.",
  ];
  let i = 0;

  setInterval(() => {
    el.style.transition = "opacity 0.4s ease";
    el.style.opacity = "0";
    setTimeout(() => {
      i = (i + 1) % lines.length;
      el.textContent = lines[i];
      el.style.opacity = "1";
    }, 420);
  }, 4200);
})();
