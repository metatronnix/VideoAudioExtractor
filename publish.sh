#!/usr/bin/env bash
# Builds self-contained, single-file binaries for each target platform.
# Output: dist/<rid>/VideoAudioExtractor(.exe)
# Each binary embeds the .NET 8 runtime, so end users do NOT need the .NET SDK/runtime.
# Note: yt-dlp, ffmpeg/ffprobe, and Node are still required on the target machine.

set -euo pipefail
cd "$(dirname "$0")"

rids=(osx-arm64 osx-x64 win-x64)

for rid in "${rids[@]}"; do
    echo "Publishing $rid..."
    dotnet publish VideoAudioExtractor.csproj -c Release -r "$rid" --self-contained true \
        -p:PublishSingleFile=true \
        -p:EnableCompressionInSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -o "dist/$rid"
done

echo ""
echo "Done. Binaries are in dist/<rid>/"
