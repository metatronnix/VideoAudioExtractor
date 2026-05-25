# VideoAudioExtractor

A .NET 8 console app that downloads audio from YouTube URLs (listed in an Excel
file) via `yt-dlp`, converts each track to FLAC with `ffmpeg`, and tags the files
from their metadata.

Runs on **Windows** and **macOS/Linux**.

## Prerequisites

The app shells out to external tools — `yt-dlp`, `ffmpeg`/`ffprobe`, and `deno`
(a JavaScript runtime that current `yt-dlp` requires for YouTube extraction). **You do
not have to install them manually:** on first run the app resolves each tool in this
order and downloads it if needed:

1. Found on your `PATH` → used as-is
2. Previously downloaded to the tools cache → reused
3. Otherwise downloaded automatically (`yt-dlp` and `deno` from GitHub releases,
   `ffmpeg`/`ffprobe` from [ffbinaries](https://ffbinaries.com/)) into
   `%LOCALAPPDATA%\VideoAudioExtractor\tools` (Windows) or
   `~/.local/share/VideoAudioExtractor/tools` (macOS/Linux)

> Node.js is **not** required — Deno is used as the JS runtime instead.

So the only hard requirement depends on how you run the app:

| How you run it | What you need |
|----------------|---------------|
| From source (`dotnet run`) | [.NET 8 SDK](https://dotnet.microsoft.com/download) |
| A [self-contained build](#distributable-self-contained-builds) | nothing — the .NET runtime is embedded and the tools auto-download |

## Getting started on macOS

These steps assume you're running from source. The only thing you must install is
the **.NET 8 SDK**.

### 1. Install Homebrew (if you don't have it)

macOS does not ship with Homebrew. Check first:

```bash
brew --version
```

If that says "command not found," install it:

```bash
/bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"
```

- **Intel Macs:** Homebrew installs to `/usr/local` and is normally on your `PATH`
  immediately — just run `brew --version` again to confirm.
- **Apple Silicon (M-series):** Homebrew installs to `/opt/homebrew`, which is **not**
  on `PATH` until you add it. Run the two commands the installer prints under
  "Next steps" (for the default zsh shell):

  ```bash
  echo 'eval "$(/opt/homebrew/bin/brew shellenv)"' >> ~/.zprofile
  eval "$(/opt/homebrew/bin/brew shellenv)"
  ```

> **Prefer not to use Homebrew?** Skip straight to the `.pkg` installer in step 2.

### 2. Install the .NET 8 SDK

```bash
brew install --cask dotnet-sdk
dotnet --version
```

Or **without Homebrew**: download the **.NET 8 SDK** installer (`.pkg`) from
<https://dotnet.microsoft.com/download> — choose **x64** for Intel Macs or **Arm64**
for Apple Silicon — and run it.

### 3. Clone and run

```bash
git clone https://github.com/metatronnix/VideoAudioExtractor.git
cd VideoAudioExtractor
dotnet run --project VideoAudioExtractor.csproj
```

On first run the app auto-downloads `yt-dlp`, `ffmpeg`/`ffprobe`, and `deno`. It then
looks for `~/Audio/youtube_videos.xlsx` (URLs in column A, sheet `Sheet1`) — create
that file and you're ready. See [Configuration](#configuration) to use a different
folder and [YouTube cookies](#youtube-cookies-optional) to sign in with your account.

### Optional: install the tools yourself (recommended)

Auto-download is a convenience fallback. Installing the tools via your package
manager is still recommended — you get native builds (e.g. arm64 `ffmpeg` on Apple
Silicon instead of the x86_64 ffbinaries build under Rosetta) and faster startup.

```bash
# macOS (Homebrew)
brew install yt-dlp ffmpeg deno
```

```powershell
# Windows
winget install yt-dlp.yt-dlp
winget install Gyan.FFmpeg
winget install DenoLand.Deno
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
- `youtube_cookies.txt` — *optional*; your own YouTube cookies (see
  [YouTube cookies](#youtube-cookies-optional)). `--cookies` is passed only if this
  file exists.

Output FLACs are written to a dated subfolder, e.g. `<audio>/2026.05.25/`, and named
`Artist - Title.flac`.

## YouTube cookies (optional)

Cookies let `yt-dlp` use **your own YouTube login**, which helps with
age-restricted/members-only videos and avoids "Sign in to confirm you're not a bot"
errors. Many public videos download fine without it, so this step is optional.

The file is **per-user** — each person supplies their own. It is **not** included in
the repo (and is git-ignored), so create your own at
`<audio>/youtube_cookies.txt` (by default `~/Audio/youtube_cookies.txt` on macOS/Linux
or `C:\Audio\youtube_cookies.txt` on Windows).

> ⚠️ This file contains your active YouTube/Google session — treat it like a password.
> Never commit it or share it.

**Option A — export with yt-dlp (recommended).** Log into YouTube in your browser,
then run once (Firefox is the most reliable source; `chrome`, `edge`, `brave`, and
`safari` also work):

```bash
# macOS/Linux
yt-dlp --cookies-from-browser firefox \
       --cookies ~/Audio/youtube_cookies.txt \
       --skip-download "https://www.youtube.com"
```

```powershell
# Windows (PowerShell)
yt-dlp --cookies-from-browser firefox `
       --cookies C:\Audio\youtube_cookies.txt `
       --skip-download "https://www.youtube.com"
```

This writes a Netscape-format cookies file to that path. (If you haven't installed
`yt-dlp` yourself, run the app once first so it auto-downloads, or `brew install yt-dlp`.)

> macOS notes: reading **Safari** cookies requires giving your terminal *Full Disk
> Access* in System Settings → Privacy & Security; **Chrome** may prompt for Keychain
> access. **Firefox** generally works without extra permissions.

**Option B — browser extension.** Install a cookies.txt exporter (e.g. "Get
cookies.txt LOCALLY" for Chrome/Firefox), open YouTube while logged in, export, and
save the result as `youtube_cookies.txt` in your audio folder.

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
> and `yt-dlp`, `ffmpeg`/`ffprobe`, and `deno` are auto-downloaded on first run if
> they aren't already on the user's `PATH` (see [Prerequisites](#prerequisites)). The
> first run therefore needs network access and will take a little longer while tools
> download.

### macOS Gatekeeper

A binary downloaded from the internet is quarantined by macOS. Unless the app is
code-signed and notarized (requires an Apple Developer account), users must clear
the quarantine flag once:

```bash
xattr -d com.apple.quarantine ./VideoAudioExtractor
chmod +x ./VideoAudioExtractor
```
