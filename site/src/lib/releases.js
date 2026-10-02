import { BASE } from "./assets.js";

// Asset matchers shared by the download section and the releases page.
export const linuxAsset = (release, arch = "amd64") =>
  release.assets.find((a) => a.name.toLowerCase().endsWith(`_${arch}.deb`));

export const hasLinuxAsset = (release) => !!(linuxAsset(release) || linuxAsset(release, "arm64"));

export const windowsAsset = (release) =>
  release.assets.find((a) => /win-x64.*_setup\.exe$/i.test(a.name)) ??
  release.assets.find((a) => /win-x64\.exe$/i.test(a.name)) ??
  release.assets.find((a) => /win-x64\.zip$/i.test(a.name));

// Newest first. The manifest is generated at deploy time from the GitHub API.
export async function fetchReleases() {
  const res = await fetch(`${BASE}releases.json`);
  if (!res.ok) throw new Error(`Release manifest ${res.status}`);
  const releases = await res.json();
  return releases
    .filter((r) => !r.draft && Array.isArray(r.assets))
    .sort((a, b) => new Date(b.published_at) - new Date(a.published_at));
}
