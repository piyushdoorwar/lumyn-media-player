# Lumyn

Lumyn is a small desktop media player built with .NET 10, C#, Avalonia UI, and mpv. Avalonia owns the interface, while libmpv handles playback and decoding.

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
    windows/
  scripts/
    build-linux.sh
    build-windows.ps1
```

## Dependencies

- .NET 10 SDK
- libmpv on Linux for local development and Linux package creation

On Ubuntu:

```bash
sudo apt update
sudo apt install dotnet-sdk-10.0 libmpv-dev
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

Create a bundled `.deb`:

```bash
chmod +x scripts/build-linux.sh
./scripts/build-linux.sh
```

Output:

```text
artifacts/packages/lumyn_0.1.4_amd64.deb
```

## Build A Windows Package

Create a portable Windows zip:

```powershell
./scripts/build-windows.ps1
```

The script publishes `win-x64`, downloads the latest mpv Windows runtime if `MPV_BIN_DIR` is not set, copies the mpv DLLs beside `Lumyn.exe`, and writes:

```text
artifacts/packages/lumyn_0.1.4_win-x64.zip
```

## Known Issues

- The `.deb` script bundles the self-contained .NET publish output, libmpv, and discovered native shared-library dependencies.
- The package targets `amd64` by default. Set `RID=linux-arm64` to build an `arm64` package.
- The Windows script currently targets `win-x64` and produces a portable zip, not an installer.
- The MVP has no playlist, media library, subtitle download, URL streaming, accounts, themes, or advanced settings.
