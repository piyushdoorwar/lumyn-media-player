import { useEffect, useRef, useState } from "react";
import { motion } from "framer-motion";
import { asset, BASE } from "../lib/assets.js";
import { fadeUp } from "../motion/motion.js";
import CopyButton from "./CopyButton.jsx";

const SNAP_CMD = "sudo snap install lumyn";
const PPA_CMD = `sudo add-apt-repository ppa:piyushdoorwar/lumyn
sudo apt update
sudo apt install lumyn`;

function detectedDownloadOS() {
  const platform = [
    navigator.userAgentData?.platform,
    navigator.platform,
    navigator.userAgent,
  ]
    .filter(Boolean)
    .join(" ")
    .toLowerCase();
  return platform.includes("win") ? "windows" : "linux";
}

function linuxAsset(release) {
  return release.assets.find((a) => /_amd64\.deb$/i.test(a.name));
}
function windowsAsset(release) {
  return release.assets.find((a) => /win-x64.*_setup\.exe$/i.test(a.name));
}
function latestAssetWithInstaller(releases, findAsset) {
  for (const release of releases) {
    const found = findAsset(release);
    if (found?.browser_download_url) return found;
  }
  return null;
}

function TerminalInstall({ children }) {
  const ref = useRef(null);
  const onToggle = () => {
    const details = ref.current;
    if (!details?.open) return;
    requestAnimationFrame(() => {
      const body = details.querySelector(".terminal-install-body");
      if (!body) return;
      const bottom = body.getBoundingClientRect().bottom;
      const vh = window.innerHeight;
      if (bottom > vh - 16) {
        window.scrollBy({ top: bottom - vh + 24, behavior: "smooth" });
      }
    });
  };
  return (
    <details className="terminal-install" ref={ref} onToggle={onToggle}>
      <summary>
        <svg className="terminal-chevron" viewBox="0 0 16 16" fill="none" aria-hidden="true">
          <path
            d="M4 6l4 4 4-4"
            stroke="currentColor"
            strokeWidth="1.6"
            strokeLinecap="round"
            strokeLinejoin="round"
          />
        </svg>
        Install via terminal
      </summary>
      <div className="terminal-install-body">{children}</div>
    </details>
  );
}

