# Builds self-contained, single-file binaries for each target platform.
# Output: dist/<rid>/VideoAudioExtractor(.exe)
# Each binary embeds the .NET 8 runtime, so end users do NOT need the .NET SDK/runtime.
# Note: yt-dlp, ffmpeg/ffprobe, and Node are still required on the target machine.

$ErrorActionPreference = 'Stop'
$proj = Join-Path $PSScriptRoot 'VideoAudioExtractor.csproj'
$rids = @('win-x64', 'osx-arm64', 'osx-x64')

foreach ($rid in $rids) {
    Write-Host "Publishing $rid..." -ForegroundColor Cyan
    dotnet publish $proj -c Release -r $rid --self-contained true `
        -p:PublishSingleFile=true `
        -p:EnableCompressionInSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -o (Join-Path $PSScriptRoot "dist/$rid")
    if ($LASTEXITCODE -ne 0) { throw "publish failed for $rid" }
}

Write-Host "`nDone. Binaries are in dist/<rid>/" -ForegroundColor Green
