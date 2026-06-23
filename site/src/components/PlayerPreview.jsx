import { useEffect, useState } from "react";
import { asset } from "../lib/assets.js";

const SUBTITLES = [
  "Clean playback, readable subtitles, no clutter.",
  "Hardware decoded. Silky smooth.",
  "Drag and drop any file to start.",
  "Full subtitle track support built in.",
  "Loop, seek, screenshot — always one key away.",
];

const START_SEC = 42;
const END_SEC = 340;
const MIN_PCT = 12;
const MAX_PCT = 55;

function fmt(s) {
  const m = Math.floor(s / 60);
  const ss = s % 60;
  return `${String(m).padStart(2, "0")}:${String(ss).padStart(2, "0")}`;
}

export default function PlayerPreview() {
  const [pos, setPos] = useState(START_SEC);
  const [subIndex, setSubIndex] = useState(0);
  const [subVisible, setSubVisible] = useState(true);

  // Time ticker + progress bar
  useEffect(() => {
    const id = setInterval(() => {
      setPos((p) => (p + 1 >= END_SEC ? START_SEC : p + 1));
    }, 1000);
    return () => clearInterval(id);
  }, []);

  // Subtitle rotator (fade out, swap, fade in)
  useEffect(() => {
    const id = setInterval(() => {
      setSubVisible(false);
      setTimeout(() => {
        setSubIndex((i) => (i + 1) % SUBTITLES.length);
        setSubVisible(true);
      }, 420);
    }, 4200);
    return () => clearInterval(id);
  }, []);

  const pct = (pos - START_SEC) / (END_SEC - START_SEC);
  const barWidth = MIN_PCT + pct * (MAX_PCT - MIN_PCT);

  return (
    <div className="player-preview" aria-label="Lumyn player preview">
      <div className="window-bar">
        <div className="mark">
          <img src={asset("lumyn.svg")} alt="" />
        </div>
        <span>sample-video.mkv - Lumyn</span>
        <div className="window-buttons" aria-hidden="true">
          <i></i>
          <i></i>
          <i></i>
        </div>
      </div>
      <div className="screen">
        <div className="play-ring">
          <span></span>
        </div>
        <div
          className="subtitle"
          style={{
            opacity: subVisible ? 1 : 0,
            transition: "opacity 0.4s ease",
          }}
        >
          {SUBTITLES[subIndex]}
        </div>
      </div>
      <div className="controls">
        <div className="timeline">
          <span
            style={{
              width: `${barWidth.toFixed(1)}%`,
              transition: "width 0.95s linear",
            }}
          ></span>
        </div>
        <div className="control-row">
          <span>{fmt(pos)}</span>
          <div className="control-icons" aria-hidden="true">
            <i></i>
            <i></i>
            <i></i>
            <i></i>
          </div>
          <span>1:28:04</span>
        </div>
      </div>
    </div>
  );
}
