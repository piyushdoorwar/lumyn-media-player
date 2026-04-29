# Lumyn

Lumyn is a small Linux-first desktop media player built with .NET 10, C#, Avalonia UI, and LibVLCSharp. Avalonia owns the interface, while VLC/libvlc handles playback and decoding.

## Features

- Open local media files with a file picker.
- Drag and drop a media file onto the window.
- Play, pause, seek, volume, mute, and fullscreen controls.
- Keyboard shortcuts: Space, Left/Right, Up/Down, F, M, O, and Esc.
- Dark UI with a centered video area and bottom control bar.
- Controls auto-hide during playback or fullscreen.
- Resume playback per file using a local JSON settings file.
- Current file name appears in the title bar.

## Structure

```text
Lumyn/
  Lumyn.sln
  src/
    Lumyn.App/
      Assets/
      Controls/
      Program.cs
      ViewModels/
      Views/
    Lumyn.Core/
      Models/
      Services/
  packaging/
    linux/
  scripts/
    build-linux.sh
```

## Dependencies

- Linux
- .NET 10 SDK
- VLC/libvlc system packages

On Ubuntu:

```bash
sudo apt update
sudo apt install dotnet-sdk-10.0 vlc libvlc-dev
```

## Run Locally

```bash
dotnet restore Lumyn.sln
dotnet build Lumyn.sln
dotnet run --project src/Lumyn.App/Lumyn.App.csproj
```

## Build A Linux Package

Publish self-contained:

```bash
dotnet publish src/Lumyn.App/Lumyn.App.csproj -c Release -r linux-x64 --self-contained true -o artifacts/publish/linux-x64
```

Create a simple `.deb`:

```bash
chmod +x scripts/build-linux.sh
./scripts/build-linux.sh
```

Output:

```text
artifacts/packages/lumyn_0.1.0_amd64.deb
```

## Known Issues

- The `.deb` script is intentionally simple and targets `amd64`.
- VLC/libvlc must be available on the Linux system.
- The MVP has no playlist, media library, subtitle download, URL streaming, accounts, themes, or advanced settings.
