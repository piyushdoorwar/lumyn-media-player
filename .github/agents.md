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

**Lumyn** is a clean, minimal desktop media player built on .NET 10 + Avalonia UI + mpv. It targets Windows x64, Ubuntu Linux amd64/arm64, and macOS (Apple Silicon + Intel).

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
| Packaging | dpkg (Linux .deb), Inno Setup (Windows .exe), native .app/.dmg (macOS) |
| Build System | .NET CLI (`dotnet build / publish`) |
| Package Manager | NuGet (centralized via `Directory.Packages.props`) |
| CI/CD | GitHub Actions |
| Deployment | GitHub Releases + GitHub Pages |

---

## 3. Solution Structure

```
lumyn-media-player/
├── Lumyn.sln                        # Visual Studio solution (2 projects)
├── Directory.Build.props            # Global build config (net10.0, nullable, etc.)
├── Directory.Packages.props         # Central NuGet version management
├── VERSION                          # Base version string, currently "1.0"
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
│   │   │   ├── VolumeSlider.cs      # Volume slider control
│   │   │   └── AudioBars.cs         # Audio visualization bars
│   │   ├── Models/
│   │   │   ├── VideoAdjustments.cs  # Record for brightness/contrast/saturation/rotation/zoom/aspect
│   │   │   └── SubtitleSettings.cs  # Subtitle appearance config
│   │   ├── ViewModels/
│   │   │   └── MainViewModel.cs     # MVVM command routing, UI state, playlist, subtitle overlay
│   │   └── Views/
│   │       ├── MainWindow.axaml / .axaml.cs   # Main player window (739 lines)
│   │       ├── JumpToTimeDialog.axaml / .axaml.cs
│   │       ├── KeyboardShortcutsDialog.axaml / .axaml.cs
│   │       ├── AboutDialog.axaml / .axaml.cs
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
├── scripts/
│   ├── build-linux.sh               # Linux .deb packaging (266 lines)
│   ├── build-windows.ps1            # Windows installer via Inno Setup (363 lines, PowerShell)
│   ├── build-macos.sh               # macOS .app + .dmg packaging
│   └── build-linux-flatpak.sh       # Flatpak packaging (alternate format)
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
- Screenshot capture to file
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

### Navigation & Persistence
- Recent files list (last 12 files)
- Resume playback position per file (SHA256-hashed path for privacy)
- Bookmarks / chapter markers with custom labels per file
- Jump-to-time dialog
- Configurable seek step: 5 / 10 / 30 seconds
- Volume and speed persisted across sessions
- Subtitle settings persisted per file

### Casting
- Chromecast (Google Cast v2) device discovery via mDNS (`_googlecast._tcp`)
- Cast to Chromecast devices
- Built-in HTTP server to serve local files to cast devices (byte-range support for seeking)
- SRT/VTT subtitle files converted to WebVTT and passed as `tracks[]` in Cast load request
- Playback control (play, pause, seek, volume) over Google Cast protocol
- **Supported formats**: MP4 (`video/mp4`), WebM (`video/webm`), and audio formats (MP3/AAC/FLAC/Opus/WAV)
- **Unsupported formats**: MKV, AVI, WMV, MOV, FLV, TS, M2TS — Chromecast cannot natively play these containers. The Cast dialog shows a clear "not supported" message and disables the Cast button when such a file is loaded. No transcoding is performed.

### Screen Sleep Inhibition
- `ScreenInhibitor` service (`Lumyn.Core/Services/ScreenInhibitor.cs`) prevents screen dim, lock, and system sleep while media is playing
- Driven automatically by `IsPlaying` property setter in `MainViewModel`
- Windows: `SetThreadExecutionState(ES_CONTINUOUS | ES_DISPLAY_REQUIRED | ES_SYSTEM_REQUIRED)` via kernel32 P/Invoke
- macOS: `IOPMAssertionCreateWithName("PreventUserIdleDisplaySleep")` via IOKit P/Invoke
- Linux: `org.freedesktop.ScreenSaver.Inhibit` on D-Bus session bus via `dbus-send` subprocess (cookie-based; works on GNOME, KDE, and freedesktop-compliant desktops)
- Inhibition released on pause, stop, end-of-file, or app close

### UI & Platform
- Clean dark theme (#111111 bg, #DEDAD5 text)
- Always-on-top toggle
- Drag-to-move via title bar
- Keyboard shortcuts (full list in KeyboardShortcutsDialog)
- OSD messages
- Sidebar playlist panel (toggle with Q)
- Ubuntu GNOME "Open With" integration (`.desktop` entry + MIME types)
- Windows file association registration script
- Single-instance, command-line file argument support

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

**Library name resolution** handles DLL (Windows), `.so.2` (Linux), `.dylib` (macOS) variants at runtime.

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

- **Location**: `~/.config/Lumyn/settings.json` (Linux); equivalent on Windows/macOS
- **Format**: JSON, auto-saved on changes
- **Contents**:
  - Resume positions (key = SHA256 of normalized file path)
  - Bookmarks per file
  - Subtitle settings per file (font, size, color, delay)
  - Recent files list (max 12)
  - Global: volume, speed, seek step preference

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
       [Playlist sidebar — toggle with Q]
```

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
| `BookmarksDialog` | Manage chapter bookmarks per file |

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

