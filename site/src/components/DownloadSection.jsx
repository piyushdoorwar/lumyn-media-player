import { useEffect, useRef, useState } from "react";
import { motion } from "framer-motion";
import { fadeUp, inView } from "../motion/motion.js";
import { fetchReleases, linuxAsset } from "../lib/releases.js";
import CopyButton from "./CopyButton.jsx";
import Icon from "./Icon.jsx";

const SNAP_CMD = "sudo snap install lumyn";
const PPA_CMD = `sudo add-apt-repository ppa:piyushdoorwar/lumyn
sudo apt update
sudo apt install lumyn`;
const STORE_URL = "https://apps.microsoft.com/detail/9p8vwdsftsn6";
const SNAP_URL = "https://snapcraft.io/lumyn";

const OS = ["linux", "windows"];

function detectOS() {
  const hint = [navigator.userAgentData?.platform, navigator.platform, navigator.userAgent]
    .filter(Boolean)
    .join(" ");
  return /\bwin(dows|32|64)\b/i.test(hint) ? "windows" : "linux";
}

// The landing page only offers the standalone Windows installer, never a zip.
const windowsInstaller = (release) => release.assets.find((a) => /win-x64.*_setup\.exe$/i.test(a.name));

function firstUrl(releases, find) {
  for (const r of releases) {
    const url = find(r)?.browser_download_url;
    if (url) return url;
  }
  return null;
}

function Command({ label, text, copyLabel }) {
  return (
    <div>
      <p className="cmd-label">{label}</p>
      <div className="cmd">
        <pre>
          <code>{text}</code>
        </pre>
        <CopyButton text={text} label={copyLabel} />
      </div>
    </div>
  );
}

function Terminal({ children }) {
  const ref = useRef(null);
  // Keep the expanded commands in view when they open near the bottom edge.
  const onToggle = () => {
    const el = ref.current;
    if (!el?.open) return;
    requestAnimationFrame(() => {
      const bottom = el.getBoundingClientRect().bottom;
      const overflow = bottom - (window.innerHeight - 16);
      if (overflow > 0) window.scrollBy({ top: overflow + 8, behavior: "smooth" });
    });
  };
  return (
    <details className="terminal" ref={ref} onToggle={onToggle}>
      <summary>
        <Icon name="terminal" />
        Install from the terminal
        <Icon name="chevron" className="chev" />
      </summary>
      <div className="terminal-body">{children}</div>
    </details>
  );
}

export default function DownloadSection() {
  const [os, setOS] = useState("linux");
  const [debUrl, setDebUrl] = useState(null);
  const [armDebUrl, setArmDebUrl] = useState(null);
  const [exeUrl, setExeUrl] = useState(null);
  const tabs = useRef([]);

  useEffect(() => {
    setOS(detectOS());
    fetchReleases()
      .then((all) => {
        const stable = all.filter((r) => !r.prerelease);
        setDebUrl(firstUrl(stable, linuxAsset));
        setArmDebUrl(firstUrl(stable, (r) => linuxAsset(r, "arm64")));
        setExeUrl(firstUrl(stable, windowsInstaller));
      })
      .catch(() => {
        // Store links still work; the direct-download buttons stay disabled.
      });
  }, []);

  const onTabKey = (e, index) => {
    const dir = e.key === "ArrowRight" ? 1 : e.key === "ArrowLeft" ? -1 : 0;
    if (!dir) return;
    e.preventDefault();
    const next = (index + dir + OS.length) % OS.length;
    setOS(OS[next]);
    tabs.current[next]?.focus();
  };

  const psCmd = exeUrl
    ? `$url = "${exeUrl}"
$out = "$env:TEMP\\${exeUrl.split("/").pop()}"
Invoke-WebRequest $url -OutFile $out
Start-Process $out`
    : null;

  const direct = (url, label, aria) => (
    <a
      className="btn btn-secondary"
      {...(url ? { href: url } : { "aria-disabled": "true" })}
      aria-label={aria}
    >
      <Icon name="download" />
      {label}
    </a>
  );

  return (
    <section className="section tint" id="download">
      <div className="wrap download-grid">
        <motion.div className="section-head" variants={fadeUp} {...inView}>
          <p className="eyebrow">Download</p>
          <h2>Get Lumyn for Ubuntu and Windows</h2>
          <p>
            Install from your app store for automatic updates, or grab a standalone package from
            the latest release.
          </p>
        </motion.div>

        <motion.div variants={fadeUp} {...inView}>
          <div className="tabs" role="tablist" aria-label="Operating system">
            {[
              ["linux", "ubuntu", "Ubuntu"],
              ["windows", "windows", "Windows"],
            ].map(([key, icon, label], i) => (
              <button
                key={key}
                ref={(el) => (tabs.current[i] = el)}
                id={`download-tab-${key}`}
                className="tab"
                type="button"
                role="tab"
                aria-selected={os === key}
                aria-controls={`download-panel-${key}`}
                tabIndex={os === key ? 0 : -1}
                onClick={() => setOS(key)}
                onKeyDown={(e) => onTabKey(e, i)}
              >
                <Icon name={icon} />
                {label}
              </button>
            ))}
          </div>

          {os === "linux" ? (
            <div className="panel" id="download-panel-linux" role="tabpanel" aria-labelledby="download-tab-linux">
              <h3>Ubuntu</h3>
              <p>Install from the Ubuntu App Center, or download the latest Debian package.</p>
              <div className="panel-actions">
                <a className="btn btn-primary" href={SNAP_URL} target="_blank" rel="noopener noreferrer">
                  <Icon name="snapcraft" />
                  Ubuntu App Center
                </a>
                {direct(debUrl, ".deb · Intel / AMD 64-bit", "Download Lumyn for Ubuntu Intel or AMD 64-bit")}
                {direct(armDebUrl, ".deb · ARM64", "Download Lumyn for Ubuntu ARM64")}
              </div>
              <Terminal>
                <Command label="Snap" text={SNAP_CMD} copyLabel="Copy snap install command" />
                <Command label="PPA (Ubuntu / Debian)" text={PPA_CMD} copyLabel="Copy PPA install commands" />
              </Terminal>
            </div>
          ) : (
            <div className="panel" id="download-panel-windows" role="tabpanel" aria-labelledby="download-tab-windows">
              <h3>Windows</h3>
              <p>Install from the Microsoft Store, or download the standalone installer.</p>
              <div className="panel-actions">
                <a className="btn btn-primary" href={STORE_URL} target="_blank" rel="noopener noreferrer">
                  <Icon name="windows" />
                  Microsoft Store
                </a>
                {direct(exeUrl, "Standalone installer", "Download the Lumyn installer for Windows")}
              </div>
              {psCmd && (
                <Terminal>
                  <Command label="PowerShell" text={psCmd} copyLabel="Copy PowerShell install command" />
                </Terminal>
              )}
            </div>
          )}
        </motion.div>
      </div>
    </section>
  );
}
