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

The release packages bundle the app runtime. The Ubuntu `.deb` uses the
distribution's `libmpv2` package so mpv and its media libraries match the host
Ubuntu release; the strictly confined snap stages its own matching mpv stack.

For development on Ubuntu, install the .NET SDK and libmpv development package.

```bash
sudo apt update
sudo apt install dotnet-sdk-10.0 libmpv-dev
```

## Download

Download the latest release from:

https://github.com/piyushdoorwar/lumyn-media-player/releases/latest

Ubuntu users can install the `.deb` package. Windows users can run the `.exe` installer.

## Run Locally

```bash
dotnet restore Lumyn.sln
dotnet build Lumyn.sln
dotnet run --project src/Lumyn.App/Lumyn.App.csproj
```

## License

Lumyn is source available, not open source under an OSI-approved license.
Personal, non-commercial use of official releases is permitted. You may view
the source code, build it for personal evaluation, and submit issues or pull
requests to the official repository.

Copying, redistribution, republishing, modification for distribution,
commercial use, resale, hosting as a service, and use in commercial products
or services are not permitted without explicit written permission from the
copyright holder.

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

Release package versions come from the pushed `v*` tag or the validated manual release-workflow input. Local packaging can set `VERSION` explicitly.