export default function DownloadSection() {
  const [activeOS, setActiveOS] = useState("linux");
  const [linuxUrl, setLinuxUrl] = useState(null);
  const [windowsUrl, setWindowsUrl] = useState(null);
  const [winPsCmd, setWinPsCmd] = useState("Fetching download URL…");
  const tabsRef = useRef([]);

  useEffect(() => {
    setActiveOS(detectedDownloadOS());

    (async () => {
      try {
        const res = await fetch(`${BASE}releases.json`);
        if (!res.ok) return;
        const releases = await res.json();
        const stable = releases
          .filter((item) => !item.draft && !item.prerelease && item.assets?.length)
          .sort((a, b) => new Date(b.published_at) - new Date(a.published_at));

        const linux = latestAssetWithInstaller(stable, linuxAsset);
        const windows = latestAssetWithInstaller(stable, windowsAsset);

        if (linux?.browser_download_url) setLinuxUrl(linux.browser_download_url);
        if (windows?.browser_download_url) {
          const u = windows.browser_download_url;
          setWindowsUrl(u);
          const fname = u.split("/").pop();
          setWinPsCmd(
            `$url = "${u}"
$out = "$env:TEMP\\${fname}"
Invoke-WebRequest $url -OutFile $out
Start-Process $out`
          );
        }
      } catch {
        // Leave buttons disabled if GitHub is unreachable.
      }
    })();
  }, []);

  const onTabKey = (e, index) => {
    const dir = e.key === "ArrowRight" ? 1 : e.key === "ArrowLeft" ? -1 : 0;
    if (!dir) return;
    e.preventDefault();
    const order = ["linux", "windows"];
    const next = order[(index + dir + order.length) % order.length];
    setActiveOS(next);
    tabsRef.current[order.indexOf(next)]?.focus();
  };

  const tab = (os, icon, label, index) => (
    <button
      className="download-tab"
      ref={(el) => (tabsRef.current[index] = el)}
      type="button"
      role="tab"
      aria-selected={activeOS === os}
      aria-controls={`download-panel-${os}`}
      tabIndex={activeOS === os ? 0 : -1}
      onClick={() => setActiveOS(os)}
      onKeyDown={(e) => onTabKey(e, index)}
    >
      <img src={asset(icon)} alt="" />
      <span>{label}</span>
    </button>
  );

  return (
    <section className="download" id="download">
      <motion.div
        variants={fadeUp}
        initial="hidden"
        whileInView="show"
        viewport={{ once: true, amount: 0.4 }}
      >
        <p className="eyebrow">Latest release</p>
        <h2>Get Lumyn for Ubuntu and Windows</h2>
        <p>
          Pick the installer that matches your desktop. Ubuntu and Windows users can
          install from their app stores or download a standalone package.
        </p>
      </motion.div>

      <motion.div
        className="download-actions"
        aria-label="Download Lumyn"
        variants={fadeUp}
        initial="hidden"
        whileInView="show"
        viewport={{ once: true, amount: 0.3 }}
      >
        <div className="download-tabs" role="tablist" aria-label="Choose operating system">
          {tab("linux", "ubuntu.svg", "Linux", 0)}
          {tab("windows", "windows.svg", "Windows", 1)}
        </div>

        <div className="download-panels-wrap">
          {activeOS === "linux" && (
            <article
              className="download-panel"
              id="download-panel-linux"
              role="tabpanel"
              aria-labelledby="download-tab-linux"
            >
              <div className="download-panel-copy">
                <h3>Ubuntu</h3>
                <p>Install from the Ubuntu App Center, or download the latest Debian package.</p>
              </div>
              <div className="download-options">
                <a
                  className="button primary platform-download"
                  href="https://snapcraft.io/lumyn"
                  target="_blank"
                  rel="noopener noreferrer"
                  aria-label="Install Lumyn from the Ubuntu App Center"
                >
                  <img src={asset("snapcraft.svg")} alt="" />
                  <span>Ubuntu App Center</span>
                </a>
                <a
                  className={`button secondary platform-download${linuxUrl ? "" : " disabled"}`}
                  {...(linuxUrl
                    ? { href: linuxUrl }
                    : { "aria-disabled": "true" })}
                  aria-label="Download Lumyn .deb package for Ubuntu"
                >
                  <img src={asset("download.svg")} alt="" />
                  <span>Download .deb</span>
                </a>
              </div>

              <TerminalInstall>
                <div className="terminal-option">
                  <p className="terminal-option-label">Snap</p>
                  <div className="code-block">
                    <code>{SNAP_CMD}</code>
                    <CopyButton text={SNAP_CMD} label="Copy snap install command" />
                  </div>
                </div>
                <div className="terminal-option">
                  <p className="terminal-option-label">PPA (Ubuntu / Debian)</p>
                  <div className="code-block">
                    <code>{PPA_CMD}</code>
                    <CopyButton text={PPA_CMD} label="Copy PPA install commands" />
                  </div>
                </div>
              </TerminalInstall>
            </article>
          )}

          {activeOS === "windows" && (
            <article
              className="download-panel"
              id="download-panel-windows"
              role="tabpanel"
              aria-labelledby="download-tab-windows"
            >
              <div className="download-panel-copy">
                <h3>Windows</h3>
                <p>Install via the Microsoft Store, or download the standalone installer.</p>
              </div>
              <div className="download-options">
                <a
                  className="button primary platform-download"
                  href="https://apps.microsoft.com/detail/9p8vwdsftsn6?hl=en-us&gl=IN&ocid=pdpshare"
                  target="_blank"
                  rel="noopener noreferrer"
                  aria-label="Get Lumyn from the Microsoft Store"
                >
                  <img src={asset("windows.svg")} alt="" />
                  <span>Microsoft Store</span>
                </a>
                <a
                  className={`button secondary platform-download${windowsUrl ? "" : " disabled"}`}
                  {...(windowsUrl
                    ? { href: windowsUrl }
                    : { "aria-disabled": "true" })}
                  aria-label="Download Lumyn .exe installer for Windows"
                >
                  <img src={asset("download.svg")} alt="" />
                  <span>Standalone installer</span>
                </a>
              </div>

              <TerminalInstall>
                <div className="terminal-option">
                  <p className="terminal-option-label">PowerShell</p>
                  <div className="code-block">
                    <code>{winPsCmd}</code>
                    <CopyButton text={winPsCmd} label="Copy PowerShell install command" />
                  </div>
                </div>
              </TerminalInstall>
            </article>
          )}
        </div>
      </motion.div>
    </section>
  );
}
