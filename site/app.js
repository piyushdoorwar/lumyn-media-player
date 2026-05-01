const repo = "piyushdoorwar/lumyn-media-player";

const linuxLink = document.querySelector("#linuxDownloadLink");
const windowsLink = document.querySelector("#windowsDownloadLink");
const macosLink = document.querySelector("#macosDownloadLink");
const heroDownloadLink = document.querySelector("#downloadLink");

heroDownloadLink.href = "#download";

function enableDownload(link, url) {
  link.href = url;
  link.classList.remove("disabled");
  link.removeAttribute("aria-disabled");
}

async function hydrateDownloadLinks() {
  try {
    const response = await fetch(`https://api.github.com/repos/${repo}/releases?per_page=10`, {
      headers: { Accept: "application/vnd.github+json" },
    });
    if (!response.ok) return;

    const releases = await response.json();
    const release = releases.find((item) => !item.draft);
    if (!release?.assets?.length) return;

    const linuxAsset = release.assets.find((asset) => /_amd64\.deb$/i.test(asset.name));
    const windowsAsset = release.assets.find((asset) => /win-x64.*_setup\.exe$/i.test(asset.name)) ??
      release.assets.find((asset) => /win-x64\.zip$/i.test(asset.name));
    const macosAsset = release.assets.find((asset) => /macos-arm64\.dmg$/i.test(asset.name)) ??
      release.assets.find((asset) => /macos-x64\.dmg$/i.test(asset.name)) ??
      release.assets.find((asset) => /macos-arm64\.zip$/i.test(asset.name)) ??
      release.assets.find((asset) => /macos-x64\.zip$/i.test(asset.name));

    if (linuxAsset?.browser_download_url) {
      enableDownload(linuxLink, linuxAsset.browser_download_url);
    }

    if (windowsAsset?.browser_download_url) {
      enableDownload(windowsLink, windowsAsset.browser_download_url);
    }

    if (macosAsset?.browser_download_url) {
      enableDownload(macosLink, macosAsset.browser_download_url);
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
