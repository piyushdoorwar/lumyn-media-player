import { asset, BASE, GITHUB_URL, SUPPORT_EMAIL } from "../lib/assets.js";

// variant: "home" | "releases" | "policy"
export default function Footer({ variant = "home", onSupport }) {
  if (variant === "policy") {
    return (
      <footer>
        <span>
          &copy; {new Date().getFullYear()} Piyush Doorwar. Lumyn Media Player.
        </span>
        <div className="footer-links">
          <a className="icon-link" href={BASE}>
            <img src={asset("lumyn.svg")} alt="" />
            <span>Home</span>
          </a>
          <a className="icon-link" href={`${BASE}releases/`}>
            <img src={asset("releases.svg")} alt="" />
            <span>Releases</span>
          </a>
          <a className="icon-link" href={`${BASE}policy/`} aria-current="page">
            <img src={asset("policy.svg")} alt="" />
            <span>Policy</span>
          </a>
          <a className="icon-link" href={GITHUB_URL} rel="noreferrer">
            <img src={asset("github.svg")} alt="" />
            <span>GitHub</span>
          </a>
          <a className="icon-link" href={`mailto:${SUPPORT_EMAIL}`}>
            <img src={asset("mail.svg")} alt="" />
            <span>Contact</span>
          </a>
        </div>
      </footer>
    );
  }

  const releasesCurrent = variant === "releases";
  return (
    <footer>
      <span>Lumyn Media Player</span>
      <div className="footer-links">
        <a
          className="icon-link"
          href={`${BASE}releases/`}
          {...(releasesCurrent ? { "aria-current": "page" } : {})}
        >
          <img src={asset("releases.svg")} alt="" />
          <span>{releasesCurrent ? "Releases" : "All releases"}</span>
        </a>
        <a className="icon-link" href={`${BASE}policy/`}>
          <img src={asset("policy.svg")} alt="" />
          <span>Policy</span>
        </a>
        <a className="github-link" href={GITHUB_URL} rel="noreferrer">
          <img src={asset("github.svg")} alt="" />
          <span>GitHub</span>
        </a>
        <button className="footer-support-link icon-link" onClick={onSupport}>
          <img src={asset("support.svg")} alt="" />
          <span>Support</span>
        </button>
      </div>
    </footer>
  );
}
