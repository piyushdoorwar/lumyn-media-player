const releaseDownloadUrl = "https://github.com/piyushdoorwar/lumyn-media-player/releases/download/v0.1.4/lumyn_0.1.4_amd64.deb";

const fallbackReleaseUrl = "https://github.com/piyushdoorwar/lumyn-media-player/releases/latest";
const downloadUrl = releaseDownloadUrl || fallbackReleaseUrl;

for (const link of document.querySelectorAll("#downloadLink, #releaseDownloadLink")) {
  link.href = downloadUrl;
}
