using System;
using System.Diagnostics;
using System.IO;
using VideoAudioExtractor;

public class TagExistingFlacs
{
    public static void Run(string audioDir, bool recursive = false)
    {
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var flacFiles = Directory.GetFiles(audioDir, "*.flac", searchOption);
        int tagged = 0, skipped = 0, errors = 0, reencoded = 0;

        Console.WriteLine($"Found {flacFiles.Length} FLAC files in {audioDir}");
        Console.WriteLine();

        foreach (var flacPath in flacFiles)
        {
            var baseName = Path.GetFileNameWithoutExtension(flacPath);
            var dashIndex = baseName.IndexOf(" - ");
            if (dashIndex <= 0)
            {
                Console.WriteLine($"  Skipped (no artist): {baseName}");
                skipped++;
                continue;
            }

            var artist = baseName[..dashIndex].Trim();
            var filePath = flacPath;

            // Check if the file is actually FLAC
            if (!IsRealFlac(flacPath))
            {
                Console.WriteLine($"  Re-encoding to FLAC: {baseName}");
                var converted = ReencodeToFlac(flacPath);
                if (converted == null)
                {
                    Console.WriteLine($"  Error re-encoding {baseName}");
                    errors++;
                    continue;
                }
                filePath = converted;
                reencoded++;
            }

            try
            {
                using var tagFile = TagLib.File.Create(filePath);
                tagFile.Tag.AlbumArtists = new[] { artist };
                tagFile.Tag.Performers = new[] { artist };
                tagFile.Tag.Title = baseName;
                tagFile.Save();
                Console.WriteLine($"  Tagged: {baseName}  ->  Artist: {artist}");
                tagged++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Error tagging {baseName}: {ex.Message}");
                errors++;
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Tagging complete: {tagged} tagged, {reencoded} re-encoded, {skipped} skipped, {errors} errors.");
    }

    private static bool IsRealFlac(string filePath)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffprobe",
                    Arguments = $"-v error -show_entries stream=codec_name -of csv=p=0 \"{filePath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.Trim().Equals("flac", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static string? ReencodeToFlac(string filePath)
    {
        var tempPath = filePath + ".tmp.flac";
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-i \"{filePath}\" -vn -c:a flac -sample_fmt s32 -y \"{tempPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            process.Start();
            process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0 || !File.Exists(tempPath))
                return null;

            File.Delete(filePath);
            File.Move(tempPath, filePath);
            return filePath;
        }
        catch
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            return null;
        }
    }
}
