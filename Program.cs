using FFMpegCore.Builders.MetaData;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using VideoAudioExtractor.Implementations;
using static System.Net.WebRequestMethods;

var progressReporter = new EnhancedConsoleProgressReporter();

// Ensure yt-dlp + ffmpeg/ffprobe are available (PATH -> cache -> download).
var tools = await ToolProvisioner.EnsureAsync(progressReporter);

// Cross-platform audio base directory.
// Override with the VAE_AUDIO_DIR environment variable; otherwise default to
// C:\Audio on Windows or ~/Audio elsewhere (macOS/Linux).
string audioBase = Environment.GetEnvironmentVariable("VAE_AUDIO_DIR") is { Length: > 0 } customDir
    ? customDir
    : OperatingSystem.IsWindows()
        ? @"C:\Audio"
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Audio");

// Optional explicit ffmpeg directory. If unset, ffmpeg/ffprobe are taken from PATH
// (the norm on macOS/Linux via Homebrew). On Windows, fall back to C:\ffmpeg\bin if present.
string? ffmpegDir = Environment.GetEnvironmentVariable("VAE_FFMPEG_DIR");
if (string.IsNullOrWhiteSpace(ffmpegDir) && OperatingSystem.IsWindows() && Directory.Exists(@"C:\ffmpeg\bin"))
    ffmpegDir = @"C:\ffmpeg\bin";

string directoryPath = Path.Combine(audioBase, DateTime.Today.ToString("yyyy.MM.dd"));

if (!Directory.Exists(directoryPath))
    Directory.CreateDirectory(directoryPath);
// Your personal YouTube video collection
var config = new ExtractorConfig
{
    DefaultOutputDirectory = directoryPath,
    CookiesFile = Path.Combine(audioBase, "youtube_cookies.txt"),
    FfmpegLocation = ffmpegDir,
    YtDlpPath = tools.YtDlp,
    FfmpegPath = tools.Ffmpeg,
    FfprobePath = tools.Ffprobe,
    MinRequestInterval = TimeSpan.FromMinutes(1),
    LongBreakChance = 0.4,
    MaxRetries = 3
};

using var extractor = new YouTubeAudioExtractor(config, progressReporter);
var youtubeVideos = extractor.LoadYouTubeUrlsFromExcel(Path.Combine(audioBase, "youtube_videos.xlsx"), "Sheet1");


Console.WriteLine("🎵 YouTube Audio Extractor v2.1 - FLAC Edition");
Console.WriteLine("===============================================\n");

Console.WriteLine("\n📦 Starting FLAC extraction:");

var overallStopwatch = Stopwatch.StartNew();

// Process with async enumerable
await foreach (var result in extractor.ExtractBatchAsync(
    youtubeVideos,
    config.DefaultOutputDirectory,
    AudioFormat.FLAC))
{
    if (result.Success)
    {
        var fileSizeMB = result.FileSizeBytes.HasValue ?
            $" ({result.FileSizeBytes.Value / (1024.0 * 1024.0):F1} MB)" : "";

        var metadataInfo = result.Metadata != null
            ? $" | Artist: {result.Metadata.Artist ?? "Unknown"} | Duration: {result.Metadata.Duration?.ToString(@"mm\:ss") ?? "Unknown"}"
            : "";

        progressReporter.ReportCompletion(
            $"✅ {result.Name}{fileSizeMB}{metadataInfo}");
    }
    else
    {
        // Make sure error is visible by stopping progress updates and using multiple methods
        if (progressReporter is EnhancedConsoleProgressReporter enhancedReporter)
        {
            enhancedReporter.StopProgressUpdates();
            enhancedReporter.ClearProgressLine();
        }

        // Force the error to be visible
        Console.WriteLine(); // Add blank line
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"❌ EXTRACTION FAILED: {result.Name}");
        Console.WriteLine($"   Error: {result.Error ?? "Unknown error"}");
        Console.ResetColor();
        Console.WriteLine(); // Add blank line
        Console.Out.Flush();

        // Also use the progress reporter
        progressReporter.ReportError($"❌ {result.Name} failed: {result.Error ?? "Unknown error"}");
    }
}

overallStopwatch.Stop();
Console.WriteLine($"\n🎉 All FLAC extractions completed in {overallStopwatch.Elapsed:hh\\:mm\\:ss}!");

// Configuration class
public class ExtractorConfig
{
    public TimeSpan MinRequestInterval { get; set; } = TimeSpan.FromMinutes(2);
    public TimeSpan SessionBreakMin { get; set; } = TimeSpan.FromMinutes(2);
    public TimeSpan SessionBreakMax { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan LongBreakMin { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan LongBreakMax { get; set; } = TimeSpan.FromMinutes(30);
    public double LongBreakChance { get; set; } = 0.25;
    public int MaxFileNameLength { get; set; } = 300;
    public string DefaultOutputDirectory { get; set; } = OperatingSystem.IsWindows()
        ? @"C:\Audio"
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Audio");
    // Path to a yt-dlp cookies file; --cookies is only passed if the file exists.
    public string? CookiesFile { get; set; }
    // Explicit directory containing ffmpeg/ffprobe (env override). If null, the
    // directory is derived from FfmpegPath, and otherwise tools are taken from PATH.
    public string? FfmpegLocation { get; set; }
    // Resolved full paths to the external tools (set by ToolProvisioner).
    public string? YtDlpPath { get; set; }
    public string? FfmpegPath { get; set; }
    public string? FfprobePath { get; set; }
    public int MaxRetries { get; set; } = 3;
    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromMinutes(10);
}

// Extraction result class
public class ExtractionResult
{
    public string Url { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Success { get; set; }
    public string? OutputPath { get; set; }
    public string? Error { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration => EndTime - StartTime;
    public long? FileSizeBytes { get; set; }
    public VideoMetadata? Metadata { get; set; } // Add this
}

public enum AudioFormat
{
    FLAC,
    MP3,
    WAV,
    AAC
}

public class VideoMetadata
{
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public string? Album { get; set; }
    public string? Track { get; set; }
    public string? Genre { get; set; }
    public int? Year { get; set; }
    public TimeSpan? Duration { get; set; }
    public string? Description { get; set; }
    public string? Uploader { get; set; }
    public DateTime? UploadDate { get; set; }
    public long? ViewCount { get; set; }
    public long? LikeCount { get; set; }
    public string? ThumbnailUrl { get; set; }
    public Dictionary<string, object> RawMetadata { get; set; } = new();
}