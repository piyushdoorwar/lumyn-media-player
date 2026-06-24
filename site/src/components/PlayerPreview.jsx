import { useEffect, useRef, useState } from "react";
import { AnimatePresence, motion, useReducedMotion } from "framer-motion";
import { asset } from "../lib/assets.js";

const SUBTITLES = [
  "Clean playback, readable subtitles, no clutter.",
  "Hardware decoded. Silky smooth.",
  "Drag and drop any file to start.",
  "Full subtitle track support built in.",
  "Loop, seek, screenshot — always one key away.",
];

const DURATION = 5284; // 1:28:04

const pad = (n) => String(n).padStart(2, "0");
function fmt(s) {
  s = Math.max(0, Math.floor(s));
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  const ss = s % 60;
  return h > 0 ? `${h}:${pad(m)}:${pad(ss)}` : `${pad(m)}:${pad(ss)}`;
}

const PlayIcon = () => (
  <svg className="play-tri" viewBox="0 0 24 24" aria-hidden="true">
    <path d="M8 5v14l11-7z" fill="currentColor" />
  </svg>
);
const PauseIcon = () => (
  <svg viewBox="0 0 24 24" aria-hidden="true">
    <rect x="6" y="5" width="4" height="14" rx="1" fill="currentColor" />
    <rect x="14" y="5" width="4" height="14" rx="1" fill="currentColor" />
  </svg>
);
const stroke = { fill: "none", stroke: "currentColor", strokeWidth: 2, strokeLinecap: "round", strokeLinejoin: "round" };
const RewindIcon = () => (
  <svg viewBox="0 0 24 24" aria-hidden="true" {...stroke}>
    <path d="M11 6L5 12l6 6M19 6l-6 6 6 6" />
  </svg>
);
const ForwardIcon = () => (
  <svg viewBox="0 0 24 24" aria-hidden="true" {...stroke}>
    <path d="M13 6l6 6-6 6M5 6l6 6-6 6" />
  </svg>
);
const LoopIcon = () => (
  <svg viewBox="0 0 24 24" aria-hidden="true" {...stroke}>
    <path d="M17 2l4 4-4 4" />
    <path d="M3 12V9a4 4 0 014-4h14" />
    <path d="M7 22l-4-4 4-4" />
    <path d="M21 12v3a4 4 0 01-4 4H3" />
  </svg>
);

function Equalizer({ active }) {
  const bars = [0, 1, 2, 3, 4];
  return (
    <div className="eq" aria-hidden="true">
      {bars.map((i) => (
        <motion.span
          key={i}
          animate={active ? { scaleY: [0.3, 1, 0.45, 0.85, 0.35] } : { scaleY: 0.22 }}
          transition={
            active
              ? { duration: 0.85 + i * 0.13, repeat: Infinity, repeatType: "mirror", ease: "easeInOut" }
              : { duration: 0.3 }
          }
        />
      ))}
    </div>
  );
}

