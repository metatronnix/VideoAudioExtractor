using ClosedXML.Excel;
using DocumentFormat.OpenXml.Presentation;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using VideoAudioExtractor.Implementations;
using File = System.IO.File;

public class YouTubeAudioExtractor : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly Random _random = new();
    private readonly ExtractorConfig _config;
    private readonly IProgressReporter _progressReporter;

    // ────────────────────────────── User‑Agents pool
    private readonly string[] _userAgents =
    {
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121 Safari/537.36",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120 Safari/537.36 Edg/120.0.0.0",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:122.0) Gecko/20100101 Firefox/122.0",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121 Safari/537.36",
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121 Safari/537.36"
    };

    private DateTime _lastRequest = DateTime.MinValue;
    private readonly string _ytDlpPath;
    private readonly bool _useShellExecution;
    private readonly Dictionary<string, string> _argumentsCache = new();

    public YouTubeAudioExtractor(ExtractorConfig? config = null, IProgressReporter? progressReporter = null)
    {

        _config = config ?? new ExtractorConfig();
        _progressReporter = progressReporter ?? new EnhancedConsoleProgressReporter();

        _httpClient = new HttpClient { Timeout = _config.HttpTimeout };

        _ytDlpPath = YtDlpInstaller.FindYtDlpPath() ?? "yt-dlp";
        _useShellExecution = string.IsNullOrEmpty(YtDlpInstaller.FindYtDlpPath());

        if (_useShellExecution)
        {
            _ytDlpPath = "yt-dlp";
            _progressReporter.ReportInfo("Using shell execution mode for yt‑dlp.");
        }

        SetupHttpClient();
    }

    // ────────────────────────────── Excel loader
    public string[] LoadYouTubeUrlsFromExcel(string excelPath, string sheetName)
    {
        if (!File.Exists(excelPath))
        {
            Console.WriteLine($"❌ Excel not found: {excelPath}");
            return Array.Empty<string>();
        }

        using var workbook = new XLWorkbook(excelPath);
        var ws = workbook.Worksheet(sheetName);
        var urls = new List<string>();
        foreach (var row in ws.RowsUsed())
        {
            var url = row.Cell(1).GetString().Trim();
            if (url.StartsWith("http"))
                urls.Add(url);
        }

        Console.WriteLine($"✅ Loaded {urls.Count} URLs from Excel.");
        return urls.ToArray();
    }

    private void SetupHttpClient()
    {
        var ua = _userAgents[_random.Next(_userAgents.Length)];
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", ua);
    }

    // ────────────────────────────── Basic behavior pacing
    private async Task SimulateHumanBehavior()
    {
        var since = DateTime.Now - _lastRequest;
        if (since < TimeSpan.FromSeconds(30))
        {
            var wait = TimeSpan.FromSeconds(30) - since;
            _progressReporter.ReportInfo($"Cooldown {wait.TotalSeconds:F0}s...");
            await Task.Delay(wait);
        }
        await Task.Delay(_random.Next(2000, 5000));
        _lastRequest = DateTime.Now;
    }
    private string Sanitize(string s)
    {
        //_progressReporter.ReportInfo($"Sanitize input: '{s}' (length: {s.Length})");
        //_progressReporter.ReportInfo($"MaxFileNameLength config: {_config.MaxFileNameLength}");

        var original = s;
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            if (s.Contains(c))
            {
                //_progressReporter.ReportInfo($"Replacing invalid char: '{c}'");
                s = s.Replace(c, '_');
            }
        }

        /*
        if (s != original)
           _progressReporter.ReportInfo($"After invalid char replacement: '{s}'");
        */

        var result = s[..Math.Min(s.Length, _config.MaxFileNameLength)];
        //_progressReporter.ReportInfo($"Sanitize output: '{result}' (length: {result.Length})");

        return result;
    }

    // ────────────────────────────── Build yt‑dlp args (FLAC only)
    private async Task<string> BuildYtDlpArgumentsAsync(string url)
    {
        var ua = _userAgents[_random.Next(_userAgents.Length)];
        var cookieArg = "--cookies \"C:\\Audio\\youtube_cookies.txt\"";
        var outputTemplate = Path.Combine(_config.DefaultOutputDirectory, "%(id)s.%(ext)s");

        var args = new StringBuilder()
            .Append("--js-runtime node ")               // NEW: force yt‑dlp to use Node")
            .Append("-f bestaudio/bestaudio* ")
            .Append("--extract-audio --audio-format flac --audio-quality 0 ")
            .Append("--postprocessor-args \"ffmpeg:-sample_fmt s32\" ")
            .Append("--add-metadata --embed-metadata ")
            .Append("--write-info-json --write-description ")
            .Append("--no-playlist --ignore-errors ")
            .Append("--sleep-interval 5 --max-sleep-interval 15 ")
            .Append("--throttled-rate 100K --extractor-retries 5 --retry-sleep 10 ")
            .Append("--concurrent-fragments 5 ")
            .Append($"{cookieArg} ")
            .Append($"--user-agent \"{ua}\" ")
            .Append($"--output \"{outputTemplate}\" ")
            .Append("--ffmpeg-location \"C:\\ffmpeg\\bin\" ")
            .Append("\"").Append(url).Append("\"");

        await Task.Yield();
        return args.ToString();
    }

    private async Task<string> GetCachedArgumentsAsync(string url)
    {
        if (_argumentsCache.TryGetValue(url, out var cached))
            return cached;

        var built = await BuildYtDlpArgumentsAsync(url);
        _argumentsCache[url] = built;
        return built;
    }

    // ────────────────────────────── Primary extraction
    public async Task<ExtractionResult> ExtractAudioWithMetadataAsync(string url, string outputPath)
    {
        var result = new ExtractionResult { Url = url, StartTime = DateTime.Now };

        // Add this at the very start
        /*
        _progressReporter.ReportInfo($"=== EXTRACTION START DEBUG ===");
        _progressReporter.ReportInfo($"URL: {url}");
        _progressReporter.ReportInfo($"Output Path: {outputPath}");
        _progressReporter.ReportInfo($"=== EXTRACTION START DEBUG ===");
        */

        try
        {
            await SimulateHumanBehavior();
            var dir = Path.GetDirectoryName(outputPath)!;
            Directory.CreateDirectory(dir);
            var baseFile = Path.GetFileNameWithoutExtension(outputPath);

            /*
            _progressReporter.ReportInfo($"▶ Extracting: {url}");
            _progressReporter.ReportProgress("Preparing download...");
            */
            await DeleteExistingFilesAsync(dir, baseFile);
            var args = await GetCachedArgumentsAsync(url);

            /*
            _progressReporter.ReportInfo($"yt‑dlp path: {_ytDlpPath}");
            _progressReporter.ReportInfo($"yt‑dlp args: {args}");
            _progressReporter.ReportInfo($"ffmpeg location: C:\\ffmpeg\\bin");
            _progressReporter.ReportInfo($"Output dir: {_config.DefaultOutputDirectory}");
            */

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _ytDlpPath,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();

            var outputTask = MonitorProcessOutputAsync(process);
            await process.WaitForExitAsync();
            await outputTask;

            if (process.ExitCode != 0)
            {
                var stdErr = await process.StandardError.ReadToEndAsync();
                var stdOut = await process.StandardOutput.ReadToEndAsync();

                _progressReporter.ReportError("❌ yt‑dlp failed:");
                if (!string.IsNullOrWhiteSpace(stdErr))
                    _progressReporter.ReportError(stdErr.Trim());
                if (!string.IsNullOrWhiteSpace(stdOut))
                    _progressReporter.ReportInfo(stdOut.Trim());

                throw new InvalidOperationException($"yt‑dlp exited with code {process.ExitCode}");
            }
            await Task.Delay(1500); // let FS settle

            var flac = Directory.GetFiles(dir, "*.flac")
                                .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if (flac == null)
                throw new FileNotFoundException("No FLAC file found after extraction.");

            result.OutputPath = flac;
            result.FileSizeBytes = new FileInfo(flac).Length;
            result.Metadata = await LoadMetadataFromInfoJsonAsync(dir, baseFile);

            /*
            _progressReporter.ReportInfo($"=== FILE RENAMING DEBUG START ===");
            _progressReporter.ReportInfo($"Original FLAC path: {flac}");
            _progressReporter.ReportInfo($"BaseFile: {baseFile}");
            _progressReporter.ReportInfo($"Metadata loaded: {result.Metadata != null}");
       

            if (result.Metadata != null)
            {
                _progressReporter.ReportInfo($"Metadata Artist: '{result.Metadata.Artist}'");
                _progressReporter.ReportInfo($"Metadata Title: '{result.Metadata.Title}'");
            }
            */

            var name = CreateFileNameFromMetadata(result.Metadata, baseFile);
            //_progressReporter.ReportInfo($"Generated name: '{name}'");

            var newPath = Path.Combine(dir, $"{name}.flac");
            //_progressReporter.ReportInfo($"New path: {newPath}");

            //_progressReporter.ReportInfo($"Paths equal (ignore case): {string.Equals(flac, newPath, StringComparison.OrdinalIgnoreCase)}");

            if (!string.Equals(flac, newPath, StringComparison.OrdinalIgnoreCase))
            {
                //_progressReporter.ReportInfo("Paths are different, attempting rename...");

                if (File.Exists(newPath))
                {
                   // _progressReporter.ReportInfo($"Target file exists, deleting: {newPath}");
                    File.Delete(newPath);
                }

              //  _progressReporter.ReportInfo($"Moving file from '{flac}' to '{newPath}'");
                File.Move(flac, newPath);
                result.OutputPath = newPath;
             //   _progressReporter.ReportInfo("File move completed successfully");
            }
          
            result.Name = Path.GetFileNameWithoutExtension(result.OutputPath);

            // Re-encode to true FLAC if the file contains a different codec
            if (!IsRealFlac(result.OutputPath))
            {
                _progressReporter.ReportInfo($"Re-encoding to true FLAC: {result.Name}");
                ReencodeToFlac(result.OutputPath);
                result.FileSizeBytes = new FileInfo(result.OutputPath).Length;
            }

            // Calculate BPM and append to filename
            result.OutputPath = AppendBpmToFileName(result.OutputPath);
            result.Name = Path.GetFileNameWithoutExtension(result.OutputPath);

            // Tag artist from filename if it matches "Artist - Title"
            TagArtistFromFileName(result.OutputPath, result.Name);

            result.Success = true;

            // Clean up metadata files
            var videoId = ExtractVideoId(baseFile);
            await CleanupMetadataFiles(dir, videoId);

            _progressReporter.ReportCompletion($"🎧 {result.Name}");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            _progressReporter.ReportError(ex.Message);
        }
        finally
        {
            result.EndTime = DateTime.Now;
        }

        return result;
    }

    // ────────────────────────────── Batch wrapper
    public async IAsyncEnumerable<ExtractionResult> ExtractBatchAsync(IEnumerable<string> videos,
                                                                     string outputDir,
                                                                     AudioFormat fmt,
                                                                     [EnumeratorCancellation] CancellationToken ct = default)
    {
        var list = videos.ToArray();
        var enhanced = _progressReporter as EnhancedConsoleProgressReporter;
        enhanced?.StartBatch(list.Length);

        for (int i = 0; i < list.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var url = list[i];
            enhanced?.StartNewVideo(i + 1, url);

            var safe = Sanitize(Path.GetFileNameWithoutExtension(url));
            var outPath = Path.Combine(outputDir, $"{safe}.flac");

            // Temporarily disable output suppression for debug
                enhanced._suppressOutput = false; 
           
            /*
            _progressReporter.ReportInfo($"URL: {url}");
            _progressReporter.ReportInfo($"Output path: {outPath}");
            */

            var res = await ExtractAudioWithMetadataAsync(url, outPath);

            // Re-enable output suppression
            if (enhanced != null)
                enhanced._suppressOutput = true;
           
            yield return res;
            enhanced?.CompleteVideo(res.Name ?? safe);
        }

        enhanced?.CompleteBatch();
    }

    // ────────────────────────────── Legacy helpers kept intact
    private async Task MonitorProcessOutputAsync(Process process)
    {
        try
        {
            var outputTask = Task.Run(async () =>
            {
                while (!process.HasExited)
                {
                    var line = await process.StandardOutput.ReadLineAsync();

                    if (line == null) continue;

                    if (line.Contains("SABR") || line.Contains("Server‑Side Ad Placement"))
                        continue; // ignore SABR experiment warnings

                    //_progressReporter.ReportInfo($"yt‑dlp → {line}");

                    if (line.Contains("[download]") &&
                        _progressReporter is EnhancedConsoleProgressReporter r)
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(line, @"([\d\.]+)%");
                        if (m.Success && double.TryParse(m.Groups[1].Value, out var p))
                            r.ReportDownloadProgress(p);
                    }
                }
            });

            var errorTask = Task.Run(async () =>
            {
                while (!process.HasExited)
                {
                    var line = await process.StandardError.ReadLineAsync();
                    if (line == null) continue;
                    if (line.Contains("SABR")) continue;
                    _progressReporter.ReportError($"yt‑dlp stderr: {line}");
                }
            });

            await Task.WhenAll(outputTask, errorTask);
        }
        catch (Exception ex)
        {
            _progressReporter.ReportError($"MonitorProcessOutputAsync error: {ex.Message}");
        }
    }

    private async Task DeleteExistingFilesAsync(string dir, string baseName)
    {
        try
        {
            if (!Directory.Exists(dir)) return;
            var exts = new[] { ".flac", ".mp3", ".wav", ".m4a", ".webm" };
            foreach (var f in Directory.GetFiles(dir))
            {
                if (exts.Contains(Path.GetExtension(f).ToLower()) &&
                    Path.GetFileNameWithoutExtension(f).Equals(baseName, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(f);
                    //_progressReporter.ReportInfo($"🗑️ Deleted old file: {Path.GetFileName(f)}");
                }
            }
            await Task.Delay(500);
        }
        catch (Exception e)
        {
            _progressReporter.ReportError($"Cleanup error: {e.Message}");
        }
    }

    private async Task<VideoMetadata?> LoadMetadataFromInfoJsonAsync(string dir, string baseFile)
    {
        try
        {
            // Extract just the video ID from the URL-based baseFile
            var videoId = ExtractVideoId(baseFile);

            var info = Directory.GetFiles(dir, "*.info.json")
                                .FirstOrDefault(f => Path.GetFileName(f).StartsWith(videoId));

            if (info == null)
            {
             //   _progressReporter.ReportError($"No .info.json file found for video ID: {videoId}");
                return null;
            }

           // _progressReporter.ReportInfo($"Found metadata file: {Path.GetFileName(info)}");

            var json = await File.ReadAllTextAsync(info);
            var root = JsonDocument.Parse(json).RootElement;

            var title = root.TryGetProperty("title", out var t) ? t.GetString() : null;

            if (string.IsNullOrWhiteSpace(title))
                return null;

            // Parse artist and song from title if it contains " - "
            string? artist = null;
            string? song = title;

            var dashIndex = title.IndexOf(" - ");
            if (dashIndex > 0)
            {
                artist = title.Substring(0, dashIndex).Trim();
                song = title.Substring(dashIndex + 3).Trim();
            }
            else
            {
                artist = root.TryGetProperty("uploader", out var u) ? u.GetString() : null;
            }

            var m = new VideoMetadata
            {
                Title = song,
                Artist = artist,
                Duration = root.TryGetProperty("duration", out var d) ? TimeSpan.FromSeconds(d.GetDouble()) : null
            };

            //_progressReporter.ReportInfo($"Loaded metadata - Artist: '{m.Artist}', Title: '{m.Title}'");
            return m;
        }
        catch (Exception ex)
        {
            _progressReporter.ReportError($"Metadata read failed: {ex.Message}");
            return null;
        }
    }

    private string ExtractVideoId(string baseFile)
    {
        // Extract video ID from URLs like "watch_v=1Mk-SAyNKNk&list=..."
        if (baseFile.Contains("watch_v="))
        {
            var start = baseFile.IndexOf("watch_v=") + 8;
            var end = baseFile.IndexOf("&", start);
            if (end == -1) end = baseFile.Length;
            return baseFile.Substring(start, end - start);
        }
        return baseFile;
    }

    private async Task<VideoMetadata?> TryParseFromDescriptionAsync(string dir, string baseFile)
    {
        try
        {
            var descFile = Directory.GetFiles(dir, "*.description")
                                   .FirstOrDefault(f => Path.GetFileName(f).Contains(baseFile));
            if (descFile == null) return null;

            var content = await File.ReadAllTextAsync(descFile);
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            if (lines.Length < 2) return null;

            // Parse the description format:
            // Line 1: "Provided to YouTube by [Label]"
            // Line 2: "Track Title · Artist Name"
            // Line 3: "Album Name"

            var trackLine = lines[1].Trim();
            var parts = trackLine.Split(" · ", StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length >= 2)
            {
                var trackTitle = parts[0].Trim();
                var artistName = parts[1].Trim();

                // Clean up common suffixes in track titles
                trackTitle = CleanTrackTitle(trackTitle);

                return new VideoMetadata
                {
                    Title = trackTitle,
                    Artist = artistName,
                    Duration = null // We'll get this from JSON if needed
                };
            }
        }
        catch (Exception ex)
        {
            _progressReporter.ReportError($"Failed to parse description file: {ex.Message}");
        }

        return null;
    }

    private string CleanTrackTitle(string title)
    {
        // Remove common suffixes like "(2019 remaster)", "(Remastered)", etc.
        var patterns = new[]
        {
        @"\s*\(\d{4}\s+remaster\)",
        @"\s*\(remaster\w*\)",
        @"\s*\(remaste\w*\)",
        @"\s*-\s+\d{4}\s+remaster",
        @"\s*-\s+remaster\w*"
    };

        foreach (var pattern in patterns)
        {
            title = System.Text.RegularExpressions.Regex.Replace(
                title, pattern, "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        return title.Trim();
    }

    private string CreateFileNameFromMetadata(VideoMetadata? m, string fallback)
    {
        /*
        _progressReporter.ReportInfo($"=== FILENAME DEBUG START ===");
        _progressReporter.ReportInfo($"Fallback: '{fallback}'");
        _progressReporter.ReportInfo($"MaxFileNameLength: {_config.MaxFileNameLength}");
        */
        if (m == null)
        {
            //_progressReporter.ReportInfo("Metadata is null, using fallback");
            var result = Sanitize(fallback);
            //_progressReporter.ReportInfo($"Final result: '{result}'");
            //_progressReporter.ReportInfo($"=== FILENAME DEBUG END ===");
            return result;
        }

        //_progressReporter.ReportInfo($"Metadata Artist: '{m.Artist}'");
        //_progressReporter.ReportInfo($"Metadata Title: '{m.Title}'");

        var artist = m.Artist ?? "Unknown Artist";
        var title = m.Title ?? fallback;

        //_progressReporter.ReportInfo($"Final Artist: '{artist}'");
        //_progressReporter.ReportInfo($"Final Title: '{title}'");

        var name = !string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(title)
            ? $"{artist} - {title}"
            : title;

        //_progressReporter.ReportInfo($"Constructed name: '{name}' (length: {name.Length})");

        var sanitized = Sanitize(name);
        //_progressReporter.ReportInfo($"After Sanitize: '{sanitized}' (length: {sanitized.Length})");
        //_progressReporter.ReportInfo($"=== FILENAME DEBUG END ===");

        return sanitized;
    }

    private async Task CleanupMetadataFiles(string dir, string videoId)
    {
        try
        {
            var filesToDelete = Directory.GetFiles(dir, $"{videoId}.*")
                                       .Where(f => f.EndsWith(".description") || f.EndsWith(".info.json"))
                                       .ToArray();

            foreach (var file in filesToDelete)
            {
                File.Delete(file);
               // _progressReporter.ReportInfo($"🗑️ Deleted: {Path.GetFileName(file)}");
            }

            await Task.Delay(100); // Brief pause to ensure file system operations complete
        }
        catch (Exception ex)
        {
            _progressReporter.ReportError($"Cleanup warning: {ex.Message}");
            // Don't throw - cleanup failure shouldn't stop the main process
        }
    }

    private bool IsRealFlac(string filePath)
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

    private void ReencodeToFlac(string filePath)
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

            if (process.ExitCode == 0 && System.IO.File.Exists(tempPath))
            {
                System.IO.File.Delete(filePath);
                System.IO.File.Move(tempPath, filePath);
            }
            else
            {
                _progressReporter.ReportError($"Re-encoding failed for {Path.GetFileName(filePath)}");
                if (System.IO.File.Exists(tempPath))
                    System.IO.File.Delete(tempPath);
            }
        }
        catch (Exception ex)
        {
            _progressReporter.ReportError($"Re-encoding error: {ex.Message}");
            if (System.IO.File.Exists(tempPath))
                System.IO.File.Delete(tempPath);
        }
    }

    private string AppendBpmToFileName(string filePath)
    {
        try
        {
            var bpmCalc = new VideoAudioExtractor.BPMCalculator();
            var bpm = (int)bpmCalc.CalculateBPM(filePath);
            _progressReporter.ReportInfo($"BPM detected: {bpm}");

            var dir = Path.GetDirectoryName(filePath)!;
            var name = Path.GetFileNameWithoutExtension(filePath);
            var newPath = Path.Combine(dir, $"{name} - {bpm}.flac");

            if (File.Exists(newPath))
                File.Delete(newPath);

            File.Move(filePath, newPath);
            return newPath;
        }
        catch (Exception ex)
        {
            _progressReporter.ReportError($"BPM detection error: {ex.Message}");
            return filePath;
        }
    }

    private void TagArtistFromFileName(string filePath, string fileName)
    {
        var dashIndex = fileName.IndexOf(" - ");
        if (dashIndex <= 0) return;

        var artist = fileName[..dashIndex].Trim();
        try
        {
            using var tagFile = TagLib.File.Create(filePath);
            tagFile.Tag.AlbumArtists = new[] { artist };
            tagFile.Tag.Performers = new[] { artist };
            tagFile.Save();
        }
        catch (Exception ex)
        {
            _progressReporter.ReportError($"Tagging error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}