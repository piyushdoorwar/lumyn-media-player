#!/usr/bin/env bash
# Capture Microsoft Store and Snap Store screenshots of the current Lumyn UI.
#
# Runs a Release build of the app on a private 1920x1080 Xvfb display with an
# isolated config directory and generated (copyright-free) sample media, drives
# it with xdotool, and writes PNGs to
# docs/store-screenshots/. LUMYN_CAPTURE_MODE=1 makes the app accept Xvfb's
# software OpenGL and play through mpv's silent (but real-time) null audio
# output, so nothing touches your real settings, desktop or speakers.
#
# Requirements: dotnet, ffmpeg, python3, Xvfb, xdotool  (sudo apt install xvfb xdotool)
#
# Usage: ./scripts/capture-store-screenshots.sh [--keep-work]
#   OUT_DIR=...      output directory (default: docs/store-screenshots)
#   SKIP_BUILD=1     reuse the existing Release build

set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT_DIR="${OUT_DIR:-$ROOT/docs/store-screenshots}"
MEDIA_DIR="$ROOT/artifacts/screenshot-media"   # cached between runs (gitignored)
MEDIA_STAMP="v2"                              # bump after adding sample media
APP="$ROOT/src/Lumyn.App/bin/Release/net10.0/Lumyn"
FONT="$ROOT/src/Lumyn.App/Assets/Fonts/DMSans-600.ttf"
W=1920
H=1080
SCALE=1.5   # render the UI at 150% so it stays legible when the stores shrink it
KEEP_WORK=0
[[ "${1:-}" == "--keep-work" ]] && KEEP_WORK=1

for tool in dotnet ffmpeg python3 Xvfb xdotool; do
  command -v "$tool" >/dev/null || { echo "Missing '$tool'. Install with: sudo apt install xvfb xdotool ffmpeg" >&2; exit 1; }
done

WORK="$(mktemp -d -t lumyn-shots.XXXXXX)"
XVFB_PID=""
APP_PID=""

cleanup() {
  [[ -n "$APP_PID" ]] && kill "$APP_PID" 2>/dev/null || true
  [[ -n "$XVFB_PID" ]] && kill "$XVFB_PID" 2>/dev/null || true
  if [[ $KEEP_WORK -eq 1 ]]; then echo "Work dir kept: $WORK"; else rm -rf "$WORK"; fi
}
trap cleanup EXIT

log() { printf '\033[1;32m==>\033[0m %s\n' "$*"; }

# ── Build ─────────────────────────────────────────────────────────────────────
if [[ "${SKIP_BUILD:-0}" != "1" ]]; then
  log "Building Release"
  dotnet build "$ROOT/src/Lumyn.App/Lumyn.App.csproj" -c Release -v q -nologo >/dev/null
fi
[[ -x "$APP" ]] || { echo "App not found at $APP" >&2; exit 1; }

# ── Sample media (generated, so the screenshots carry no third-party content) ─
VIDEOS="$MEDIA_DIR/Videos"
MUSIC="$MEDIA_DIR/Music/Lumyn Sessions"

make_video() { # name seconds palette seed [chapters-file]
  local out="$VIDEOS/$1" secs="$2" palette="$3" seed="$4" chapters="${5:-}"
  [[ -f "$out" ]] && return
  local meta=() map=()
  if [[ -n "$chapters" ]]; then meta=(-i "$chapters"); map=(-map_metadata 1 -map_chapters 1); fi
  ffmpeg -v error -y \
    -f lavfi -i "gradients=s=1280x720:${palette}:speed=0.004:seed=${seed}:r=10:d=${secs},vignette=PI/5" \
    "${meta[@]}" \
    -f lavfi -i "anoisesrc=c=pink:a=0.02:d=${secs}" \
    -map 0:v -map "$([[ -n "$chapters" ]] && echo 2 || echo 1):a" "${map[@]}" \
    -c:v libx264 -preset veryfast -crf 26 -pix_fmt yuv420p -g 100 -c:a aac -b:a 64k \
    -metadata title="${1%.*}" "$out"
}

