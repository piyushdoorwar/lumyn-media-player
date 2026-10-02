import { useEffect, useRef, useState } from "react";
import { AnimatePresence, motion, useReducedMotion } from "framer-motion";
import { asset } from "../lib/assets.js";
import Icon from "./Icon.jsx";

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

function Equalizer({ active }) {
  return (
    <div className="eq" aria-hidden="true">
      {[0, 1, 2, 3, 4].map((i) => (
        <motion.span
          key={i}
          animate={active ? { scaleY: [0.3, 1, 0.45, 0.85, 0.35] } : { scaleY: 0.2 }}
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

// A small, working stand-in for the player: play/pause, seek, ±10s and loop.
export default function PlayerPreview() {
  const reduced = useReducedMotion();
  const [playing, setPlaying] = useState(true);
  const [pos, setPos] = useState(61);
  const [subIndex, setSubIndex] = useState(0);
  const [subVisible, setSubVisible] = useState(true);
  const [loop, setLoop] = useState(true);
  const [hover, setHover] = useState(null); // 0..1 while hovering the seek bar
  const trackRef = useRef(null);

  useEffect(() => {
    if (!playing) return;
    const id = setInterval(() => {
      setPos((p) => {
        if (p + 1 < DURATION) return p + 1;
        if (loop) return 0;
        setPlaying(false);
        return DURATION;
      });
    }, 1000);
    return () => clearInterval(id);
  }, [playing, loop]);

  useEffect(() => {
    if (!playing) return;
    let swap;
    const id = setInterval(() => {
      setSubVisible(false);
      swap = setTimeout(() => {
        setSubIndex((i) => (i + 1) % SUBTITLES.length);
        setSubVisible(true);
      }, 420);
    }, 4200);
    return () => {
      clearInterval(id);
      clearTimeout(swap);
      setSubVisible(true);
    };
  }, [playing]);

  const pct = (pos / DURATION) * 100;
  const active = playing && !reduced;
  const toggle = () => {
    if (!playing && pos >= DURATION) setPos(0);
    setPlaying((p) => !p);
  };

  const ratioFromEvent = (e) => {
    const r = trackRef.current.getBoundingClientRect();
    return Math.min(1, Math.max(0, (e.clientX - r.left) / r.width));
  };

  return (
    <div className="player" role="group" aria-label="Lumyn player preview">
      <div className="player-bar">
        <img src={asset("lumyn.svg")} alt="" />
        <span>sample-video.mkv — Lumyn</span>
        <div className="player-dots" aria-hidden="true">
          <i />
          <i />
          <i />
        </div>
      </div>

      <div className="screen">
        <Equalizer active={active} />
        <motion.button
          type="button"
          className="play-ring"
          onClick={toggle}
          aria-label={playing ? "Pause" : "Play"}
          whileTap={{ scale: 0.94 }}
        >
          <AnimatePresence mode="wait" initial={false}>
            <motion.span
              key={playing ? "pause" : "play"}
              style={{ display: "grid", placeItems: "center" }}
              initial={{ opacity: 0, scale: 0.7 }}
              animate={{ opacity: 1, scale: 1 }}
              exit={{ opacity: 0, scale: 0.7 }}
              transition={{ duration: 0.15 }}
            >
              <Icon name={playing ? "pause" : "play"} className={playing ? "" : "ic-play"} />
            </motion.span>
          </AnimatePresence>
        </motion.button>
        <div className="subtitle" style={{ opacity: subVisible ? 1 : 0 }}>
          {SUBTITLES[subIndex]}
        </div>
      </div>

      <div className="player-controls">
        <div
          className="seek"
          ref={trackRef}
          role="slider"
          tabIndex={0}
          aria-label="Seek"
          aria-valuemin={0}
          aria-valuemax={DURATION}
          aria-valuenow={Math.floor(pos)}
          aria-valuetext={fmt(pos)}
          onClick={(e) => setPos(ratioFromEvent(e) * DURATION)}
          onKeyDown={(e) => {
            const step = e.key === "ArrowRight" ? 10 : e.key === "ArrowLeft" ? -10 : 0;
            if (!step) return;
            e.preventDefault();
            setPos((p) => Math.min(DURATION, Math.max(0, p + step)));
          }}
          onMouseMove={(e) => setHover(ratioFromEvent(e))}
          onMouseLeave={() => setHover(null)}
        >
          <div className="seek-track">
            <span className="seek-fill" style={{ width: `${pct}%` }} />
            <span className="seek-knob" style={{ left: `${pct}%` }} />
          </div>
          <AnimatePresence>
            {hover !== null && (
              <motion.div
                className="seek-thumb"
                style={{ left: `${hover * 100}%` }}
                initial={{ opacity: 0, y: 4 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, y: 4 }}
                transition={{ duration: 0.12 }}
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
            <button
              type="button"
              className="ctrl-btn"
              aria-label="Back 10 seconds"
              onClick={() => setPos((p) => Math.max(0, p - 10))}
            >
              <Icon name="rewind" />
            </button>
            <button
              type="button"
              className="ctrl-btn primary"
              aria-label={playing ? "Pause" : "Play"}
              onClick={toggle}
            >
              <Icon name={playing ? "pause" : "play"} />
            </button>
            <button
              type="button"
              className="ctrl-btn"
              aria-label="Forward 10 seconds"
              onClick={() => setPos((p) => Math.min(DURATION, p + 10))}
            >
              <Icon name="forward" />
            </button>
            <button
              type="button"
              className={`ctrl-btn${loop ? " active" : ""}`}
              aria-label="Loop"
              aria-pressed={loop}
              onClick={() => setLoop((l) => !l)}
            >
              <Icon name="loop" />
            </button>
          </div>
          <span>{fmt(DURATION)}</span>
        </div>
      </div>
    </div>
  );
}
