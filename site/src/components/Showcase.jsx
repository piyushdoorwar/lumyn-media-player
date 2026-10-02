import { motion } from "framer-motion";
import { asset } from "../lib/assets.js";
import { inView, listContainer, listItem } from "../motion/motion.js";
import Icon from "./Icon.jsx";

// Each row: a large screenshot card beside two smaller ones. Rows alternate sides.
const ROWS = [
  {
    big: {
      icon: "cpu",
      title: "Powered by mpv",
      body: "Native playback through mpv, bundled with release builds, so nearly any file plays smoothly on first run.",
      media: "preview-speed.svg",
    },
    smalls: [
      {
        icon: "bookmark",
        title: "Resume & bookmarks",
        body: "Pick up where you left off and jump to favourite moments.",
        media: "preview-bookmarks.svg",
      },
      {
        icon: "search",
        title: "Subtitle search",
        body: "Find and download matching subtitles without leaving the player.",
        media: "preview-subtitle-search.svg",
      },
    ],
  },
  {
    flip: true,
    big: {
      icon: "cast",
      title: "Cast to Chromecast",
      body: "Stream to nearby devices with automatic discovery, format detection and full playback control.",
      media: "preview-cast.svg",
    },
    smalls: [
      {
        icon: "image",
        title: "Seek thumbnails",
        body: "Hover the seek bar to preview any moment.",
        media: "preview-seek-thumbnails.svg",
      },
      {
        icon: "clock",
        title: "Recently played",
        body: "Thumbnails, resume progress and cover art on the start screen.",
        media: "preview-recently-played.svg",
      },
    ],
  },
  {
    big: {
      icon: "captions",
      title: "Subtitle friendly",
      body: "Load subtitle files, pick embedded tracks, restyle them and tune sync delay to the millisecond.",
      media: "preview-subtitles.svg",
    },
    smalls: [
      {
        icon: "monitor",
        title: "Watch modes & audio clarity",
        body: "Cinema, Lecture, Language Learning, Night and Music Video presets, plus voice-focused EQ.",
        media: "preview-watch-modes.svg",
      },
      {
        icon: "gauge",
        title: "Focused controls",
        body: "Seek, speed, screenshots, looping and track switching.",
        media: "preview-screenshot.svg",
      },
    ],
  },
];

const MORE = [
  ["timer", "Subtitle delay"],
  ["type", "Subtitle styling"],
  ["list", "Playlist & queue"],
  ["music", "Audio tracks"],
  ["loop", "Loop & A-B repeat"],
  ["flag", "Chapter markers"],
  ["drop", "Drag & drop"],
  ["clock", "Jump to time"],
  ["pin", "Always on top"],
  ["fullscreen", "Fullscreen"],
  ["keyboard", "Keyboard shortcuts"],
  ["disc", "Cover art"],
];

function Shot({ media }) {
  return (
    <div className="shot">
      <img src={asset(media)} alt="" loading="lazy" draggable="false" />
    </div>
  );
}

function Big({ icon, title, body, media }) {
  return (
    <motion.article className="card show-big" variants={listItem}>
      <div>
        <span className="icon">
          <Icon name={icon} />
        </span>
        <h3 style={{ marginTop: 14 }}>{title}</h3>
        <p>{body}</p>
      </div>
      <Shot media={media} />
    </motion.article>
  );
}

function Small({ icon, title, body, media }) {
  return (
    <motion.article className="card show-small" variants={listItem}>
      <div>
        <span className="icon">
          <Icon name={icon} />
        </span>
        <h3>{title}</h3>
        <p>{body}</p>
      </div>
      <Shot media={media} />
    </motion.article>
  );
}

export default function Showcase() {
  return (
    <>
      <div className="showcase">
        {ROWS.map((row) => {
          const big = <Big {...row.big} />;
          const stack = (
            <div className="show-stack">
              {row.smalls.map((s) => (
                <Small key={s.title} {...s} />
              ))}
            </div>
          );
          return (
            <motion.div
              key={row.big.title}
              className={`show-row${row.flip ? " flip" : ""}`}
              variants={listContainer}
              {...inView}
            >
              {row.flip ? (
                <>
                  {stack}
                  {big}
                </>
              ) : (
                <>
                  {big}
                  {stack}
                </>
              )}
            </motion.div>
          );
        })}
      </div>

      <motion.ul className="features" aria-label="More features" variants={listContainer} {...inView}>
        {MORE.map(([icon, label]) => (
          <motion.li key={label} className="feature" variants={listItem}>
            <Icon name={icon} />
            {label}
          </motion.li>
        ))}
      </motion.ul>
    </>
  );
}
