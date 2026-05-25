using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;

// Ensures the external CLI tools the app shells out to (yt-dlp, ffmpeg, ffprobe)
// are available. Resolution order for each tool:
//   1. Already on PATH                         -> use it
//   2. Previously downloaded to the tools cache -> use it
//   3. Download it to the tools cache           -> use it
//
// This lets a self-contained build run with zero manual prerequisites: the .NET
// runtime is embedded in the binary, and these tools are fetched on first run.
public static class ToolProvisioner
{
    public record ToolPaths(string YtDlp, string Ffmpeg, string Ffprobe, string Deno);

    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromMinutes(10)
    };

    private static string ToolsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VideoAudioExtractor", "tools");

    private static string ExeName(string baseName) =>
        OperatingSystem.IsWindows() ? baseName + ".exe" : baseName;

    public static async Task<ToolPaths> EnsureAsync(IProgressReporter reporter)
    {
        Directory.CreateDirectory(ToolsDir);

        var ytDlp = await EnsureYtDlpAsync(reporter);
        var (ffmpeg, ffprobe) = await EnsureFfmpegAsync(reporter);
        var deno = await EnsureDenoAsync(reporter);

        return new ToolPaths(ytDlp, ffmpeg, ffprobe, deno);
    }

    // ───────────────────────────── Deno (yt-dlp JS runtime) ───────────
    // Current yt-dlp deprecated YouTube extraction without a JS runtime; Deno is
    // the runtime it enables by default. A single self-contained binary, so we can
    // fetch it the same way as the other tools.
    private static async Task<string> EnsureDenoAsync(IProgressReporter reporter)
    {
        var onPath = FindOnPath("deno");
        if (onPath != null)
        {
            reporter.ReportInfo($"deno found on PATH: {onPath}");
            return onPath;
        }

        var cached = Path.Combine(ToolsDir, ExeName("deno"));
        if (File.Exists(cached))
        {
            reporter.ReportInfo($"deno found in cache: {cached}");
            return cached;
        }

        var asset = DenoAsset();
        var url = $"https://github.com/denoland/deno/releases/latest/download/{asset}";

        reporter.ReportInfo($"Downloading deno from {url}");
        await DownloadAndExtractSingleAsync(url, "deno", cached);
        MakeExecutable(cached);
        reporter.ReportCompletion($"deno installed: {cached}");
        return cached;
    }

    private static string DenoAsset()
    {
        var arm64 = RuntimeInformation.OSArchitecture == Architecture.Arm64;
        if (OperatingSystem.IsWindows())
            return "deno-x86_64-pc-windows-msvc.zip";
        if (OperatingSystem.IsMacOS())
            return arm64 ? "deno-aarch64-apple-darwin.zip" : "deno-x86_64-apple-darwin.zip";
        return arm64 ? "deno-aarch64-unknown-linux-gnu.zip" : "deno-x86_64-unknown-linux-gnu.zip";
    }

    // ───────────────────────────── yt-dlp ─────────────────────────────
    private static async Task<string> EnsureYtDlpAsync(IProgressReporter reporter)
    {
        var onPath = FindOnPath("yt-dlp");
        if (onPath != null)
        {
            reporter.ReportInfo($"yt-dlp found on PATH: {onPath}");
            return onPath;
        }

        var cached = Path.Combine(ToolsDir, ExeName("yt-dlp"));
        if (File.Exists(cached))
        {
            reporter.ReportInfo($"yt-dlp found in cache: {cached}");
            return cached;
        }

        var asset =
            OperatingSystem.IsWindows() ? "yt-dlp.exe" :
            OperatingSystem.IsMacOS() ? "yt-dlp_macos" :
            "yt-dlp_linux";
        var url = $"https://github.com/yt-dlp/yt-dlp/releases/latest/download/{asset}";

        reporter.ReportInfo($"Downloading yt-dlp from {url}");
        await DownloadFileAsync(url, cached);
        MakeExecutable(cached);
        reporter.ReportCompletion($"yt-dlp installed: {cached}");
        return cached;
    }

    // ───────────────────────────── ffmpeg/ffprobe ─────────────────────
    private static async Task<(string ffmpeg, string ffprobe)> EnsureFfmpegAsync(IProgressReporter reporter)
    {
        var ffmpegOnPath = FindOnPath("ffmpeg");
        var ffprobeOnPath = FindOnPath("ffprobe");
        if (ffmpegOnPath != null && ffprobeOnPath != null)
        {
            reporter.ReportInfo($"ffmpeg/ffprobe found on PATH: {ffmpegOnPath}");
            return (ffmpegOnPath, ffprobeOnPath);
        }

        var cachedFfmpeg = Path.Combine(ToolsDir, ExeName("ffmpeg"));
        var cachedFfprobe = Path.Combine(ToolsDir, ExeName("ffprobe"));
        if (File.Exists(cachedFfmpeg) && File.Exists(cachedFfprobe))
        {
            reporter.ReportInfo($"ffmpeg/ffprobe found in cache: {ToolsDir}");
            return (cachedFfmpeg, cachedFfprobe);
        }

        // Resolve download URLs from the ffbinaries API.
        var platform = FfbinariesPlatform();
        reporter.ReportInfo($"Resolving ffmpeg build for platform '{platform}' via ffbinaries...");

        var json = await Http.GetStringAsync("https://ffbinaries.com/api/v1/version/latest");
        using var doc = JsonDocument.Parse(json);
        var bin = doc.RootElement.GetProperty("bin").GetProperty(platform);
        var ffmpegZip = bin.GetProperty("ffmpeg").GetString()!;
        var ffprobeZip = bin.GetProperty("ffprobe").GetString()!;

        if (OperatingSystem.IsMacOS() && RuntimeInformation.OSArchitecture == Architecture.Arm64)
            reporter.ReportInfo("Note: ffbinaries provides an x86_64 macOS build; it runs on Apple Silicon via Rosetta 2.");

        reporter.ReportInfo("Downloading ffmpeg...");
        await DownloadAndExtractSingleAsync(ffmpegZip, "ffmpeg", cachedFfmpeg);
        MakeExecutable(cachedFfmpeg);

        reporter.ReportInfo("Downloading ffprobe...");
        await DownloadAndExtractSingleAsync(ffprobeZip, "ffprobe", cachedFfprobe);
        MakeExecutable(cachedFfprobe);

        reporter.ReportCompletion($"ffmpeg/ffprobe installed: {ToolsDir}");
        return (cachedFfmpeg, cachedFfprobe);
    }

    private static string FfbinariesPlatform()
    {
        if (OperatingSystem.IsWindows()) return "windows-64";
        if (OperatingSystem.IsMacOS()) return "osx-64";
        return RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "linux-arm-64" : "linux-64";
    }

    // ───────────────────────────── helpers ────────────────────────────
    private static string? FindOnPath(string baseName)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVar)) return null;

        var names = OperatingSystem.IsWindows()
            ? new[] { baseName + ".exe", baseName }
            : new[] { baseName };

        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                foreach (var name in names)
                {
                    var full = Path.Combine(dir, name);
                    if (File.Exists(full)) return full;
                }
            }
            catch
            {
                // Ignore malformed PATH entries.
            }
        }
        return null;
    }

    private static async Task DownloadFileAsync(string url, string destPath)
    {
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        await using var src = await resp.Content.ReadAsStreamAsync();
        await using var dst = File.Create(destPath);
        await src.CopyToAsync(dst);
    }

    // Downloads a zip and extracts the single entry whose name contains toolName.
    private static async Task DownloadAndExtractSingleAsync(string zipUrl, string toolName, string destPath)
    {
        var tmpZip = Path.Combine(Path.GetTempPath(), $"vae-{toolName}-{Guid.NewGuid():N}.zip");
        try
        {
            await DownloadFileAsync(zipUrl, tmpZip);
            using var archive = ZipFile.OpenRead(tmpZip);
            var entry = archive.Entries.FirstOrDefault(e =>
                            !string.IsNullOrEmpty(e.Name) &&
                            e.Name.Contains(toolName, StringComparison.OrdinalIgnoreCase))
                        ?? archive.Entries.FirstOrDefault(e => !string.IsNullOrEmpty(e.Name));

            if (entry == null)
                throw new InvalidOperationException($"No extractable entry found in {zipUrl}");

            if (File.Exists(destPath)) File.Delete(destPath);
            entry.ExtractToFile(destPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmpZip)) File.Delete(tmpZip);
        }
    }

    private static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        File.SetUnixFileMode(path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }
}
