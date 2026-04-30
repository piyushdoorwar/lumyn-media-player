const releaseDownloadUrl = "";

const fallbackReleaseUrl = "https://github.com/piyushdoorwar/lumyn-media-player/releases/latest";
const downloadUrl = releaseDownloadUrl || fallbackReleaseUrl;

for (const link of document.querySelectorAll("#downloadLink, #releaseDownloadLink")) {
  link.href = downloadUrl;
}
