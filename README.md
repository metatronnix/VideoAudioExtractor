# VideoAudioExtractor

A .NET 8 console app that downloads audio from YouTube URLs (listed in an Excel
file) via `yt-dlp`, converts each track to FLAC with `ffmpeg`, and tags the files
from their metadata.

Runs on **Windows** and **macOS/Linux**.

## Prerequisites

The app shells out to two external tools — `yt-dlp` and `ffmpeg`/`ffprobe`. **You do
not have to install them manually:** on first run the app resolves each tool in this
order and downloads it if needed:

1. Found on your `PATH` → used as-is
2. Previously downloaded to the tools cache → reused
3. Otherwise downloaded automatically (`yt-dlp` from GitHub releases, `ffmpeg`/
   `ffprobe` from [ffbinaries](https://ffbinaries.com/)) into
   `%LOCALAPPDATA%\VideoAudioExtractor\tools` (Windows) or
   `~/.local/share/VideoAudioExtractor/tools` (macOS/Linux)

> Node.js is **not** required.

So the only hard requirement depends on how you run the app:

| How you run it | What you need |
|----------------|---------------|
| From source (`dotnet run`) | [.NET 8 SDK](https://dotnet.microsoft.com/download) |
| A [self-contained build](#distributable-self-contained-builds) | nothing — the .NET runtime is embedded and the tools auto-download |

### Optional: install the tools yourself (recommended)

Auto-download is a convenience fallback. Installing the tools via your package
manager is still recommended — you get native builds (e.g. arm64 `ffmpeg` on Apple
Silicon instead of the x86_64 ffbinaries build under Rosetta) and faster startup.

```bash
# macOS (Homebrew)
brew install yt-dlp ffmpeg
```

```powershell
# Windows
winget install yt-dlp.yt-dlp
winget install Gyan.FFmpeg
```

If `ffmpeg` is installed somewhere not on your `PATH`, point the app at it with
`VAE_FFMPEG_DIR` (see Configuration); on Windows it also falls back to `C:\ffmpeg\bin`
if that folder exists.

## Configuration

Paths are cross-platform and can be overridden with environment variables:

| Variable | Default | Purpose |
|----------|---------|---------|
| `VAE_AUDIO_DIR` | `~/Audio` (macOS/Linux), `C:\Audio` (Windows) | Base folder for input and output |
| `VAE_FFMPEG_DIR` | (unset → use `PATH`; Windows falls back to `C:\ffmpeg\bin`) | Explicit ffmpeg/ffprobe directory |

Inside the audio base directory the app expects:

- `youtube_videos.xlsx` — URLs to download (column A, sheet `Sheet1`)
- `youtube_cookies.txt` — *optional*; `--cookies` is passed only if this file exists

Output FLACs are written to a dated subfolder, e.g. `<audio>/2026.05.25/`, and named
`Artist - Title.flac`.

## Build & run

```bash
dotnet build VideoAudioExtractor.csproj -c Debug
dotnet run --project VideoAudioExtractor.csproj
```

To point at a custom audio folder for one run:

```bash
# macOS/Linux
VAE_AUDIO_DIR="$HOME/Music/yt" dotnet run --project VideoAudioExtractor.csproj
```

```powershell
# Windows (PowerShell)
$env:VAE_AUDIO_DIR = "D:\yt"; dotnet run --project VideoAudioExtractor.csproj
```

## Distributable (self-contained) builds

To produce standalone, single-file binaries that **embed the .NET 8 runtime** — so
end users do **not** need the .NET SDK or runtime installed — run:

```bash
# macOS / Linux
./publish.sh
```

```powershell
# Windows
.\publish.ps1
```

This writes one single-file executable per platform to `dist/<rid>/`:

| Platform | Output |
|----------|--------|
| `dist/win-x64/`   | `VideoAudioExtractor.exe` (Windows x64) |
| `dist/osx-arm64/` | `VideoAudioExtractor` (Apple Silicon Macs) |
| `dist/osx-x64/`   | `VideoAudioExtractor` (Intel Macs) |

Each binary is ~35 MB. The publish scripts cross-compile, so you can build all
targets from any one OS.

> **Zero manual prerequisites:** the self-contained build embeds the .NET runtime,
> and `yt-dlp` + `ffmpeg`/`ffprobe` are auto-downloaded on first run if they aren't
> already on the user's `PATH` (see [Prerequisites](#prerequisites)). The first run
> therefore needs network access and will take a little longer while tools download.

### macOS Gatekeeper

A binary downloaded from the internet is quarantined by macOS. Unless the app is
code-signed and notarized (requires an Apple Developer account), users must clear
the quarantine flag once:

```bash
xattr -d com.apple.quarantine ./VideoAudioExtractor
chmod +x ./VideoAudioExtractor
```
