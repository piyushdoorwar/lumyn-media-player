import { asset, BASE, GITHUB_URL } from "../lib/assets.js";

export default function TopBar({ current }) {
  return (
    <header className="topbar">
      <a className="brand" href={BASE} aria-label="Lumyn home">
        <img src={asset("lumyn.svg")} alt="" />
        <span>Lumyn</span>
      </a>
      <nav aria-label="Primary navigation">
        <a className="icon-link" href={`${BASE}#features`}>
          <img src={asset("features.svg")} alt="" />
          <span>Features</span>
        </a>
        <a className="icon-link" href={`${BASE}#download`}>
          <img src={asset("download.svg")} alt="" />
          <span>Download</span>
        </a>
        <a
          className="icon-link"
          href={`${BASE}releases/`}
          {...(current === "releases" ? { "aria-current": "page" } : {})}
        >
          <img src={asset("releases.svg")} alt="" />
          <span>Releases</span>
        </a>
        <a
          className="icon-link"
          href={`${BASE}policy/`}
          {...(current === "policy" ? { "aria-current": "page" } : {})}
        >
          <img src={asset("policy.svg")} alt="" />
          <span>Policy</span>
        </a>
        <a className="github-link icon-link" href={GITHUB_URL} rel="noreferrer">
          <img src={asset("github.svg")} alt="" />
          <span>GitHub</span>
        </a>
      </nav>
    </header>
  );
}
