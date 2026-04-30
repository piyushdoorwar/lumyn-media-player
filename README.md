# Lumyn

Lumyn is a clean desktop media player for local audio and video files. It keeps the interface quiet, gives you fast playback controls, and includes practical subtitle support.

Website: https://piyushdoorwar.github.io/lumyn-media-player/

## Features

- Open local audio and video files.
- Drag and drop media into the player.
- Play, pause, seek, mute, fullscreen, loop, and adjust speed.
- Keyboard shortcuts for common playback actions.
- Subtitle file loading, embedded subtitle track selection, appearance settings, and sync delay.
- Resume playback position per file.
- Screenshots for video playback.
- Ubuntu "Open With" integration for supported audio and video formats.

## Tech Stack

- .NET 10
- C#
- Avalonia UI
- mpv / libmpv for playback

## Supported OS

- Ubuntu Linux, amd64
- Windows, x64

The Ubuntu package is the main supported build right now. It is intended for recent Ubuntu desktop releases and works best on a modern GNOME desktop session. Wayland is fine; X11 should also work.

The release packages bundle the app runtime and mpv pieces needed for playback. For development on Ubuntu, install the .NET SDK and libmpv development package.

```bash
sudo apt update
sudo apt install dotnet-sdk-10.0 libmpv-dev
```

## Download

Download the latest release from:

https://github.com/piyushdoorwar/lumyn-media-player/releases/latest

Ubuntu users can install the `.deb` package. Windows users can download and extract the portable `.zip`, then run `Lumyn.exe`.

## Run Locally

```bash
dotnet restore Lumyn.sln
dotnet build Lumyn.sln
dotnet run --project src/Lumyn.App/Lumyn.App.csproj
```

## Build Packages

Build the Ubuntu `.deb`:

```bash
./scripts/build-linux.sh
```

Build the Windows portable `.zip` from Windows PowerShell:

```powershell
./scripts/build-windows.ps1
```

GitHub Actions also builds release artifacts through the `Build release artifacts` workflow.

Package versions use the base version in `VERSION` plus the GitHub Actions run number, for example `0.1.42`. Set `VERSION` in the environment to override the full package version manually.