### Windows — `.exe` installer (`scripts/build-windows.ps1`, 363 lines)

1. `dotnet publish -c Release -r win-x64 --self-contained true`
2. Resolve mpv:
   - Auto-download from `shinchiro/mpv-winbuild-cmake` GitHub releases
   - Extract via 7z/7za
   - Or use manually provided `MPV_BIN_DIR`
3. Copy `mpv-2.dll` + dependencies
4. Generate `Register-FileAssociations.ps1` for post-install user run
5. Compile Inno Setup `.iss` → `lumyn_X.X.X_win-x64_setup.exe`

**Dependencies needed on build machine**: Inno Setup, 7-Zip

### macOS — `.dmg` bundle (`scripts/build-macos.sh`)

1. `dotnet publish -c Release -r osx-arm64 (or osx-x64) --self-contained true`
2. `brew install mpv ffmpeg` — ffmpeg provides libav* dylibs that mpv links against
3. Bundle `libmpv.dylib` + libav* dylibs (via `copy_dylib_closure`) into `.app/Contents/MacOS/`; fix `@loader_path` rpath
4. The ffmpeg **binary** is NOT bundled — only its shared libraries are needed
5. Create `.dmg` disk image → `Lumyn.dmg`

**Flatpak**: `scripts/build-linux-flatpak.sh` — alternate Linux packaging (not in primary CI)

---

## 11. CI/CD Workflows

### `build-artifacts.yml` — triggered on push to `main` or manual dispatch

| Job | Runner | Output artifact |
|---|---|---|
| `linux-deb` | ubuntu-latest | `lumyn-linux-amd64-deb` (*.deb) |
| `windows-installer` | windows-latest | `lumyn-windows-x64-installer` (*_setup.exe) |
| `macos-arm64` | macos-15 | `lumyn-macos-osx-arm64` (*.dmg) |
| `macos-x64` | macos-15-intel | `lumyn-macos-osx-x64` (*.dmg) |

All jobs install .NET 10.0 SDK.

### `release.yml` — triggered on `v*` tag push or manual dispatch

- Runs same build jobs as `build-artifacts.yml`
- Additional `github-release` job: attaches all artifacts to a GitHub Release

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
  3. Each build script passes it to `dotnet publish` via `-p:Version=... -p:InformationalVersion=...`
  4. The version is baked into the assembly `AssemblyInformationalVersionAttribute`
  5. `AboutDialog.axaml.cs` reads it from the attribute at runtime
- **Local dev builds**: Show `0.0.0-dev` — set as default `<InformationalVersion>` in `Lumyn.App.csproj`
- **CI builds** (`build-artifacts.yml` on push to main): Use `VERSION=0.0.0-dev` — for build validation only, not releases
- **Manual trigger**: `release.yml` supports `workflow_dispatch` with an explicit version input for testing
- **VERSION file**: Deleted — no longer needed
- **Artifact names**: e.g., `lumyn_1.2.3_amd64.deb`, `lumyn_1.2.3_win-x64_setup.exe`, `lumyn_1.2.3_macos-arm64.dmg`

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

### Releases page (`/site/releases/`)

- `index.html` — static markup; OS filter tabs + stable-only toggle
- `releases.js` — fetches all non-draft releases from GitHub API, renders paginated list