export default function PlayerPreview() {
  const reduced = useReducedMotion();
  const [playing, setPlaying] = useState(true);
  const [pos, setPos] = useState(61);
  const [subIndex, setSubIndex] = useState(0);
  const [subVisible, setSubVisible] = useState(true);
  const [loop, setLoop] = useState(true);
  const [hover, setHover] = useState(null); // 0..1 ratio while hovering the seek bar
  const trackRef = useRef(null);

  // Time advances only while playing
  useEffect(() => {
    if (!playing) return;
    const id = setInterval(() => {
      setPos((p) => (p + 1 >= DURATION ? (loop ? 0 : DURATION) : p + 1));
    }, 1000);
    return () => clearInterval(id);
  }, [playing, loop]);

  // Subtitle rotates only while playing
  useEffect(() => {
    if (!playing) return;
    const id = setInterval(() => {
      setSubVisible(false);
      setTimeout(() => {
        setSubIndex((i) => (i + 1) % SUBTITLES.length);
        setSubVisible(true);
      }, 420);
    }, 4200);
    return () => clearInterval(id);
  }, [playing]);

  const pct = (pos / DURATION) * 100;
  const active = playing && !reduced;

  const ratioFromEvent = (e) => {
    const r = trackRef.current.getBoundingClientRect();
    return Math.min(1, Math.max(0, (e.clientX - r.left) / r.width));
  };
  const seek = (e) => setPos(ratioFromEvent(e) * DURATION);

  return (
    <div className="player-preview" aria-label="Lumyn player preview">
      <div className="window-bar">
        <div className="mark">
          <img src={asset("lumyn.svg")} alt="" />
        </div>
        <span>sample-video.mkv — Lumyn</span>
        <div className="window-buttons" aria-hidden="true">
          <i></i>
          <i></i>
          <i></i>
        </div>
      </div>

      <div className="screen">
        <Equalizer active={active} />

        <motion.button
          type="button"
          className="play-ring"
          onClick={() => setPlaying((p) => !p)}
          aria-label={playing ? "Pause" : "Play"}
          style={{ animationPlayState: active ? "running" : "paused" }}
          whileHover={{ scale: 1.06 }}
          whileTap={{ scale: 0.92 }}
        >
          <AnimatePresence mode="wait" initial={false}>
            <motion.span
              key={playing ? "pause" : "play"}
              style={{ display: "grid", placeItems: "center" }}
              initial={{ opacity: 0, scale: 0.6 }}
              animate={{ opacity: 1, scale: 1 }}
              exit={{ opacity: 0, scale: 0.6 }}
              transition={{ duration: 0.18 }}
            >
              {playing ? <PauseIcon /> : <PlayIcon />}
            </motion.span>
          </AnimatePresence>
        </motion.button>

        <div
          className="subtitle"
          style={{ opacity: subVisible ? 1 : 0, transition: "opacity 0.4s ease" }}
        >
          {SUBTITLES[subIndex]}
        </div>
      </div>

      <div className="controls">
        <div
          className="timeline seekable"
          ref={trackRef}
          onClick={seek}
          onMouseMove={(e) => setHover(ratioFromEvent(e))}
          onMouseLeave={() => setHover(null)}
        >
          <motion.span
            style={{ width: `${pct}%` }}
            animate={{ width: `${pct}%` }}
            transition={{ ease: "linear", duration: active ? 0.9 : 0.2 }}
          />
          <span className="seek-knob" style={{ left: `${pct}%` }} />
          <AnimatePresence>
            {hover !== null && (
              <motion.div
                className="seek-thumb"
                style={{ left: `${hover * 100}%` }}
                initial={{ opacity: 0, y: 6 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, y: 6 }}
                transition={{ duration: 0.15 }}
              >
                <div className="thumb-img" />
                <div className="thumb-time">{fmt(hover * DURATION)}</div>
              </motion.div>
            )}
          </AnimatePresence>
        </div>

        <div className="control-row">
          <span>{fmt(pos)}</span>
          <div className="control-icons">
            <motion.button
              className="ctrl-btn"
              aria-label="Back 10 seconds"
              onClick={() => setPos((p) => Math.max(0, p - 10))}
              whileTap={{ scale: 0.85 }}
            >
              <RewindIcon />
            </motion.button>
            <motion.button
              className="ctrl-btn"
              aria-label={playing ? "Pause" : "Play"}
              onClick={() => setPlaying((p) => !p)}
              whileTap={{ scale: 0.85 }}
            >
              {playing ? <PauseIcon /> : <PlayIcon />}
            </motion.button>
            <motion.button
              className="ctrl-btn"
              aria-label="Forward 10 seconds"
              onClick={() => setPos((p) => Math.min(DURATION, p + 10))}
              whileTap={{ scale: 0.85 }}
            >
              <ForwardIcon />
            </motion.button>
            <motion.button
              className={`ctrl-btn${loop ? " active" : ""}`}
              aria-label="Toggle loop"
              aria-pressed={loop}
              onClick={() => setLoop((l) => !l)}
              whileTap={{ scale: 0.85 }}
            >
              <LoopIcon />
            </motion.button>
          </div>
          <span>{fmt(DURATION)}</span>
        </div>
      </div>
    </div>
  );
}
