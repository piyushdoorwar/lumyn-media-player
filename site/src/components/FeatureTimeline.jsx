import { useRef } from "react";
import { motion, useScroll, useSpring } from "framer-motion";
import { asset } from "../lib/assets.js";
import { revealFrom, nodePop } from "../motion/motion.js";

const FEATURES = [
  {
    title: "Powered by mpv",
    body: "Native playback through mpv, packaged with release builds for a smoother first run.",
    preview: "preview-speed.svg",
  },
  {
    title: "Subtitle friendly",
    body: "Load subtitle files, pick embedded tracks, adjust appearance, and tune sync delay.",
    preview: "preview-subtitles.svg",
  },
  {
    title: "Focused controls",
    body: "Hover controls, seek shortcuts, speed controls, screenshots, looping, and track switching.",
    preview: "preview-screenshot.svg",
  },
  {
    title: "Desktop native",
    body: "Avalonia UI with a compact custom title bar and a simple local-media workflow.",
    preview: "preview-playlist.svg",
  },
  {
    title: "Cast to Chromecast",
    body: "Stream to nearby Chromecast devices with automatic discovery, format support detection, and full playback control.",
    preview: "preview-cast.svg",
  },
  {
    title: "Resume & Bookmarks",
    body: "Automatically resume playback from where you left off, and create bookmarks to jump to your favourite moments instantly.",
    preview: "preview-bookmarks.svg",
  },
  {
    title: "Subtitle search",
    body: "Find matching subtitles from inside the player and download the right file without leaving your session.",
    preview: "preview-subtitle-search.svg",
  },
  {
    title: "Seek thumbnail preview",
    body: "Hover anywhere on the seek bar to instantly preview a thumbnail of that moment — no seeking required. Frames load progressively in the background so the first preview appears within seconds.",
    preview: "preview-seek-thumbnails.svg",
  },
  {
    title: "Recently played",
    body: "A visual start screen shows your recently played files with thumbnail previews, resume progress, and album cover art for audio files — so you can pick up exactly where you left off.",
    preview: "preview-recently-played.svg",
  },
  {
    title: "Watch modes & audio clarity",
    body: "Switch between Cinema, Theatre, and TV modes to apply video adjustments in one tap. Audio Clarity mode boosts dialogue with voice-focused EQ for easier listening.",
    preview: "preview-watch-modes.svg",
  },
];

export default function FeatureTimeline() {
  const ref = useRef(null);
  const { scrollYProgress } = useScroll({
    target: ref,
    offset: ["start center", "end center"],
  });
  const scaleY = useSpring(scrollYProgress, {
    stiffness: 120,
    damping: 30,
    mass: 0.3,
  });

  return (
    <div className="feature-timeline" ref={ref}>
      <motion.div className="timeline-fill" style={{ scaleY }} />
      {FEATURES.map((f, i) => (
        <motion.article
          className="feature-row"
          key={f.title}
          variants={revealFrom(i % 2 === 0 ? -64 : 64)}
          initial="hidden"
          whileInView="show"
          viewport={{ once: true, amount: 0.3 }}
        >
          <div className="feature-card">
            <h3>{f.title}</h3>
            <p>{f.body}</p>
          </div>
          <div className="feature-node">
            <motion.span variants={nodePop} />
          </div>
          <div className="feature-visual">
            <img
              className="feature-preview"
              src={asset(f.preview)}
              alt=""
              loading="lazy"
              draggable="false"
            />
          </div>
        </motion.article>
      ))}
    </div>
  );
}
