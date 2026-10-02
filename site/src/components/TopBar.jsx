import { useEffect, useState } from "react";
import { asset, BASE, GITHUB_URL } from "../lib/assets.js";
import Icon from "./Icon.jsx";

const LINKS = [
  { key: "features", href: `${BASE}#features`, icon: "grid", label: "Features" },
  { key: "download", href: `${BASE}#download`, icon: "download", label: "Download" },
  { key: "releases", href: `${BASE}releases/`, icon: "tag", label: "Releases" },
  { key: "policy", href: `${BASE}policy/`, icon: "shield", label: "Privacy" },
];

// current: "home" | "releases" | "policy"
export default function TopBar({ current }) {
  const [open, setOpen] = useState(false);

  useEffect(() => {
    if (!open) return;
    const onKey = (e) => e.key === "Escape" && setOpen(false);
    const onResize = () => window.innerWidth > 820 && setOpen(false);
    document.addEventListener("keydown", onKey);
    window.addEventListener("resize", onResize);
    return () => {
      document.removeEventListener("keydown", onKey);
      window.removeEventListener("resize", onResize);
    };
  }, [open]);

  return (
    <header className={`topbar${open ? " open" : ""}`}>
      <a className="skip-link" href="#main">
        Skip to content
      </a>
      <div className="wrap">
        <a className="brand" href={BASE} aria-label="Lumyn home">
          <img src={asset("lumyn.svg")} alt="" />
          <span>Lumyn</span>
        </a>
        <button
          className="nav-toggle"
          type="button"
          aria-expanded={open}
          aria-controls="primary-nav"
          aria-label={open ? "Close menu" : "Open menu"}
          onClick={() => setOpen((o) => !o)}
        >
          <Icon name={open ? "close" : "menu"} />
        </button>
        <nav className="nav" id="primary-nav" aria-label="Primary" onClick={() => setOpen(false)}>
          {LINKS.map((l) => (
            <a key={l.key} href={l.href} {...(current === l.key ? { "aria-current": "page" } : {})}>
              <Icon name={l.icon} />
              <span>{l.label}</span>
            </a>
          ))}
          <a className="nav-gh" href={GITHUB_URL} rel="noreferrer">
            <Icon name="github" />
            <span>GitHub</span>
          </a>
        </nav>
      </div>
    </header>
  );
}
