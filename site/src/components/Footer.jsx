import { asset, BASE, GITHUB_URL, SUPPORT_EMAIL } from "../lib/assets.js";
import Icon from "./Icon.jsx";

// onSupport opens the support dialog; without it the link falls back to email.
export default function Footer({ onSupport }) {
  return (
    <footer className="footer">
      <div className="wrap">
        <div>
          <a className="brand" href={BASE}>
            <img src={asset("lumyn.svg")} alt="" />
            <span>Lumyn</span>
          </a>
          <p>
            A quiet desktop media player for Windows and Ubuntu. &copy; {new Date().getFullYear()}{" "}
            Piyush Doorwar.
          </p>
        </div>
        <div className="footer-links">
          <a href={`${BASE}releases/`}>
            <Icon name="tag" />
            Releases
          </a>
          <a href={`${BASE}policy/`}>
            <Icon name="shield" />
            Privacy
          </a>
          <a href={GITHUB_URL} rel="noreferrer">
            <Icon name="github" />
            GitHub
          </a>
          {onSupport ? (
            <button type="button" onClick={onSupport}>
              <Icon name="lifebuoy" />
              Support
            </button>
          ) : (
            <a href={`mailto:${SUPPORT_EMAIL}`}>
              <Icon name="mail" />
              Contact
            </a>
          )}
        </div>
      </div>
    </footer>
  );
}
