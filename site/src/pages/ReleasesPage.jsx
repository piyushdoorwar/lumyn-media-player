import { useEffect, useMemo, useState } from "react";
import { AnimatePresence, motion } from "framer-motion";
import { asset, BASE, GITHUB_URL } from "../lib/assets.js";
import { listContainer, listItem } from "../motion/motion.js";
import TopBar from "../components/TopBar.jsx";
import Footer from "../components/Footer.jsx";
import SupportModal from "../components/SupportModal.jsx";

const PER_PAGE = 10;

function linuxAsset(release) {
  return release.assets.find((a) => /_amd64\.deb$/i.test(a.name));
}
function windowsAsset(release) {
  return (
    release.assets.find((a) => /win-x64.*_setup\.exe$/i.test(a.name)) ??
    release.assets.find((a) => /win-x64\.exe$/i.test(a.name)) ??
    release.assets.find((a) => /win-x64\.zip$/i.test(a.name))
  );
}
function hasOsAsset(release, os) {
  if (os === "all") return true;
  if (os === "linux") return !!linuxAsset(release);
  if (os === "windows") return !!windowsAsset(release);
  return true;
}
function formatDate(iso) {
  return new Date(iso).toLocaleDateString("en-GB", {
    day: "numeric",
    month: "short",
    year: "numeric",
  });
}
function timeAgo(iso) {
  const seconds = Math.floor((Date.now() - new Date(iso)) / 1000);
  if (seconds < 60) return "just now";
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes}m ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours}h ago`;
  const days = Math.floor(hours / 24);
  if (days < 30) return `${days}d ago`;
  const months = Math.floor(days / 30);
  if (months < 12) return `${months}mo ago`;
  return `${Math.floor(months / 12)}y ago`;
}

const OS_TABS = [
  { os: "all", label: "All", icon: null },
  { os: "linux", label: "Linux", icon: "ubuntu.svg" },
  { os: "windows", label: "Windows", icon: "windows.svg" },
];

function DownloadButton({ asset: a, imgSrc }) {
  if (!a) return null;
  const ext = a.name.split(".").pop().toLowerCase();
  const label = ext === "exe" ? ".exe" : ext === "deb" ? ".deb" : `.${ext}`;
  return (
    <a
      className="button secondary release-dl-btn"
      href={a.browser_download_url}
      download
      title={`Download ${a.name}`}
    >
      <img src={imgSrc} alt="" />
      <span>{label}</span>
    </a>
  );
}

export default function ReleasesPage() {
  const [status, setStatus] = useState("loading"); // loading | error | ready
  const [allReleases, setAllReleases] = useState([]);
  const [currentOS, setCurrentOS] = useState("all");
  const [stableOnly, setStableOnly] = useState(true);
  const [currentPage, setCurrentPage] = useState(1);
  const [supportOpen, setSupportOpen] = useState(false);

  useEffect(() => {
    (async () => {
      try {
        const res = await fetch(`${BASE}releases.json`);
        if (!res.ok) throw new Error(`Release manifest ${res.status}`);
        const results = await res.json();
        results.sort((a, b) => new Date(b.published_at) - new Date(a.published_at));
        setAllReleases(results);
        setStatus("ready");
      } catch {
        setStatus("error");
      }
    })();
  }, []);

  const filtered = useMemo(
    () =>
      allReleases.filter(
        (r) => hasOsAsset(r, currentOS) && (!stableOnly || !r.prerelease)
      ),
    [allReleases, currentOS, stableOnly]
  );
  const latestStable = useMemo(() => filtered.find((r) => !r.prerelease), [filtered]);
  const totalPages = Math.max(1, Math.ceil(filtered.length / PER_PAGE));
  const page = Math.min(currentPage, totalPages);
  const pageItems = filtered.slice((page - 1) * PER_PAGE, (page - 1) * PER_PAGE + PER_PAGE);

  const selectOS = (os, index) => (e) => {
    if (e?.key) {
      const dir = e.key === "ArrowRight" ? 1 : e.key === "ArrowLeft" ? -1 : 0;
      if (!dir) return;
      e.preventDefault();
      const next = OS_TABS[(index + dir + OS_TABS.length) % OS_TABS.length];
      setCurrentOS(next.os);
      setCurrentPage(1);
      return;
    }
    setCurrentOS(os);
    setCurrentPage(1);
  };

  return (
    <>
      <TopBar current="releases" />

      <main className="releases-main">
        <div className="releases-header">
          <div>
            <p className="eyebrow">All versions</p>
            <h1 className="releases-title">Releases</h1>
            <p className="releases-subtitle">
              Download any previous release. Stable builds are listed by default.
            </p>
          </div>

          <div className="releases-filters">
            <div className="os-tabs" role="tablist" aria-label="Filter by operating system">
              {OS_TABS.map((t, i) => (
                <button
                  key={t.os}
                  className={`os-tab${currentOS === t.os ? " active" : ""}`}
                  role="tab"
                  aria-selected={currentOS === t.os}
                  aria-controls="release-list"
                  onClick={selectOS(t.os, i)}
                  onKeyDown={selectOS(t.os, i)}
                >
                  {t.icon && <img src={asset(t.icon)} alt="" />} {t.label}
                </button>
              ))}
            </div>
            <span className="releases-filters-sep" aria-hidden="true"></span>
            <label className="stable-toggle">
              <input
                type="checkbox"
                checked={stableOnly}
                onChange={(e) => {
                  setStableOnly(e.target.checked);
                  setCurrentPage(1);
                }}
              />
              <span className="toggle-track">
                <span className="toggle-thumb"></span>
              </span>
              <span className="toggle-label">Stable only</span>
            </label>
          </div>
        </div>

        <div id="release-list" className="release-list">
          {status === "loading" && (
            <div className="releases-state">
              <span className="spinner" aria-hidden="true"></span>
              <span>Fetching releases…</span>
            </div>
          )}
          {status === "error" && (
            <div className="releases-state">
              Could not load releases.{" "}
              <a className="github-link" href={`${GITHUB_URL}/releases`} rel="noreferrer">
                <img src={asset("github.svg")} alt="" />
                <span>View on GitHub</span>
              </a>
            </div>
          )}
          {status === "ready" && filtered.length === 0 && (
            <div className="releases-state">No releases found for this platform yet.</div>
          )}
          {status === "ready" && filtered.length > 0 && (
            <AnimatePresence mode="wait">
              <motion.div
                key={`${currentOS}-${stableOnly}-${page}`}
                variants={listContainer}
                initial="hidden"
                animate="show"
              >
                {pageItems.map((release) => {
                  const isLatest = latestStable?.id === release.id;
                  const linux = linuxAsset(release);
                  const windows = windowsAsset(release);
                  const showLinux = currentOS === "all" || currentOS === "linux";
                  const showWindows = currentOS === "all" || currentOS === "windows";
                  const hasDownloads =
                    (showLinux && linux) || (showWindows && windows);
                  return (
                    <motion.article
                      className="release-item"
                      key={release.id}
                      variants={listItem}
                    >
                      <div className="release-meta">
                        <div className="release-tag-row">
                          <span className="release-version">{release.tag_name}</span>
                          {isLatest && <span className="badge-latest">Latest</span>}
                          {release.prerelease && (
                            <span className="badge-pre">Pre-release</span>
                          )}
                        </div>
                        <time
                          className="release-date"
                          dateTime={release.published_at}
                          title={formatDate(release.published_at)}
                        >
                          {timeAgo(release.published_at)} · {formatDate(release.published_at)}
                        </time>
                      </div>
                      <div className="release-downloads">
                        {hasDownloads ? (
                          <>
                            {showLinux && (
                              <DownloadButton asset={linux} imgSrc={asset("ubuntu.svg")} />
                            )}
                            {showWindows && (
                              <DownloadButton asset={windows} imgSrc={asset("windows.svg")} />
                            )}
                          </>
                        ) : (
                          <a
                            className="release-gh-link github-link"
                            href={release.html_url}
                            rel="noreferrer"
                          >
                            <img src={asset("github.svg")} alt="" />
                            <span>View on GitHub</span>
                          </a>
                        )}
                      </div>
                    </motion.article>
                  );
                })}
              </motion.div>
            </AnimatePresence>
          )}
        </div>

        {status === "ready" && totalPages > 1 && (
          <nav className="pagination" aria-label="Releases pagination">
            <button
              className="page-btn"
              aria-label="Previous page"
              disabled={page <= 1}
              onClick={() => {
                setCurrentPage((p) => Math.max(1, p - 1));
                window.scrollTo(0, 0);
              }}
            >
              ← Prev
            </button>
            <span className="page-label">
              Page {page} of {totalPages}
            </span>
            <button
              className="page-btn"
              aria-label="Next page"
              disabled={page >= totalPages}
              onClick={() => {
                setCurrentPage((p) => Math.min(totalPages, p + 1));
                window.scrollTo(0, 0);
              }}
            >
              Next →
            </button>
          </nav>
        )}
      </main>

      <Footer variant="releases" onSupport={() => setSupportOpen(true)} />
      <SupportModal open={supportOpen} onClose={() => setSupportOpen(false)} />
    </>
  );
}