make_track() { # file title root-hz seconds palette seed
  local file="$1" title="$2" f="$3" secs="$4" palette="$5" seed="$6"
  local base="$MUSIC/${file%.*}"
  [[ -f "$base.mp3" ]] && return
  # A soft major-seventh pad with a pulse, so the equalizer has something to react to.
  local expr="0.22*sin(2*PI*${f}*t)*(0.55+0.45*abs(sin(PI*1.9*t)))+0.16*sin(2*PI*${f}*1.26*t)+0.13*sin(2*PI*${f}*1.5*t)*(0.6+0.4*sin(2*PI*0.27*t))+0.09*sin(2*PI*${f}*1.89*t)+0.2*sin(2*PI*${f}/4*t)*pow(abs(sin(PI*1.9*t)),6)"
  ffmpeg -v error -y -f lavfi -i "aevalsrc='${expr}':s=44100:d=${secs}" \
    -c:a libmp3lame -q:a 4 \
    -metadata title="$title" -metadata artist="Lumyn Sessions" -metadata album="Quiet Hours" \
    "$base.mp3"
  ffmpeg -v error -y -f lavfi -i "gradients=s=800x800:${palette}:seed=${seed}:speed=0.01:d=1,vignette=PI/4" \
    -vf "drawtext=fontfile='${FONT}':text='${title}':fontcolor=white@0.92:fontsize=58:x=56:y=h-120,drawtext=fontfile='${FONT}':text='Lumyn Sessions':fontcolor=white@0.6:fontsize=30:x=58:y=h-62" \
    -frames:v 1 -update 1 "$base.png"
}

if [[ "$(cat "$MEDIA_DIR/.stamp" 2>/dev/null)" != "$MEDIA_STAMP" ]]; then
  log "Generating sample media (first run only)"
  mkdir -p "$VIDEOS" "$MUSIC"

  cat > "$WORK/chapters.txt" <<'EOF'
;FFMETADATA1
[CHAPTER]
TIMEBASE=1/1
START=0
END=95
title=Opening
[CHAPTER]
TIMEBASE=1/1
START=95
END=260
title=First light
[CHAPTER]
TIMEBASE=1/1
START=260
END=455
title=The long night
[CHAPTER]
TIMEBASE=1/1
START=455
END=600
title=Aurora
[CHAPTER]
TIMEBASE=1/1
START=600
END=720
title=Credits
EOF
  make_video "Northern Lights.mp4" 720 "c0=0x06281f:c1=0x1f7a4d:c2=0x0b1d3a:c3=0x6fd3a0:n=4" 7 "$WORK/chapters.txt"
  make_video "Golden Hour.mp4"     90  "c0=0x2a0f3d:c1=0xd9773b:c2=0x0d2440:c3=0xf2c14e:n=4" 3
  make_video "Deep Blue.mkv"       90  "c0=0x03142b:c1=0x1d5c8c:c2=0x0a0f1f:c3=0x58b7d6:n=4" 11
  make_video "Ember.mp4"           90  "c0=0x1a0606:c1=0xa8321f:c2=0x2b0a1c:c3=0xf08a4b:n=4" 5
  make_video "Glacier.mkv"         90  "c0=0x0a1a24:c1=0x9fd8e8:c2=0x16324a:c3=0xe6f4f7:n=4" 13
  make_video "Midnight Run.mp4"    90  "c0=0x0d0b1f:c1=0x5b3fa8:c2=0x120a2e:c3=0xc06bd9:n=4" 17
  make_video "Fern Valley.mp4"     90  "c0=0x0c1f0a:c1=0x6b9b2e:c2=0x1d2b0e:c3=0xd6e58a:n=4" 19

  # Back-to-back cues so a subtitle is on screen whenever the video is paused.
  python3 - "$VIDEOS/Northern Lights.srt" <<'EOF'
import sys
lines = [
    "The sky was quiet for hours.",
    "Then, just after midnight, it began to move.",
    "Green at first, then something closer to silver.",
    "No one spoke. There was nothing to add.",
    "We watched until the light gave out.",
]
def ts(s):
    return f"{s // 3600:02}:{s % 3600 // 60:02}:{s % 60:02},000"
