# Store screenshots

Store listing screenshots are generated, not taken by hand. Re-run the capture after any visible UI change (layout, theme, controls, dialogs) and upload the new set.

```bash
sudo apt install -y xvfb xdotool   # one-time
./scripts/capture-store-screenshots.sh
```

It takes about 3–5 minutes (plus ~2 minutes the first time, to generate sample media) and writes:

| Folder | Contents | Upload to |
|---|---|---|
| `docs/store-screenshots/microsoft-store/` | All 7 shots, 1920×1080 PNG | Partner Center → Lumyn → submission → **Store listings** → Screenshots (Desktop). Up to 10, at least 1366×768. |
| `docs/store-screenshots/snap-store/` | Best 5, each under 2 MB (JPEG if a PNG is too large) | snapcraft.io → **My snaps** → lumyn → **Listing** → Screenshots. Up to 5. |

Shots: home (recently played), video with subtitles, seek-bar preview, markers (bookmarks + chapters), audio queue, settings (watch modes), keyboard shortcuts.

## How it works

- Runs the Release build on a private 1920×1080 Xvfb display at 150% UI scale, with a throwaway `XDG_CONFIG_HOME` seeded with recent files, resume positions and bookmarks. Your real settings, desktop and speakers are untouched.
- Sample media is generated with ffmpeg (abstract gradient videos with chapters and an `.srt`, a four-track album with covers), so no third-party content appears. It is cached in `artifacts/screenshot-media/`; bump `MEDIA_STAMP` in the script after adding files.
- `LUMYN_CAPTURE_MODE=1` makes the app accept Mesa's software OpenGL (llvmpipe), which Avalonia rejects by default, and play through mpv's silent but real-time `null` audio output.
- Avalonia's software OpenGL path is flaky under Xvfb: some launches show a black or grid-garbled video surface. The script checks two plain-video strips of every video shot (brightness plus edge density) and relaunches until the frame is clean.
- Mouse positions for the seek bar, settings gear and the dialog's Shortcuts item are constants near the top of the scenes section. If the chrome moves, re-measure them from a fresh capture (`--keep-work` keeps logs and raw frames).