**Filters:**
- **OS tabs** — All / Linux / Windows / macOS (filters by asset type)
- **Stable only toggle** — pill toggle, checked by default; hides pre-releases when on; unchecking reveals pre-releases (shown with `badge-pre` badge)

**JS state variables in `releases.js`:**
- `currentOS` — active OS tab value (`"all"`, `"linux"`, `"windows"`, `"macos"`)
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
```

### Windows

- Install .NET SDK 10.0
- Download `mpv-2.dll` from mpv builds and place in project output or set `MPV_BIN_DIR`
- Run via VS / `dotnet run`

### macOS

```bash
brew install dotnet mpv
dotnet run --project src/Lumyn.App/Lumyn.App.csproj
```

---

## 15. Conventions & Patterns

- **MVVM**: Views bind to `MainViewModel` via `DataContext`. No code-behind logic beyond Avalonia event wiring.
- **RelayCommand**: Standard `ICommand` wrapper used throughout `MainViewModel`.
- **Thread safety**: All UI updates via `Dispatcher.UIThread.InvokeAsync`. mpv event loop runs on dedicated background thread `"Lumyn mpv events"`. `MediaState` guarded by lock.
- **Nullable**: Enabled globally. All fields/properties use nullable annotations.
- **No mocks in architecture**: Services are concrete classes, injected via constructor. No DI container — manual injection in `App.axaml.cs`.
- **File path hashing**: SHA256 used for all per-file storage keys to avoid storing raw paths in settings JSON.
- **Unsafe code**: Allowed in both projects for mpv P/Invoke and OpenGL interop.
- **Platform detection**: Runtime OS check for library name variants (`.dll` / `.so.2` / `.dylib`).
- **Settings path**: `Environment.GetFolderPath(SpecialFolder.ApplicationData)` + `Lumyn/settings.json`.
- **Avalonia resources**: Icons and styles defined in `App.axaml` as `Application.Resources`. Referenced in XAML as `StaticResource`.
- **Custom controls**: Placed in `Lumyn.App/Controls/`. Inherit from Avalonia primitives (e.g., `Control`, `Slider`).

---

## Changelog (Feature Updates)

> Update this section whenever a feature is added, removed, or significantly changed.

| Date | Change |
|---|---|
| 2026-05 | Git tag–based versioning — `VERSION` file removed, version now sourced from git tag (`v1.2.3`), baked into assembly via `-p:InformationalVersion`, read from `AssemblyInformationalVersionAttribute` at runtime. Pre-release tags (containing `-`) auto-marked on GitHub. Local dev shows `0.0.0-dev`. |
| 2026-05 | Screen sleep/lock inhibition while playing — `ScreenInhibitor` service added to `Lumyn.Core/Services/`. Windows: `SetThreadExecutionState`, macOS: `IOPMAssertion`, Linux: `org.freedesktop.ScreenSaver.Inhibit` via `dbus-send` (uses `ArgumentList` to avoid argv-splitting issues with typed values). Driven by `IsPlaying` setter in `MainViewModel`. |
| 2026-05 | Full-screen / maximize conflict fix (`386f746`) |
| 2026-05 | Top bar visibility fix in non-fullscreen mode (`e214ef9`) |
| 2026-05 | Video rendering optimization (`6d349ee`) |
| 2026-05 | Fix unnecessary wakeups/render pressure over time (`4f41a0f`) |
| 2026-05 | Media controls via DLNA cast (`a750c3b`) |
| 2026-05 | Switch cast from DLNA/UPnP to Google Cast (Chromecast) — `DlnaCastService` replaced by `ChromecastCastService` using `GoogleCast` NuGet (mDNS discovery, Cast v2 protocol, SRT→WebVTT subtitle tracks). Format support is best-effort (no transcoding). |
| 2026-05 | Casting: removed experimental remux-to-temp-file approach (too slow/unreliable). Unsupported formats (MKV, AVI, etc.) now show a clear "not supported" message in the Cast dialog and disable the Cast button. MP4/WebM + subtitle casting fully working. |
| 2026-05 | Packaging: removed `Depends: ffmpeg` from Linux `.deb` control (app doesn't invoke the ffmpeg binary at runtime). Removed ffmpeg binary bundling from macOS `.app` (only libav* dylibs needed by mpv are kept). |
