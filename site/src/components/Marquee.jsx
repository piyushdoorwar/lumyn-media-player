import { asset } from "../lib/assets.js";

const ITEMS = [
  ["ic-bookmark.svg", "Bookmarks"],
  ["ic-subtitle-search.svg", "Subtitle Search"],
  ["ic-subtitle-delay.svg", "Subtitle Delay"],
  ["ic-subtitle-appearance.svg", "Subtitle Appearance"],
  ["ic-playlist.svg", "Playlist / Queue"],
  ["ic-audio.svg", "Audio Tracks"],
  ["ic-speed.svg", "Playback Speed"],
  ["ic-screenshot.svg", "Screenshot"],
  ["ic-loop.svg", "Loop"],
  ["ic-chapters.svg", "Chapter Markers"],
  ["ic-cast.svg", "Cast to Chromecast"],
  ["ic-open-file.svg", "Open File"],
  ["ic-drag-drop.svg", "Drag & Drop"],
  ["ic-jump-to-time.svg", "Jump to Time"],
  ["ic-always-on-top.svg", "Always on Top"],
  ["ic-fullscreen.svg", "Fullscreen"],
  ["ic-queue.svg", "Keyboard Shortcuts"],
  ["ic-loop.svg", "A-B Repeat"],
  ["ic-screenshot.svg", "Seek Thumbnails"],
  ["ic-open-file.svg", "Recently Played"],
  ["ic-audio.svg", "Cover Art"],
  ["ic-speed.svg", "Watch Modes"],
];

function Row({ ariaHidden }) {
  return (
    <ul className="marquee-row" {...(ariaHidden ? { "aria-hidden": "true" } : {})}>
      {ITEMS.map(([icon, label], i) => (
        <li key={`${label}-${i}`}>
          <img src={asset(icon)} alt="" />
          {label}
        </li>
      ))}
    </ul>
  );
}

export default function Marquee() {
  return (
    <div className="marquee-wrap" aria-hidden="true">
      <div className="marquee-track">
        <Row />
        <Row ariaHidden />
      </div>
    </div>
  );
}
