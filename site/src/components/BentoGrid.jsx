import { motion } from "framer-motion";
import { asset } from "../lib/assets.js";

// Every row is a big screenshot tile beside a stack of two split tiles
// (text on the left, screenshot on the right). The big tile alternates sides.
const ROWS = [
  {
    side: "left",
    big: {
      title: "Powered by mpv",
      body: "Native playback through mpv, packaged with release builds for a smoother first run across every format you throw at it.",
      media: "preview-speed.svg",
    },
    smalls: [
      {
        title: "Resume & Bookmarks",
        body: "Pick up where you left off, and jump to favourite moments.",
        media: "preview-bookmarks.svg",
      },
      {
        title: "Subtitle search",
        body: "Find and download matching subtitles in-app.",
        media: "preview-subtitle-search.svg",
      },
    ],
  },
  {
    side: "right",
    big: {
      title: "Cast to Chromecast",
      body: "Stream to nearby devices with automatic discovery, format detection, and full playback control.",
      media: "preview-cast.svg",
    },
    smalls: [
      {
        title: "Seek thumbnail preview",
        body: "Hover the seek bar to preview any moment.",
        media: "preview-seek-thumbnails.svg",
      },
      {
        title: "Recently played",
        body: "Thumbnails, resume progress, and cover art.",
        media: "preview-recently-played.svg",
      },
    ],
  },
  {
    side: "left",
    big: {
      title: "Subtitle friendly",
      body: "Load subtitle files, pick embedded tracks, restyle appearance, and tune sync delay to the millisecond.",
      media: "preview-subtitles.svg",
    },
    smalls: [
      {
        title: "Watch modes & audio clarity",
        body: "Cinema, Theatre, and TV modes, plus voice-focused EQ.",
        media: "preview-watch-modes.svg",
      },
      {
        title: "Focused controls",
        body: "Seek, speed, screenshots, looping, and track switching.",
        media: "preview-screenshot.svg",
      },
    ],
  },
];

const container = {
  hidden: {},
  show: { transition: { staggerChildren: 0.07 } },
};
const item = {
  hidden: { opacity: 0, y: 22 },
  show: { opacity: 1, y: 0, transition: { duration: 0.5, ease: [0.22, 1, 0.36, 1] } },
};

function Big({ title, body, media, compact }) {
  return (
    <motion.article
      className={`bento-card bento-big${compact ? " compact" : ""}`}
      variants={item}
      whileHover={{ y: -4 }}
    >
      <div className="bento-head">
        <h3>{title}</h3>
        <p>{body}</p>
      </div>
      <div className="bento-frame">
        <img src={asset(media)} alt="" loading="lazy" draggable="false" />
      </div>
    </motion.article>
  );
}

function Small({ title, body, media }) {
  return (
    <motion.article className="bento-card split" variants={item} whileHover={{ y: -4 }}>
      <div className="bento-text">
        <h3>{title}</h3>
        <p>{body}</p>
      </div>
      <div className="bento-shot">
        <img src={asset(media)} alt="" loading="lazy" draggable="false" />
      </div>
    </motion.article>
  );
}

function Stack({ items }) {
  return (
    <motion.div className="bento-stack" variants={container}>
      {items.map((s) => (
        <Small key={s.title} {...s} />
      ))}
    </motion.div>
  );
}

const rowReveal = {
  variants: container,
  initial: "hidden",
  whileInView: "show",
  viewport: { once: true, amount: 0.2 },
};

export default function BentoGrid() {
  return (
    <div className="bento">
      {ROWS.map((row, i) => (
        <motion.div className="bento-row" key={i} {...rowReveal}>
          {row.side === "left" ? (
            <>
              <Big {...row.big} />
              <Stack items={row.smalls} />
            </>
          ) : (
            <>
              <Stack items={row.smalls} />
              <Big {...row.big} />
            </>
          )}
        </motion.div>
      ))}
    </div>
  );
}
