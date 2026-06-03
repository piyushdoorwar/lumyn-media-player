# Lumyn Media Player — Agent Reference

> **Usage**: At the start of every session, read this file first. It provides a complete picture of the solution — structure, architecture, features, release pipeline, and conventions — so you don't need to crawl the codebase from scratch.
> After completing any feature work, update the relevant section(s) of this file.

---

## Table of Contents

1. [Project Overview](#1-project-overview)
2. [Tech Stack](#2-tech-stack)
3. [Solution Structure](#3-solution-structure)
4. [Architecture](#4-architecture)
5. [Key Source Files](#5-key-source-files)
6. [Features](#6-features)
7. [Native Interop (mpv)](#7-native-interop-mpv)
8. [State & Persistence](#8-state--persistence)
9. [UI Layout & Windows](#9-ui-layout--windows)
10. [Build & Packaging](#10-build--packaging)
11. [CI/CD Workflows](#11-cicd-workflows)
12. [Versioning & Release](#12-versioning--release)
13. [Website / Site](#13-website--site)
14. [Development Setup](#14-development-setup)
15. [Conventions & Patterns](#15-conventions--patterns)

---

## 1. Project Overview

**Lumyn** is a clean, minimal desktop media player built on .NET 10 + Avalonia UI + mpv. It targets Windows x64 and Ubuntu Linux amd64/arm64.

- **Repo**: `lumyn-media-player`
- **Owner/Author**: Piyush Doorwar
- **License**: Source available — non-commercial personal use
- **Current version base**: `1.0` (see `VERSION` file at root)
- **Website**: deployed to GitHub Pages from `/site/`

Design philosophy: quiet, distraction-free interface. No bloat. Let the media play.

---

## 2. Tech Stack

| Layer | Technology |
|---|---|
| Language | C# (latest, nullable enabled, implicit usings) |
| Runtime | .NET 10.0 |
| UI Framework | Avalonia UI 11.3.14 |
| UI Theme | Fluent (Windows 11 style, dark) |
| Media Engine | mpv / libmpv (P/Invoke native bindings) |
| Rendering | OpenGL via mpv render context |
| Packaging | dpkg (Linux .deb), Snapcraft (Linux snap), Inno Setup / MSIX (Windows) |
| Build System | .NET CLI (`dotnet build / publish`) |
| Package Manager | NuGet (centralized via `Directory.Packages.props`) |
| CI/CD | GitHub Actions |
| Deployment | GitHub Releases + GitHub Pages |

---

## 3. Solution Structure

```
lumyn-media-player/
├── Lumyn.sln                        # Visual Studio solution (3 projects)
├── Directory.Build.props            # Global build config (net10.0, nullable, etc.)
├── Directory.Packages.props         # Central NuGet version management
├── VERSION                          # Base version string, currently "1.0"
│
├── docs/
│   └── release/
│       ├── ppa.md                   # Ubuntu PPA setup and required GitHub secrets
│       └── snap-store.md            # Snap Store publish setup and required GitHub secret
│
├── src/
│   ├── Lumyn.App/                   # UI / Presentation layer
│   │   ├── Program.cs               # Entry point
│   │   ├── App.axaml / App.axaml.cs # Application bootstrap, styles, resources
│   │   ├── Assets/
│   │   │   ├── Icons/               # SVG icons + lumyn.ico
│   │   │   └── Styles/Lumyn.axaml   # Custom styling (dark theme overrides)
│   │   ├── Controls/
│   │   │   ├── MpvVideoSurface.cs   # OpenGL video surface (Avalonia control)
│   │   │   ├── VideoSurface.cs      # Video surface wrapper
│   │   │   ├── SeekBar.cs           # Custom timeline/scrubbing control
│   │   │   ├── MiniProgressBar.cs   # Tiny custom-rendered resume progress bar
│   │   │   ├── VolumeSlider.cs      # Volume slider control
│   │   │   └── AudioBars.cs         # Music-reactive equalizer bars (driven by live RMS via LevelProvider)
│   │   ├── Models/
│   │   │   ├── AudioClarityMode.cs # Audio clarity preset enum
│   │   │   ├── VideoAdjustments.cs  # Record for brightness/contrast/saturation/rotation/zoom/aspect
│   │   │   ├── SubtitleSettings.cs  # Subtitle appearance config
│   │   │   └── WatchMode.cs         # Watch mode presets
│   │   ├── ViewModels/
│   │   │   └── MainViewModel.cs     # MVVM command routing, UI state, playlist, subtitle overlay
│   │   └── Views/
│   │       ├── MainWindow.axaml / .axaml.cs   # Main player window (739 lines)
│   │       ├── JumpToTimeDialog.axaml / .axaml.cs
│   │       ├── KeyboardShortcutsDialog.axaml / .axaml.cs
│   │       ├── AboutDialog.axaml / .axaml.cs
│   │       ├── SettingsDialog.axaml / .axaml.cs
│   │       ├── SubtitleSearchDialog.axaml / .axaml.cs
│   │       ├── SubtitleSettingsDialog.axaml / .axaml.cs
│   │       ├── VideoAdjustmentsDialog.axaml / .axaml.cs
│   │       ├── CastDialog.axaml / .axaml.cs
│   │       └── BookmarksDialog.axaml / .axaml.cs
│   │
│   └── Lumyn.Core/                  # Core business logic (no UI dependency)
│       ├── Models/
│       │   ├── MediaState.cs        # Playback state snapshot (position, duration, pause, volume, speed)
│       │   ├── VideoFrameData.cs    # Frame data for OpenGL rendering
│       │   └── SubtitleSearchResult.cs
│       └── Services/
│           ├── PlaybackService.cs        # mpv wrapper, 995 lines — main engine
│           ├── SettingsService.cs        # JSON persistence, 257 lines
│           ├── ChromecastCastService.cs  # Chromecast (Google Cast v2) discovery, HTTP file server, cast control
│           ├── SubtitleParser.cs         # SRT/ASS/SSA/VTT parser
│           └── SubtitleSearchService.cs  # Online subtitle search
│
│   └── Lumyn.Test/                  # xUnit tests for core and important deterministic logic
│       ├── SubtitleParserTests.cs   # SRT/ASS parsing behavior
│       ├── SettingsServiceTests.cs  # settings, resume, bookmarks, hashed per-file keys
│       └── ChromecastCastServiceTests.cs # cast format support decisions
│
├── scripts/
│   ├── build-linux.sh               # Linux .deb packaging (266 lines)
│   ├── build-windows.ps1            # Windows installer via Inno Setup (363 lines, PowerShell)
│   └── build-snap.sh                # Snap / Ubuntu App Center packaging wrapper
│
├── packaging/
│   ├── windows/                     # Inno Setup .iss config
│   └── linux/                       # Linux packaging resources (desktop file, MIME types)
│
├── artifacts/                       # Build output (gitignored)
│   ├── packages/                    # Final distributable packages
│   ├── publish/                     # Intermediate dotnet publish output
│   └── pkg/                         # Packaged app structures
│
├── site/                            # Static website (GitHub Pages)
│
└── .github/
    ├── agents.md                    # THIS FILE
    └── workflows/
        ├── build-artifacts.yml      # Build all platforms on push to main
        ├── release.yml              # Tag-triggered release with GitHub Release artifacts
        └── static.yml              # Deploy /site to GitHub Pages
```

### NuGet Dependencies (`Directory.Packages.props`)

```
Avalonia             11.3.14
Avalonia.Desktop     11.3.14
Avalonia.Themes.Fluent  11.3.14
Avalonia.Fonts.Inter    11.3.14
```

`Lumyn.Core` NuGet dependencies:
```
GoogleCast           1.7.0   (Google Cast v2 protocol — includes Zeroconf for mDNS and protobuf-net)
```

`Lumyn.Test` NuGet dependencies:
```
Microsoft.NET.Test.Sdk
xunit
xunit.runner.visualstudio
coverlet.collector
```

---

## 4. Architecture

Pattern: **MVVM + Service Layer**, single process, single window.

```
┌─────────────────────────────────────────────────────┐
│  Views (Avalonia XAML)                              │
│  MainWindow + Dialogs                               │
└───────────────────┬─────────────────────────────────┘
                    │ Data binding (INPC)
┌───────────────────▼─────────────────────────────────┐
│  MainViewModel                                      │
│  - RelayCommand pattern                             │
│  - UI state (IsPlaying, Position, Title, etc.)      │
│  - Playlist / queue management                      │
│  - Subtitle overlay rendering (Avalonia text layer) │
│  - Dispatcher.UIThread for all UI updates           │
└───────────────────┬─────────────────────────────────┘
                    │ Direct method calls + events
┌───────────────────▼─────────────────────────────────┐
│  Lumyn.Core Services                                │
│                                                     │
│  PlaybackService (995 lines)                        │
│  ├─ mpv P/Invoke bindings                           │
│  ├─ OpenGL render context (MpvVideoSurface)         │
│  ├─ Background event loop thread                    │
│  ├─ Track management (audio/subtitle)               │
│  ├─ Video adjustments (brightness/contrast/etc.)    │
│  └─ Events: StateChanged, EndReached, ErrorOccurred │
│                                                     │
│  SettingsService (257 lines)                        │
│  ├─ ~/.config/Lumyn/settings.json (Linux)           │
│  ├─ Resume positions, bookmarks, subtitle config    │
│  └─ File paths hashed via SHA256 (privacy)          │
│                                                     │
│  ChromecastCastService                                    │
│  ├─ mDNS DNS-SD discovery of Chromecast devices          │
│  ├─ Google Cast v2 protocol (TLS + protobuf, via         │
│  │   GoogleCast 1.7.0 NuGet package + Zeroconf)          │
│  ├─ HTTP server to serve local files to cast device      │
│  ├─ SRT/VTT → WebVTT conversion for subtitle tracks      │
│  └─ Playback control + status polling over Cast protocol │
│                                                     │
│  SubtitleParser (static)                            │
│  └─ SRT / ASS / SSA parsing + HTML tag stripping    │
│                                                     │
│  SubtitleSearchService                              │
│  └─ Online subtitle search integration             │
└───────────────────┬─────────────────────────────────┘
                    │ P/Invoke
┌───────────────────▼─────────────────────────────────┐
│  libmpv (native, OS-provided or bundled)            │
│  - Hardware decoding (hwdec=auto-safe)              │
│  - All audio/video format support                   │
│  - Audio output                                     │
└─────────────────────────────────────────────────────┘
```

### Startup Sequence

1. `Program.cs` → sets X11 options (disables IBus IME for Ubuntu 26.04 compat)
2. `App.axaml.cs` → creates `PlaybackService`, `SettingsService`, `ChromecastCastService`
3. Creates `MainViewModel` injecting all services
4. Creates `MainWindow` with `ViewModel` as `DataContext`
5. Checks for command-line file argument → calls `OpenFileWhenReadyAsync()`

### Playback Flow

1. User opens file → `MainViewModel.OpenFileAsync()`
2. Calls `PlaybackService.OpenAsync(filePath, resumePosition)`
3. PlaybackService sends mpv `loadfile` command via P/Invoke
4. mpv event loop thread processes events (250ms timeout)
5. `MediaState` snapshot updated under lock
6. `StateChanged` event fired → ViewModel receives, dispatches UI refresh

### Rendering Pipeline

1. `MpvVideoSurface` (Avalonia control) initializes on first render
2. Calls `PlaybackService.InitializeRenderer(getProcAddress, requestRender)`
3. mpv OpenGL render context created
4. Each frame: `RenderVideo(framebuffer, width, height)` called
5. mpv renders directly to OpenGL framebuffer

---

## 5. Key Source Files

| File | Lines | Role |
|---|---|---|
| `src/Lumyn.Core/Services/PlaybackService.cs` | 995 | All mpv interaction, playback control, rendering, track management |
| `src/Lumyn.Core/Services/SettingsService.cs` | 257 | JSON persistence: resume positions, bookmarks, subtitle settings, recent files |
| `src/Lumyn.App/ViewModels/MainViewModel.cs` | 500+ | MVVM hub: commands, UI state, playlist, subtitle overlay, cast UI |
| `src/Lumyn.App/Views/MainWindow.axaml` | 739 | Full UI layout — video surface, controls, overlays |
| `src/Lumyn.Core/Services/ChromecastCastService.cs` | 200+ | Chromecast discovery (mDNS), HTTP server, cast control, SRT→VTT |
| `src/Lumyn.Core/Services/SubtitleParser.cs` | 150+ | SRT/ASS/SSA parsing |
| `src/Lumyn.App/Controls/MpvVideoSurface.cs` | — | Avalonia OpenGL surface for mpv rendering |
| `src/Lumyn.App/Controls/SeekBar.cs` | — | Custom timeline scrubbing control |
| `src/Lumyn.App/Controls/MiniProgressBar.cs` | — | Custom 3px recent-card progress indicator; avoid default `ProgressBar` template for tiny bars |
| `src/Lumyn.Core/Services/ThumbnailExtractor.cs` | — | Secondary silent mpv instance for seek-bar hover frame previews. Generates JPEG frames in a background Task in two phases: `Phase1Count` (20) coarse evenly-spaced frames first, then `Phase2PerMinute` (12/min) refinement. `GetNearest(progress)` returns the closest frame's bytes. |
| `src/Lumyn.Core/Services/QueueMetadataProbe.cs` | — | Secondary silent mpv instance that reads duration / `media-title` / artist for a queued file without playing it (`Probe(path, ct)` → `QueueProbeResult`). Drives the audio-queue rows. |
| `src/Lumyn.Core/Services/MediaTime.cs` | — | `FormatDuration(TimeSpan?)` → `m:ss` / `h:mm:ss` (blank if unknown). Unit-tested. |
| `src/Lumyn.App/Converters.cs` | — | `Converters.QueueColumnWidth` (`FuncValueConverter<bool,GridLength>`): true → `3*` (~30% beside the `7*` hero), false → `0`; collapses the audio queue column for a single track. Referenced via `x:Static`. |

---

## 6. Features

### Playback
- Play / Pause / Stop
- Seek by absolute position or relative offset
- Frame step forward / backward
- Speed control: 0.25x – 4.0x (steps of 0.25)
- Volume control: 0–150% with mute toggle
- Loop file toggle
- Playlist / queue: add, remove, clear
- Audio track selection (cycle or direct)
- Subtitle track selection (cycle, direct, or off)
- Chapter navigation (next/previous via mpv)

### Video
- Hardware-accelerated decoding (`hwdec=auto-safe`)
- Brightness, Contrast, Saturation adjustments (−100 to +100)
- Video rotation: 0°, 90°, 180°, 270°
- Zoom control (log₂ scale)
- Aspect ratio override: 16:9, 4:3, 2.35:1, 1:1, auto
- Screenshot capture to file (also copied to the system clipboard when supported)
- Fullscreen mode (maximize conflict fix landed in recent commits)

### Subtitles
- Load external: SRT, ASS, SSA, VTT, SUB
- Embedded subtitle track selection
- Subtitle appearance: font, size, color
- Subtitle sync delay adjustment (ms precision)
- SRT/ASS/SSA parser with timing extraction
- Subtitle overlay rendered in Avalonia (avoids mpv subtitle duplication)
- Online subtitle search integration

### Audio
- Track cycling and direct selection
- Volume normalization (0–150%)
- Metadata reading (title, artist, album)
- Cover art detection and display
- **YouTube-Music-style two-pane audio view** (when the queue has 2+ tracks): left ~70% shows the cover, title/artist, and the waveform below it; right ~30% is the queue ("Up next") with a cover thumbnail, title/artist, and duration per row. Click a row to play, drag the handle to reorder, hover/right-click to remove. The currently-playing row is highlighted with a soft Lumyn-green tint (`#1649B35C`) and light-green title. A single file keeps the centered hero (the queue column collapses via `Converters.QueueColumnWidth`). **The queue UI exists only in this audio view** — there is no video/sidebar queue. Per-row duration/title/artist are filled by `QueueMetadataProbe` on a single cancellable background task; covers reuse the recent-card audio pipeline (`ExtractAudioCoverArt` → thumbs cache). Cached in `MainViewModel._queueMeta` (`QueueTrackMeta`), surfaced through `PlaylistItem`. The underlying `_playlist` still drives next/previous-track navigation for both audio and video.
- Audio-only mode shows cover art on the left and title/artist + a live music-reactive equalizer (`AudioBars`) on the right, over a subtle ambient backdrop (the cover art stretched `UniformToFill` with a `BlurEffect` + dark `#111111` scrim, shown only when `HasCoverArt`) — like YT/Apple Music. Bars are driven by real loudness, not a fixed animation: `PlaybackService.SetAudioMetering(true)` (toggled with audio mode) adds a labelled `@viz:lavfi=[astats=metadata=1:reset=1]` pass-through filter, and `GetAudioLevel()` reads `af-metadata/viz/lavfi.astats.Overall.RMS_level` (dBFS) normalized to 0..1. `AudioBars.LevelProvider` (wired in `MainWindow.axaml.cs`) pulls that level each frame; falls back to a decorative wave when no level is available. Note: `SetAudioFilter` re-asserts the `@viz` filter so audio-clarity changes don't drop metering.

### Navigation & Persistence
- Recent files list (last 12 files) with resume percentage badge (top-left), full-bleed thumbnail, and miniature progress bar at card bottom
- Video thumbnails cached per file (`~/.config/Lumyn/thumbs/{sha256_16}.jpg`) extracted via ffmpeg at the resume position (or 30% fallback). Re-extracted on stop at the exact stopped position. Backfilled at startup for all recent files with no cached thumbnail.
- Audio cover art for recently played cards uses the external sidecar image (same basename + `.png`/`.jpg`/etc., or `cover.jpg`/`folder.jpg` in the same directory) — same source as the audio player cover art display. Falls back to ffmpeg `-map 0:v:0` for embedded cover art if no sidecar file exists.
- `SettingsService.GetThumbnailPath(filePath)` / `GetExistingThumbnail(filePath)` manage the thumbnail cache directory.
- Resume playback position per file (SHA256-hashed path for privacy), gated per media kind by a **Playback** settings toggle — `ResumeAudio` (default **off**) and `ResumeVideo` (default **on**) in `SettingsService` (`ResumeEnabledFor(isAudio)`). When a kind's toggle is off, positions for that kind are neither saved nor restored, and recent-card progress badges are hidden for it.
- Markers dialog combines editable user bookmarks with read-only embedded video chapters
- Bookmarks support add/edit/remove/jump per file; embedded chapters support jump only
- Jump-to-time dialog
- Configurable seek step: 5 / 10 / 30 seconds
- Volume and speed persisted across sessions
- Subtitle settings persisted per file
- Main window size/position/maximized state persisted across sessions (clamped on-screen; fullscreen not persisted)

### Casting
- Chromecast (Google Cast v2) device discovery via mDNS (`_googlecast._tcp`)
- Cast to Chromecast devices
- Built-in HTTP server to serve local files to cast devices (byte-range support for seeking)
- Windows installer adds a private/domain inbound Windows Firewall rule for `Lumyn.exe` named `Lumyn Cast`; MSIX declares `internetClient` and `privateNetworkClientServer`.
- Cast server chooses an active multicast-capable IPv4 LAN interface, preferring adapters with a gateway and Ethernet/Wi-Fi over virtual adapters.
- SRT/VTT subtitle files converted to WebVTT and passed as `tracks[]` in Cast load request
- Playback control (play, pause, seek, volume) over Google Cast protocol
- **Supported formats**: MP4 (`video/mp4`), WebM (`video/webm`), and audio formats (MP3/AAC/FLAC/Opus/WAV)
- **Unsupported formats**: MKV, AVI, WMV, MOV, FLV, TS, M2TS — Chromecast cannot natively play these containers. The Cast dialog shows a clear "not supported" message and disables the Cast button when such a file is loaded. No transcoding is performed.

### Screen Sleep Inhibition
- `ScreenInhibitor` service (`Lumyn.Core/Services/ScreenInhibitor.cs`) prevents screen dim, lock, and system sleep while media is playing
- Driven automatically by `IsPlaying` property setter in `MainViewModel`
- Windows: `SetThreadExecutionState(ES_CONTINUOUS | ES_DISPLAY_REQUIRED | ES_SYSTEM_REQUIRED)` via kernel32 P/Invoke
- Linux: `org.freedesktop.ScreenSaver.Inhibit` on D-Bus session bus via `dbus-send` subprocess (cookie-based; works on GNOME, KDE, and freedesktop-compliant desktops)
- Inhibition released on pause, stop, end-of-file, or app close

### UI & Platform
- Clean dark theme (#111111 bg, #DEDAD5 text)
- Always-on-top toggle
- Drag-to-move via title bar
- Keyboard shortcuts (full list in KeyboardShortcutsDialog)
- OSD toast: top-left, translucent (`#99` opacity), subtle border, small bare icon — intentionally unobtrusive so it doesn't pull attention from the video. Uses `Grid ColumnDefinitions="Auto,*"` so long messages wrap instead of overflowing the pill.
- Scrollbar: 6px wide/tall, green-tinted thumb (`#3A9B4B`), opacity-based hover (0.35 → 0.75 → 1.0), no arrow buttons, applied globally via `Lumyn.axaml`.
- Queue is shown only in the audio two-pane view (no separate video/sidebar queue)
- Ubuntu GNOME "Open With" integration (`.desktop` entry + MIME types)
- Windows file associations through Inno Setup registry entries (`packaging/windows/lumyn.iss`) and MSIX declarations (`packaging/windows/AppxManifest.xml`); `scripts/build-windows.ps1` also contains a portable `Register-FileAssociations.ps1` generator helper.
- Command-line startup file support: `App.axaml.cs` accepts normal file paths and `file://` URIs from `desktop.Args`, then calls `OpenFileWhenReadyAsync()`.
- Chromecast/cast icon uses the Font Awesome style filled cast silhouette in `MediaIcons.axaml` and `site/assets/ic-cast.svg`; app cast accents should use Lumyn green (`#49B35C` / `#3A9B4B`), not blue.

---

## 7. Native Interop (mpv)

All mpv interaction is in `PlaybackService.cs` via `LibraryImport` / P/Invoke.

**Key bindings used:**
```
mpv_create, mpv_initialize, mpv_set_option_string
mpv_observe_property, mpv_set_property, mpv_get_property
mpv_command, mpv_wait_event (250ms timeout)
mpv_render_context_create, mpv_render_context_render, mpv_render_context_free
```

**Observed mpv properties:**
```
time-pos, duration, pause, mute, volume, speed
aid (audio track ID), sid (subtitle track ID)
brightness, contrast, saturation, video-rotate, video-zoom
video-aspect-override, loop-file
track-list, chapter-list, metadata
```

**Library name resolution** handles DLL (Windows) and `.so.2` / `.so` (Linux) variants at runtime.

**Unsafe code** is enabled in both projects for performance-critical native pointer work.

---

## 8. State & Persistence

### MediaState (in-memory snapshot)
```csharp
public class MediaState {
    public string? FilePath;
    public TimeSpan Position;
    public TimeSpan Duration;
    public bool IsPlaying;
    public bool IsMuted;
    public int Volume;      // 0–150
    public float Speed;     // 0.25–4.0
    public bool IsLooping;
}
```

Updated under lock in the mpv event loop thread. `StateChanged` event dispatches to ViewModel.

### Settings Persistence

- **Location**: `~/.config/Lumyn/settings.json` (Linux); equivalent app-data location on Windows
- **Thumbnail cache**: `~/.config/Lumyn/thumbs/{sha256_16chars}.jpg` — one JPEG per file, keyed by first 16 hex chars of SHA256(normalized path). Created by the app on startup.
- **Format**: compact JSON. Writes are **debounced** (~400ms `System.Threading.Timer`) and written atomically (temp file + `File.Move`) off the UI thread; `Flush()` forces a synchronous write and runs on app close. `SettingsService` is `IDisposable`.
- **Contents**:
  - Resume positions (key = SHA256 of normalized file path)
  - Bookmarks per file
  - Subtitle settings per file (font, size, color, delay)
  - Audio clarity mode per file
  - Recent files list (max 12)
  - Main window geometry (`WindowGeometry`: width/height/x/y/maximized)
  - Resume preferences: `ResumeAudio` (default false), `ResumeVideo` (default true)
  - Global: volume, speed, seek step preference, UI visibility toggles

---

## 9. UI Layout & Windows

### MainWindow
- **Default size**: 980×620; **Minimum**: 640×380
- **Decorations**: `BorderOnly` (custom title bar)
- **Background**: `#111111`; **Foreground**: `#DEDAD5`
- **Theme**: Fluent dark + custom `Lumyn.axaml` overrides

```
┌─────────────────────────────────────────────┐  ← TopBar (38px, collapsible in fullscreen)
│  Logo | Open | Subtitles | Adj | Playlist   │
│                 Title (draggable)           │
│  About | Shortcuts | Settings | Window Ctrl │
├─────────────────────────────────────────────┤
│                                             │
│   MpvVideoSurface (OpenGL — fills area)     │
│                                             │
│   SubtitleOverlay (Avalonia text layer)     │
│                                             │
├─────────────────────────────────────────────┤  ← Bottom controls
│  SeekBar                                   │
│  Play | << | >> | Vol | Speed | Loop | Mute │
└─────────────────────────────────────────────┘
```

In audio mode with 2+ tracks the centre area becomes a 70/30 split: cover + info + waveform on the left, the "Up next" queue on the right. Video has no queue UI.

### Recent Files Start Screen

- Recent files are exposed as `RecentFileItem` records from `MainViewModel.RecentFileItems`. Both video and audio items use one shared 200×120 card template.
- `SettingsService.GetResumeInfo()` returns resume position plus progress percentage (`0–100`), or `-1` when no resume state exists.
- The progress percentage is shown as a small badge (`ProgressLabel`) in the top-left corner of each card, visible only when a resume position exists.
- The tiny green progress bar at the card bottom uses `Controls/MiniProgressBar.cs`, not Avalonia's built-in `ProgressBar`. The default template renders incorrectly at 3px height.
- **Thumbnail image** (`RecentFileItem.ThumbnailBitmap`) is loaded lazily from the thumbnail cache path (`ThumbnailPath`). `HasThumbnail` controls visibility. The `_cachedBitmap` field is mutable on a `record` — this is intentional for lazy loading.
- **Thumbnail extraction** is triggered in three places: (1) during active playback in `RefreshState()` once per file; (2) on `Stop()` at the exact stopped position using the captured `CurrentMediaPosition`; (3) backfill at startup via `BackfillRecentThumbnails()` for all recent files without a cached thumbnail. All extraction is `Task.Run()` off the UI thread; completion posts `NotifyRecentFilesChanged` to `Dispatcher.UIThread`.
- **ffmpeg extraction** uses `-ss {seek} -i {file} -vframes 1 -vf scale=320:-2 -f image2 {tempPath}`. The `-f image2` flag is required on ffmpeg 8+ for non-image extensions (`.tmp` fails without it). Temp file is written to `outputPath + ".tmp"` then atomically moved.
- **Audio cover art** uses `FindCoverArtPath()` first (sidecar files), then falls back to `ffmpeg -map 0:v:0` for embedded cover art.
- Stop/end/cast-stop paths must call `NotifyRecentFilesChanged()` after saving or clearing resume state so recent-card percentages refresh immediately without requiring app restart.

### Dialogs

| Dialog | Purpose |
|---|---|
| `JumpToTimeDialog` | Skip to specific timestamp |
| `KeyboardShortcutsDialog` | Help overlay for all hotkeys |
| `AboutDialog` | Version + credits |
| `SubtitleSearchDialog` | Online subtitle search |
| `SubtitleSettingsDialog` | Font, size, color, delay |
| `VideoAdjustmentsDialog` | Brightness/contrast/saturation/rotation/zoom/aspect |
| `CastDialog` | Chromecast device selection |
| `BookmarksDialog` | Markers dialog: manage user bookmarks and jump to embedded chapters |

---

## 10. Build & Packaging

### Local Dev

```bash
dotnet restore Lumyn.sln
dotnet build Lumyn.sln
dotnet run --project src/Lumyn.App/Lumyn.App.csproj
```

### Linux — `.deb` package (`scripts/build-linux.sh`, 266 lines)

1. `dotnet publish -c Release -r linux-x64 --self-contained true`
2. Locate `libmpv.so.2` via `ldconfig` / filesystem search
3. Bundle libmpv + all dependencies (`ldd`)
4. Build `.deb` structure:
   - `/opt/lumyn/` — binaries + bundled libs
   - `/usr/bin/lumyn` — symlink to launcher script
   - `/usr/share/applications/lumyn.desktop`
   - `/usr/share/icons/`
   - `/usr/share/mime/packages/` — MIME type definitions
5. `dpkg-deb` → `lumyn_X.X.X_amd64.deb`
6. Supports both `amd64` and `arm64`

**Dependencies needed on build machine**: `libmpv-dev`, `dpkg`
**Runtime package dependencies**: none (libmpv bundled; no `Depends:` field in control file)

### Linux — Ubuntu PPA (`packaging/debian/` + `.github/workflows/release.yml`)

- Debian source package metadata lives in `packaging/debian/changelog`, `packaging/debian/control`, and `packaging/debian/rules`
- Release workflow job: `publish-ppa`
- PPA target: `ppa:piyushdoorwar/lumyn`
- Upload target in `dput`: `~piyushdoorwar/ubuntu/lumyn/`
- Package version is generated as `${VERSION}-0piyushdoorwar1`
- Current target distribution in changelog updates: `resolute`
- The workflow vendors NuGet packages into `./packages`, stages `packaging/debian/` into the root-level `debian/` directory expected by Debian tooling, builds an unsigned source package with `dpkg-buildpackage -S`, signs it with `debsign`, then uploads with `dput`
- Required GitHub secret: `GPG_PRIVATE_KEY`
- Optional GitHub secret: `GPG_PASSPHRASE` when the exported private key is passphrase-protected
- Setup details are documented in `docs/release/ppa.md`

### Windows — `.exe` installer (`scripts/build-windows.ps1`, 363 lines)

1. `dotnet publish -c Release -r win-x64 --self-contained true`
2. Resolve mpv:
   - Auto-download from `shinchiro/mpv-winbuild-cmake` GitHub releases
   - Extract via 7z/7za
   - Or use manually provided `MPV_BIN_DIR`
3. Copy `mpv-2.dll` + dependencies
4. Generate `Register-FileAssociations.ps1` for post-install user run
5. Compile Inno Setup `.iss` → `lumyn_X.X.X_win-x64_setup.exe`

File association notes:
- Inno Setup registers common audio/video extensions in `packaging/windows/lumyn.iss`.
- Inno Setup also adds/removes the `Lumyn Cast` Windows Firewall rule. Keep it scoped to domain/private profiles.
- MSIX file type declarations and private-network capabilities live in `packaging/windows/AppxManifest.xml`.
- The portable registration helper in `scripts/build-windows.ps1` writes `Register-FileAssociations.ps1`, but the main installer/MSIX path should be checked first before changing that helper.

**Dependencies needed on build machine**: Inno Setup, 7-Zip

### Linux — Snap / Ubuntu App Center (`packaging/snap/snapcraft.yaml`)

- Snapcraft project file: `packaging/snap/snapcraft.yaml`
- Desktop launcher metadata: `packaging/snap/gui/lumyn.desktop`
- Build wrapper: `scripts/build-snap.sh` stages `packaging/snap/snapcraft.yaml` into Snapcraft's expected root-level `snap/` path, runs `snapcraft`, then removes the temporary root `snap/` directory
- GitHub Actions stage the same temporary root `snap/snapcraft.yaml` and build via `canonical/action-build@v1`; the resulting `*.snap` is uploaded as `lumyn-linux-amd64-snap`
- Core26 snap builds must pass `snapcraft-channel: 9.x/candidate` to `canonical/action-build@v1`; the action's default stable track can use an older Snapcraft schema that rejects `base: core26` stable snaps, and `9.x/stable` was not available when core26 packaging was added.
- Release workflow injects the resolved `VERSION` into staged `snap/snapcraft.yaml` as a part `build-environment` entry before `canonical/action-build@v1`; this is required because Snapcraft's managed build instance does not reliably inherit the outer GitHub job environment
- Release workflow verifies `meta/snap.yaml` inside the packed snap before publishing, and fails if the snap version does not match the release version
- Both build and release workflows verify that the packed snap contains `opt/lumyn/lib/libmpv.so.2`, the app-local compatibility symlink, and a launcher that exports `opt/lumyn/lib` in `LD_LIBRARY_PATH`.
- The release workflow publishes the snap to the Snap Store with `snapcraft upload --release="$SNAP_CHANNEL"` when `SNAPCRAFT_STORE_CREDENTIALS` is configured
- Snap Store target: registered snap name `lumyn`
- Release channel mapping: stable versions like `1.2.3` publish to `stable`; prerelease versions like `1.2.3-beta.1` or `0.0.0-dev` publish to `edge`
- Required GitHub secret: `SNAPCRAFT_STORE_CREDENTIALS` generated with `snapcraft export-login --snaps=lumyn --channels=stable,edge snapcraft-login`
- Setup details are documented in `docs/release/snap-store.md`
- Uses `base: core26`, `confinement: strict`, and self-contained `dotnet publish`
- Build installs .NET 10 via `dotnet-install.sh` inside the Snapcraft build part so the snap is not blocked by host SDK availability
- Stages `libmpv2` and desktop/audio/OpenGL runtime libraries through `stage-packages`
- `packaging/snap/lumyn-launcher` constructs runtime library paths from `$SNAP`, `uname -m`, and the architecture triplet. Keep `$SNAP/opt/lumyn` and `$SNAP/opt/lumyn/lib` first so bundled `libmpv` wins over host libraries.
- On Linux, `PlaybackService` intentionally tries app-local bundled `libmpv` paths before generic system names. This prevents `.deb` installs from silently masking packaging bugs with host libraries and is required for strict snap confinement.
- Declared plugs include `home`, `removable-media`, `audio-playback`, `opengl`, `network`, `network-bind`, `screen-inhibit-control`, `wayland`, and `x11`
- `removable-media` and Chromecast/mDNS-related access may need store review or manual connection depending on final Snap Store policy and interface behavior

Build/test locally:

```bash
./scripts/build-snap.sh
sudo snap install ./lumyn_*.snap --dangerous
lumyn
```

---

## 11. CI/CD Workflows

### `build-artifacts.yml` — triggered on push to `main` or manual dispatch

| Job | Runner | Output artifact |
|---|---|---|
| `unit-tests` | ubuntu-latest | runs `dotnet test Lumyn.sln --configuration Release` |
| `linux-deb` (matrix) | ubuntu-latest (amd64) / `ubuntu-24.04-arm` (arm64) | `lumyn-linux-amd64-deb`, `lumyn-linux-arm64-deb` (*.deb) |
| `linux-snap` | ubuntu-latest | `lumyn-linux-amd64-snap` (*.snap) |
| `windows-installer` | windows-latest | `lumyn-windows-x64-installer` (*_setup.exe) |

All jobs install .NET 10.0 SDK and cache NuGet packages via `actions/cache@v4` (key: OS+arch+hash of `Directory.Packages.props`/`**/*.csproj`). The workflow has a `concurrency` group that cancels superseded runs. The arm64 `.deb` must build on a native arm64 runner because `build-linux.sh` bundles the host-architecture `libmpv` (via `ldd`); cross-building on amd64 would ship a broken arm64 package.

### `release.yml` — triggered on `v*` tag push or manual dispatch

- Runs same build jobs as `build-artifacts.yml` (including the amd64+arm64 `.deb` matrix and NuGet caching)
- Additional `github-release` job: generates a `checksums.sha256` manifest over all downloaded artifacts and attaches it plus all artifacts to a GitHub Release
- `linux-snap` also publishes the built snap to the Snap Store using the `SNAPCRAFT_STORE_CREDENTIALS` secret

### `static.yml` — triggered on push to `main`

- Deploys `/site/` directory to GitHub Pages

---

## 12. Versioning & Release

- **Version source**: Git tag. Push a tag like `v1.2.3` or `v2.0.0-beta.1` → `release.yml` fires automatically.
- **Tag format**: `v{MAJOR}.{MINOR}.{PATCH}` for production, `v{MAJOR}.{MINOR}.{PATCH}-{label}` for pre-release (e.g. `v1.2.0-beta.1`, `v2.0.0-rc.1`).
- **Pre-release vs production**: Controlled entirely in the GitHub UI — the workflow does **not** set the `prerelease` flag. Push the tag, then mark the release as pre-release or production in GitHub manually.
- **How the version flows**:
  1. `release.yml` extracts the version from the tag (`v1.2.3` → `1.2.3`) in a `prepare` job
  2. All build jobs receive it as the `VERSION` env var
  3. Standard build scripts pass it to `dotnet publish` via `-p:Version=... -p:InformationalVersion=...`
  4. Snap releases also inject it into staged `snap/snapcraft.yaml` as a part `build-environment` value, because the managed Snapcraft build does not reliably inherit the outer job env
  5. The version is baked into the assembly `AssemblyInformationalVersionAttribute`
  6. `AboutDialog.axaml.cs` reads it from the attribute at runtime
- **Local dev builds**: Show `0.0.0-dev` — set as default `<InformationalVersion>` in `Lumyn.App.csproj`
- **CI builds** (`build-artifacts.yml` on push to main): Use `VERSION=0.0.0-dev` — for build validation only, not releases
- **Manual trigger**: `release.yml` supports `workflow_dispatch` with an explicit version input for testing
- **VERSION file**: Deleted — no longer needed
- **Artifact names**: e.g., `lumyn_1.2.3_amd64.deb`, `lumyn_1.2.3_win-x64_setup.exe`

### Release checklist

1. Merge everything to `main`
2. Push a tag: `git tag v1.2.3 && git push origin v1.2.3`
3. `release.yml` fires — builds all platforms, creates GitHub Release with correct version in binary and package filenames

---

## 13. Website / Site

- Located at `/site/` in the repo
- Static HTML/CSS site
- Deployed automatically to GitHub Pages via `static.yml` on every push to `main`
- URL: `https://piyushdoorwar.github.io/lumyn-media-player/`
- Contains: landing page, download links, documentation
- Landing page download section uses OS tabs that default from the visitor's system: Linux shows Ubuntu App Center / Snapcraft first and `.deb` second, and Windows shows Microsoft Store + standalone `.exe`.

### Releases page (`/site/releases/`)

- `index.html` — static markup; OS filter tabs + stable-only toggle
- `releases.js` — fetches all non-draft releases from GitHub API, renders paginated list

**Filters:**
- **OS tabs** — All / Linux / Windows (filters by asset type)
- **Stable only toggle** — pill toggle, checked by default; hides pre-releases when on; unchecking reveals pre-releases (shown with `badge-pre` badge)

**JS state variables in `releases.js`:**
- `currentOS` — active OS tab value (`"all"`, `"linux"`, `"windows"`)
- `stableOnly` — boolean, `true` by default; toggled by `#stableOnlyToggle` checkbox
- `currentPage` — current pagination page (resets to 1 on any filter change)

---

## 14. Development Setup

### Ubuntu Linux

```bash
# Install .NET SDK 10.0
# (via Microsoft repo or snap)

# Install libmpv
sudo apt install libmpv-dev

# Clone and run
git clone ...
cd lumyn-media-player
dotnet run --project src/Lumyn.App/Lumyn.App.csproj

# Run tests
dotnet test Lumyn.sln
```

### Windows

- Install .NET SDK 10.0
- Download `mpv-2.dll` from mpv builds and place in project output or set `MPV_BIN_DIR`
- Run via VS / `dotnet run`

---

## 15. Conventions & Patterns

- **MVVM**: Views bind to `MainViewModel` via `DataContext`. No code-behind logic beyond Avalonia event wiring.
- **RelayCommand**: Standard `ICommand` wrapper used throughout `MainViewModel`.
- **Thread safety**: All UI updates via `Dispatcher.UIThread.InvokeAsync`. mpv event loop runs on dedicated background thread `"Lumyn mpv events"`. `MediaState` guarded by lock.
- **Nullable**: Enabled globally. All fields/properties use nullable annotations.
- **No mocks in architecture**: Services are concrete classes, injected via constructor. No DI container — manual injection in `App.axaml.cs`.
- **File path hashing**: SHA256 used for all per-file storage keys to avoid storing raw paths in settings JSON.
- **Unsafe code**: Allowed in both projects for mpv P/Invoke and OpenGL interop.
- **Platform detection**: Runtime OS check for library name variants (`.dll` / `.so.2`).
- **Settings path**: `Environment.GetFolderPath(SpecialFolder.ApplicationData)` + `Lumyn/settings.json`.
- **Avalonia resources**: Icons and styles defined in `App.axaml` as `Application.Resources`. Referenced in XAML as `StaticResource`.
- **Custom controls**: Placed in `Lumyn.App/Controls/`. Inherit from Avalonia primitives (e.g., `Control`, `Slider`).
- **Tiny progress visuals**: For very small progress indicators, prefer a custom-rendered `Control` (like `MiniProgressBar`) over styling Avalonia `ProgressBar`; template layout can make tiny fills appear full or empty incorrectly.
- **Seek/timeline hit targets**: `SeekBar` intentionally has a larger invisible hit area than its visible track. Preserve that ergonomic leeway when adjusting bottom controls.
- **Idle timer gating**: `MainWindow` runs `_positionTimer` (200ms refresh), `_glowTimer` (2.5s ambient-glow sample), and `_glowLerpTimer` (80ms color lerp) only when they have work — driven by `UpdateActivityTimers()` off the VM's `IsPlaying`/`IsCasting`/`HasMedia`/`IsAudioMode`. Position runs while playing/casting; glow sampling while a local video plays; the lerp stops itself once the border color settles. Paused/seek updates still arrive via `PlaybackService.StateChanged → QueueRefreshState`. Don't reintroduce unconditional `.Start()` on these — it causes constant idle wakeups.
- **Seek-thumbnail bitmap cache**: `MainWindow._thumbBitmapCache` (decoded hover-preview bitmaps, 0–1999 progress-bucket keys) is an LRU bounded to `MaxThumbBitmaps` (64); evicted and file-change/closed bitmaps are disposed. Keep eviction + disposal when touching it so heavy scrubbing can't grow memory unbounded.
- **Tests**: `src/Lumyn.Test` uses xUnit. Prefer tests for deterministic core logic such as subtitle parsing, settings persistence, resume/bookmarks, and format decisions. Avoid unit tests that require mpv, OpenGL, real Chromecast devices, or Avalonia windows unless those dependencies are isolated.
