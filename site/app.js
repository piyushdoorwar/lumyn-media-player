const repo = "piyushdoorwar/lumyn-media-player";

const linuxLink = document.querySelector("#linuxDownloadLink");
const windowsLink = document.querySelector("#windowsDownloadLink");
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
    const windowsAsset = release.assets.find((asset) => /win-x64\.zip$/i.test(asset.name));

    if (linuxAsset?.browser_download_url) {
      enableDownload(linuxLink, linuxAsset.browser_download_url);
    }

    if (windowsAsset?.browser_download_url) {
      enableDownload(windowsLink, windowsAsset.browser_download_url);
    }
  } catch {
    // Keep the buttons disabled if GitHub is unreachable or matching assets are absent.
  }
}

hydrateDownloadLinks();