with open(sys.argv[1], "w") as f:
    for i, start in enumerate(range(0, 720, 5)):
        f.write(f"{i + 1}\n{ts(start)} --> {ts(start + 5)}\n{lines[i % len(lines)]}\n\n")
EOF

  make_track "01 Morning Light.mp3" "Morning Light" 220    214 "c0=0x0b3d2e:c1=0x49b35c:c2=0x0d2236:c3=0xb8e6c4:n=4" 21
  make_track "02 Open Road.mp3"     "Open Road"     196    187 "c0=0x2a0f3d:c1=0xd9773b:c2=0x0d2440:c3=0xf2c14e:n=4" 22
  make_track "03 Slow Tide.mp3"     "Slow Tide"     174.61 243 "c0=0x03142b:c1=0x1d5c8c:c2=0x0a0f1f:c3=0x58b7d6:n=4" 23
  make_track "04 Afterglow.mp3"     "Afterglow"     246.94 201 "c0=0x1a0606:c1=0xa8321f:c2=0x2b0a1c:c3=0xf08a4b:n=4" 24
  echo "$MEDIA_STAMP" > "$MEDIA_DIR/.stamp"
fi

# ── Virtual display ───────────────────────────────────────────────────────────
DISPLAY_NUM=99
while [[ -e "/tmp/.X${DISPLAY_NUM}-lock" ]]; do DISPLAY_NUM=$((DISPLAY_NUM + 1)); done
log "Starting Xvfb on :$DISPLAY_NUM (${W}x${H})"
Xvfb ":$DISPLAY_NUM" -screen 0 "${W}x${H}x24" -nolisten tcp +extension GLX >/dev/null 2>&1 &
XVFB_PID=$!
sleep 1
export DISPLAY=":$DISPLAY_NUM"


# ── Helpers ───────────────────────────────────────────────────────────────────
seed_settings() { # config-dir
  mkdir -p "$1/Lumyn"
  python3 - "$1/Lumyn/settings.json" "$VIDEOS" "$MUSIC" "$W" "$H" "$SCALE" <<'EOF'
import hashlib, json, os, sys
out, videos, music = sys.argv[1], sys.argv[2], sys.argv[3]
scale = float(sys.argv[6])
w, h = int(sys.argv[4]) / scale, int(sys.argv[5]) / scale   # logical (DIP) size
key = lambda p: hashlib.sha256(os.path.abspath(p).encode()).hexdigest()
v = lambda n: os.path.join(videos, n)
m = lambda n: os.path.join(music, n)

recent = [
    (v("Northern Lights.mp4"), 268, 720),
    (m("02 Open Road.mp3"), None, None),
    (v("Golden Hour.mp4"), 61, 90),
    (v("Deep Blue.mkv"), 22, 90),
    (m("01 Morning Light.mp3"), None, None),
    (v("Ember.mp4"), 76, 90),
    (v("Glacier.mkv"), 40, 90),
    (m("04 Afterglow.mp3"), None, None),
    (v("Midnight Run.mp4"), 12, 90),
    (v("Fern Valley.mp4"), 55, 90),
]
nl = v("Northern Lights.mp4")
settings = {
    "RecentFiles": [p for p, _, _ in recent],
    "ResumePositions": {key(p): pos for p, pos, _ in recent if pos},
    "ResumeDurations": {key(p): dur for p, _, dur in recent if dur},
    "Bookmarks": {key(nl): [
        {"PositionSeconds": 132, "Label": "First glow on the horizon"},
        {"PositionSeconds": 318, "Label": "Wide shot"},
        {"PositionSeconds": 512, "Label": "Peak of the display"},
    ]},
    "WindowGeometry": {"Width": w, "Height": h, "X": 0, "Y": 0, "Maximized": False},
    "LastVolume": 80,
    "LastSpeed": 1.0,
    "SeekStep": 10,
}
with open(out, "w") as f:
    json.dump(settings, f)
EOF
}

