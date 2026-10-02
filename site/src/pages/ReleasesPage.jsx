import { useEffect, useMemo, useRef, useState } from "react";
import { AnimatePresence, motion } from "framer-motion";
import { GITHUB_URL } from "../lib/assets.js";
import { fetchReleases, linuxAsset, windowsAsset } from "../lib/releases.js";
import { listContainer, listItem } from "../motion/motion.js";
import TopBar from "../components/TopBar.jsx";
import Footer from "../components/Footer.jsx";
import SupportModal from "../components/SupportModal.jsx";
import Icon from "../components/Icon.jsx";

const PER_PAGE = 10;

const OS_TABS = [
  { os: "all", label: "All" },
  { os: "linux", label: "Linux", icon: "ubuntu" },
  { os: "windows", label: "Windows", icon: "windows" },
];

const FINDERS = { linux: linuxAsset, windows: windowsAsset };

function formatDate(iso) {
  return new Date(iso).toLocaleDateString("en-GB", { day: "numeric", month: "short", year: "numeric" });
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

function DownloadButton({ asset, icon }) {
  if (!asset) return null;
  const ext = asset.name.split(".").pop().toLowerCase();
  return (
    <a className="btn btn-secondary btn-sm" href={asset.browser_download_url} title={`Download ${asset.name}`}>
      <Icon name={icon} />.{ext}
    </a>
  );
}

export default function ReleasesPage() {
  const [status, setStatus] = useState("loading"); // loading | error | ready
  const [releases, setReleases] = useState([]);
  const [os, setOS] = useState("all");
  const [stableOnly, setStableOnly] = useState(true);
  const [page, setPage] = useState(1);
  const [supportOpen, setSupportOpen] = useState(false);
  const tabs = useRef([]);
  const listTop = useRef(null);

  useEffect(() => {
    fetchReleases()
      .then((all) => {
        setReleases(all);
        setStatus("ready");
      })
      .catch(() => setStatus("error"));
  }, []);

  const filtered = useMemo(
    () =>
      releases.filter(
        (r) => (os === "all" || !!FINDERS[os](r)) && (!stableOnly || !r.prerelease)
      ),
    [releases, os, stableOnly]
  );
  const latestStableId = useMemo(() => filtered.find((r) => !r.prerelease)?.id, [filtered]);
  const totalPages = Math.max(1, Math.ceil(filtered.length / PER_PAGE));
  const current = Math.min(page, totalPages);
  const pageItems = filtered.slice((current - 1) * PER_PAGE, current * PER_PAGE);

  const selectOS = (next) => {
    setOS(next);
    setPage(1);
  };
  const onTabKey = (e, index) => {
    const dir = e.key === "ArrowRight" ? 1 : e.key === "ArrowLeft" ? -1 : 0;
    if (!dir) return;
    e.preventDefault();
    const next = (index + dir + OS_TABS.length) % OS_TABS.length;
    selectOS(OS_TABS[next].os);
    tabs.current[next]?.focus();
  };
  const goTo = (n) => {
    setPage(n);
    listTop.current?.scrollIntoView({ block: "start" });
  };

  const showLinux = os !== "windows";
  const showWindows = os !== "linux";

  return (
    <>
      <TopBar current="releases" />

      <main id="main">
        <header className="page-head">
          <div className="wrap" ref={listTop}>
            <p className="eyebrow">All versions</p>
            <h1>Releases</h1>
            <p className="lede">Download any published build. Stable releases are shown by default.</p>
            <div className="toolbar">
              <div className="tabs" role="tablist" aria-label="Filter by operating system">
                {OS_TABS.map((t, i) => (
                  <button
                    key={t.os}
                    ref={(el) => (tabs.current[i] = el)}
                    className="tab"
                    type="button"
                    role="tab"
                    aria-selected={os === t.os}
                    aria-controls="release-list"
                    tabIndex={os === t.os ? 0 : -1}
                    onClick={() => selectOS(t.os)}
                    onKeyDown={(e) => onTabKey(e, i)}
                  >
                    {t.icon && <Icon name={t.icon} />}
                    {t.label}
                  </button>
                ))}
              </div>
              <label className="switch">
                <input
                  type="checkbox"
                  checked={stableOnly}
                  onChange={(e) => {
                    setStableOnly(e.target.checked);
                    setPage(1);
                  }}
                />
                <span className="track" aria-hidden="true" />
                Stable only
              </label>
            </div>
          </div>
        </header>

        <div className="wrap">
          <div id="release-list" role="tabpanel" aria-live="polite">
            {status === "loading" && (
              <div className="state">
                <span className="spinner" aria-hidden="true" />
                Fetching releases…
              </div>
            )}
            {status === "error" && (
              <div className="state">
                Could not load releases.
                <a className="btn btn-secondary btn-sm" href={`${GITHUB_URL}/releases`} rel="noreferrer">
                  <Icon name="github" />
                  View on GitHub
                </a>
              </div>
            )}
            {status === "ready" && filtered.length === 0 && (
              <div className="state">No releases for this platform yet.</div>
            )}
            {status === "ready" && filtered.length > 0 && (
              <AnimatePresence mode="wait">
                <motion.div
                  key={`${os}-${stableOnly}-${current}`}
                  className="release-list"
                  variants={listContainer}
                  initial="hidden"
                  animate="show"
                >
                  {pageItems.map((r) => {
                    const linux = showLinux ? linuxAsset(r) : null;
                    const windows = showWindows ? windowsAsset(r) : null;
                    return (
                      <motion.article className="release" key={r.id} variants={listItem}>
                        <div>
                          <div className="release-tag">
                            <span className="release-version">{r.tag_name}</span>
                            {r.id === latestStableId && <span className="badge">Latest</span>}
                            {r.prerelease && <span className="badge warn">Pre-release</span>}
                          </div>
                          <time className="release-date" dateTime={r.published_at} title={formatDate(r.published_at)}>
                            {timeAgo(r.published_at)} · {formatDate(r.published_at)}
                          </time>
                        </div>
                        <div className="release-downloads">
                          {linux || windows ? (
                            <>
                              <DownloadButton asset={linux} icon="ubuntu" />
                              <DownloadButton asset={windows} icon="windows" />
                            </>
                          ) : (
                            <a className="btn btn-secondary btn-sm" href={r.html_url} rel="noreferrer">
                              <Icon name="github" />
                              View on GitHub
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
            <nav className="pagination" aria-label="Releases pages">
              <button
                type="button"
                className="btn btn-secondary btn-sm"
                disabled={current <= 1}
                onClick={() => goTo(current - 1)}
              >
                <Icon name="arrowLeft" />
                Newer
              </button>
              <span>
                Page {current} of {totalPages}
              </span>
              <button
                type="button"
                className="btn btn-secondary btn-sm"
                disabled={current >= totalPages}
                onClick={() => goTo(current + 1)}
              >
                Older
                <Icon name="arrowRight" />
              </button>
            </nav>
          )}
          {!(status === "ready" && totalPages > 1) && <div className="page-end" />}
        </div>
      </main>

      <Footer onSupport={() => setSupportOpen(true)} />
      <SupportModal open={supportOpen} onClose={() => setSupportOpen(false)} />
    </>
  );
}