start_app() { # scene-name [file]
  local cfg="$WORK/$1/config"
  rm -rf "$WORK/$1"
  seed_settings "$cfg"
  env -u WAYLAND_DISPLAY \
    XDG_CONFIG_HOME="$cfg" \
    LUMYN_CAPTURE_MODE=1 AVALONIA_GLOBAL_SCALE_FACTOR="$SCALE" \
    DBUS_SESSION_BUS_ADDRESS=unix:path=/nonexistent \
    "$APP" "${@:2}" >"$WORK/$1.log" 2>&1 &
  APP_PID=$!
  # Wait for the main window to map.
  for _ in $(seq 1 60); do
    xdotool search --onlyvisible --pid "$APP_PID" >/dev/null 2>&1 && break
    sleep 0.25
  done
  sleep 2
  xdotool search --onlyvisible --pid "$APP_PID" | head -1 | xargs -r xdotool windowfocus --sync 2>/dev/null || true
}

stop_app() {
  [[ -n "$APP_PID" ]] && kill "$APP_PID" 2>/dev/null || true
  wait "$APP_PID" 2>/dev/null || true
  APP_PID=""
}

key()   { xdotool key --clearmodifiers "$@"; sleep 0.4; }
move()  { xdotool mousemove "$1" "$2"; sleep "${3:-0.4}"; }
click() { xdotool mousemove "$1" "$2" click 1; sleep "${3:-0.8}"; }

shoot() { # file-name
  ffmpeg -v error -y -f x11grab -draw_mouse 0 -video_size "${W}x${H}" -i "$DISPLAY" -frames:v 1 -update 1 "$RAW/$1.png"
  echo "    captured $1.png"
}

RAW="$WORK/raw"
mkdir -p "$RAW"

NL="$VIDEOS/Northern Lights.mp4"
TRACK="$MUSIC/02 Open Road.mp3"

# Screen positions at 1920x1080 / 150% (re-measure here if the chrome moves).
SEEK_Y=966              # seek bar centre line
SETTINGS_BTN="1637 31"  # gear in the top bar
SHORTCUTS_NAV="503 479" # "Shortcuts" in the settings dialog's sidebar

# ── Scenes ────────────────────────────────────────────────────────────────────
log "Scene: home (recently played)"
start_app home
sleep 6   # ffmpeg backfills the card thumbnails
move 1700 700
shoot 01-home
stop_app

log "Scene: video playback with subtitles"
start_app video "$NL"
sleep 4
# Subtitles only draw while playing, and the controls hide 3 s after the last
# pointer move, so wake them and capture straight away.
move 960 520 0.2
move 980 540 0.6
shoot 02-video-subtitles

log "Scene: seek-bar preview"
sleep 4                        # let the thumbnail extractor fill in frames
move 1240 "$SEEK_Y" 1.2
shoot 03-seek-preview

log "Scene: markers"
key space
key b
sleep 1
shoot 04-markers
key Escape
stop_app

log "Scene: audio queue"
start_app audio "$TRACK"
sleep 5                        # queue metadata + cover thumbnails
move 1900 1060
shoot 05-audio-queue
stop_app

log "Scene: settings"
start_app settings "$NL"
sleep 3
key space
click $SETTINGS_BTN 1.5
shoot 06-settings
click $SHORTCUTS_NAV 1
shoot 07-shortcuts
stop_app

# ── Export ────────────────────────────────────────────────────────────────────
MS="$OUT_DIR/microsoft-store"
SNAP="$OUT_DIR/snap-store"
rm -rf "$MS" "$SNAP"
mkdir -p "$MS" "$SNAP"

log "Exporting Microsoft Store set (${W}x${H} PNG, up to 10)"
cp "$RAW"/*.png "$MS/"

# Snap Store: up to 5 images, each under 2 MB. PNG when it fits, else JPEG.
log "Exporting Snap Store set (5 images, < 2 MB each)"
for name in 01-home 02-video-subtitles 05-audio-queue 03-seek-preview 06-settings; do
  src="$RAW/$name.png"
  if [[ $(stat -c %s "$src") -lt 2000000 ]]; then
    cp "$src" "$SNAP/$name.png"
  else
    ffmpeg -v error -y -i "$src" -q:v 2 "$SNAP/$name.jpg"
  fi
done

log "Done"
ls -l "$MS" "$SNAP" | sed 's/^/    /'
