using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using VideoAudioExtractor.Implementations;
using File = System.IO.File;
// Main extractor class
public class YouTubeAudioExtractor : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly Random _random = new();
    private readonly ExtractorConfig _config;
    private readonly IProgressReporter _progressReporter;
    private readonly string[] _userAgents = [
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Safari/537.36",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 Edg/120.0.0.0",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:122.0) Gecko/20100101 Firefox/122.0",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Safari/537.36",
        "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Safari/537.36"
    ];

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

        // Check if we need to use shell execution (fallback mode)
        _useShellExecution = string.IsNullOrEmpty(YtDlpInstaller.FindYtDlpPath());
        if (_useShellExecution)
        {
            _ytDlpPath = "yt-dlp";
            _progressReporter.ReportInfo("Using shell execution mode for yt-dlp");
        }

        SetupHttpClient();
    }

    public string[] LoadYouTubeUrlsFromExcel(string excelPath, string sheetName)
    {
        if (!File.Exists(excelPath))
        {
            Console.WriteLine($"❌ Excel file not found: {excelPath}");
            return Array.Empty<string>();
        }

        using var workbook = new ClosedXML.Excel.XLWorkbook(excelPath);
        var ws = workbook.Worksheet(sheetName);

        var urls = new List<string>();
        foreach (var row in ws.RowsUsed())
        {
            var url = row.Cell(1).GetString().Trim(); // assumes column A
            if (!string.IsNullOrEmpty(url) &&
                (url.StartsWith("http://") || url.StartsWith("https://")))
            {
                urls.Add(url);
            }
        }

        Console.WriteLine($"✅ Loaded {urls.Count} URLs from Excel file");
        return urls.ToArray();
    }


    private void SetupHttpClient()
    {
        var userAgent = _userAgents[_random.Next(_userAgents.Length)];
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", userAgent);
    }

    // Reduce human behavior simulation
    private async Task SimulateHumanBehavior()
    {
        var timeSinceLastRequest = DateTime.Now - _lastRequest;

        if (timeSinceLastRequest < TimeSpan.FromSeconds(30)) // Reduced from 1 minute
        {
            var waitTime = TimeSpan.FromSeconds(30) - timeSinceLastRequest;
            _progressReporter.ReportInfo($"Rate limiting: waiting {waitTime.TotalSeconds:F0} seconds...");
            await Task.Delay(waitTime);
        }

        // Reduced delay
        var extraDelay = _random.Next(2000, 5000); // Reduced from 10-30 seconds
        await Task.Delay(extraDelay);

        _lastRequest = DateTime.Now;
    }

    private string? SanitizeFileName(string fileName) =>
        Path.GetInvalidFileNameChars()
            .Aggregate(fileName, (current, c) => current.Replace(c, '_'))
            [..Math.Min(fileName.Length, _config.MaxFileNameLength)];

    /*
    private async Task<string> BuildYtDlpArgumentsAsync(
    string youtubeUrl,
    string outputTemplate,
    AudioFormat format)
    {
        // Prefer m4a (progressive) to avoid SABR when possible
        var formatArgs = format switch
        {
            AudioFormat.FLAC => "-f bestaudio/bestaudio* --audio-format flac --audio-quality 0 --embed-thumbnail",
            AudioFormat.MP3 => "-f bestaudio/bestaudio* --audio-format mp3 --audio-quality 0",
            AudioFormat.WAV => "-f bestaudio/bestaudio* --audio-format wav",
            AudioFormat.AAC => "-f bestaudio/bestaudio* --audio-format aac --audio-quality 0",
            _ => "-f bestaudio/bestaudio* --audio-format flac --audio-quality 0 -f bestaudio/bestaudio* --embed-thumbnail"
        };

        var formatArgs =
    "-f bestaudio/bestaudio* " +
    "--audio-format flac --audio-quality 0 " +
    "--embed-thumbnail --convert-thumbnails jpg " +
    "--extractor-args \"youtube:player_client=android\"";

        var cookieArg = await GetWorkingCookieArgumentAsync();
        var userAgent = _userAgents[_random.Next(_userAgents.Length)];

        // Build base arguments
        var baseArgs =
            $"{formatArgs} " +
            $"{cookieArg} " +
            $"--user-agent \"{userAgent}\" " +
            $"--output \"{outputTemplate}\" " +
            $"--no-playlist " +
            $"--embed-metadata " +
            $"--add-metadata " +
            $"--write-info-json " +
            $"--write-description " +
            $"--write-thumbnail " +
            $"--sleep-interval 5 " +
            $"--max-sleep-interval 15 " +
            $"--throttled-rate 100K " +
            $"--extractor-retries 5 " +
            $"--retry-sleep 10 " +
            // SABR handling
            $"--concurrent-fragments 5 ";   // download multiple segments in parallel
        
        // Add thumbnail embedding for supported formats
        if (format == AudioFormat.FLAC || format == AudioFormat.MP3)
        {
            baseArgs += "--embed-thumbnail --convert-thumbnails jpg ";
        }

        baseArgs += $"\"{youtubeUrl}\"";

        return baseArgs;
    }
    */
    private async Task<string> BuildYtDlpArgumentsAsync(
        string youtubeUrl,
        AudioFormat format)
    {
        // We only support FLAC here
        var formatArgs =
       "-f bestaudio/bestaudio* " + // pick only audio track, never full video
       "--extract-audio " +        // explicit audio extraction + conversion
       "--audio-format flac --audio-quality 0 " +
       "--add-metadata --embed-metadata " +        // metadata embedding
       "--convert-thumbnails jpg " +
       "--ignore-errors --no-overwrites " +        // extra diagnostics if needed
       "--write-info-json --write-description";        // keep metadata sidecars

        var cookieArg = await GetWorkingCookieArgumentAsync();
        var userAgent = _userAgents[_random.Next(_userAgents.Length)];

        // Always create predictable output folder and safe fallback naming
        //var outputTemplate = Path.Combine(_config.DefaultOutputDirectory, "%(title,video_id)s.%(ext)s");
        var outputTemplate = Path.Combine(_config.DefaultOutputDirectory, "%(id)s.%(ext)s");


        var args = new StringBuilder()
            .Append($"{formatArgs} ")
            .Append($"{cookieArg} ")
            .Append($"--user-agent \"{userAgent}\" ")
            .Append($"--output \"{outputTemplate}\" ")
            .Append("--no-playlist ")
            .Append("--sleep-interval 5 --max-sleep-interval 15 ")
            .Append("--throttled-rate 100K --extractor-retries 5 --retry-sleep 10 ")
            .Append("--concurrent-fragments 5 ")
            .Append($"\"{youtubeUrl}\"") 
            .Append("--ffmpeg-location \"C:\\ffmpeg\\bin\" ")
            .Append("--keep-video");


        Console.WriteLine($"Running yt-dlp with {args}");
        return args.ToString();
    }

    private async Task<string> GetCachedArgumentsAsync(string youtubeUrl, AudioFormat format)
    {
        var cacheKey = $"{youtubeUrl}|{format}";

        if (_argumentsCache.TryGetValue(cacheKey, out var cachedArgs))
        {
            _progressReporter.ReportInfo("Using cached yt-dlp arguments");
            return cachedArgs;
        }

        var arguments = await BuildYtDlpArgumentsAsync(youtubeUrl, format);
        _argumentsCache[cacheKey] = arguments;

        return arguments;
    }

    public async Task<bool> TestVideoAccessAsync(string youtubeUrl)
    {
        try
        {
            _progressReporter.ReportInfo($"Testing YouTube URL: {youtubeUrl}");

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _ytDlpPath,
                    Arguments = $"--dump-json \"{youtubeUrl}\"",
                    UseShellExecute = _useShellExecution,
                    RedirectStandardOutput = !_useShellExecution,
                    RedirectStandardError = !_useShellExecution,
                    CreateNoWindow = true
                }
            };

            process.Start();

            if (_useShellExecution)
            {
                await process.WaitForExitAsync();
                if (process.ExitCode == 0)
                {
                    _progressReporter.ReportCompletion("Video appears to be accessible (shell mode)");
                    return true;
                }
                else
                {
                    _progressReporter.ReportError("Video test failed (shell mode)");
                    return false;
                }
            }
            else
            {
                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                {
                    try
                    {
                        // Handle multiple JSON objects (one per line)
                        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                        JsonElement videoInfo = default;

                        // Try to parse the first valid JSON line
                        foreach (var line in lines)
                        {
                            var trimmedLine = line.Trim();
                            if (string.IsNullOrEmpty(trimmedLine) || !trimmedLine.StartsWith("{"))
                                continue;

                            try
                            {
                                videoInfo = JsonSerializer.Deserialize<JsonElement>(trimmedLine);
                                break; // Successfully parsed, use this one
                            }
                            catch (JsonException)
                            {
                                continue; // Try next line
                            }
                        }

                        // Check if we successfully parsed any JSON
                        if (videoInfo.ValueKind == JsonValueKind.Undefined)
                        {
                            _progressReporter.ReportError("No valid JSON found in yt-dlp output");
                            return false;
                        }

                        // Extract video information
                        var title = videoInfo.TryGetProperty("title", out var titleProp)
                            ? titleProp.GetString() ?? "Unknown Title"
                            : "Unknown Title";

                        var duration = videoInfo.TryGetProperty("duration", out var durationProp)
                            ? durationProp.GetDouble()
                            : 0;

                        _progressReporter.ReportCompletion($"Video accessible: {title}");

                        if (duration > 0)
                        {
                            _progressReporter.ReportInfo($"Duration: {TimeSpan.FromSeconds(duration):mm\\:ss}");
                        }

                        return true;
                    }
                    catch (Exception ex)
                    {
                        _progressReporter.ReportError($"Could not parse video info: {ex.Message}");
                        _progressReporter.ReportInfo($"Raw output (first 500 chars): {output[..Math.Min(500, output.Length)]}");
                        return false;
                    }
                }
                else
                {
                    _progressReporter.ReportError($"Video not accessible: {error}");
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            _progressReporter.ReportError($"Test failed: {ex.Message}");
            return false;
        }
    }

    private async Task CleanupMetadataFilesAsync(string outputDir, string baseFileName)
    {
        try
        {
            var extensionsToDelete = new[] { ".info.json", ".description" };

            foreach (var ext in extensionsToDelete)
            {
                var files = Directory.GetFiles(outputDir, $"*{ext}")
                    .Where(f => Path.GetFileNameWithoutExtension(f)
                        .Contains(baseFileName, StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                foreach (var file in files)
                {
                    try
                    {
                        System.IO.File.Delete(file);
                        _progressReporter.ReportInfo($"🗑️ Deleted metadata file: {Path.GetFileName(file)}");
                    }
                    catch (Exception ex)
                    {
                        _progressReporter.ReportError($"Failed to delete {Path.GetFileName(file)}: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _progressReporter.ReportError($"Error cleaning up metadata files: {ex.Message}");
        }

        await Task.CompletedTask;
    }

    public async Task<ExtractionResult> ExtractAudioWithMetadataAsync(
      string youtubeUrl,
      string outputPath,
      AudioFormat format = AudioFormat.FLAC)
    {
        var result = new ExtractionResult
        {
            Url = youtubeUrl,
            StartTime = DateTime.Now
        };

        try
        {
            await SimulateHumanBehavior();
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            var outputDir = Path.GetDirectoryName(outputPath)!;
            var baseFileName = Path.GetFileNameWithoutExtension(outputPath);

            _progressReporter.ReportInfo($"Target output path: {outputPath}");
            _progressReporter.ReportProgress($"Extracting audio and metadata: {Path.GetFileName(outputPath)}");

            await DeleteExistingFilesAsync(outputDir, baseFileName);

            // Build arguments once - this will extract both audio and metadata
            var arguments = await GetCachedArgumentsAsync(youtubeUrl, format);

            _progressReporter.ReportInfo($"yt-dlp command: {_ytDlpPath} {arguments}");

            using var process = new Process();
            var fileInfoBefore = GetFileInfoSnapshot(outputDir);

            if (_useShellExecution)
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = _ytDlpPath,
                    Arguments = arguments,
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
            }
            else
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = _ytDlpPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
            }

            _progressReporter.ReportInfo("Starting yt-dlp process...");
            process.Start();

            // Handle output monitoring based on execution mode
            if (_useShellExecution)
            {
                var processTask = Task.Run(() => process.WaitForExit());
                var timeoutTask = Task.Delay(TimeSpan.FromMinutes(30));
                var activityCheckTask = MonitorProcessActivityAsync(process);

                var completedTask = await Task.WhenAny(processTask, timeoutTask, activityCheckTask);

                if (completedTask == timeoutTask)
                {
                    if (!process.HasExited) process.Kill();
                    throw new TimeoutException("yt-dlp process timed out");
                }
                else if (completedTask == activityCheckTask && !process.HasExited)
                {
                    if (!process.HasExited) process.Kill();
                    throw new InvalidOperationException("yt-dlp process stuck");
                }
            }
            else
            {
                var outputTask = MonitorProcessOutputAsync(process);
                var processTask = process.WaitForExitAsync();
                var timeoutTask = Task.Delay(TimeSpan.FromMinutes(30));
                var activityCheckTask = MonitorProcessActivityAsync(process);

                var completedTask = await Task.WhenAny(processTask, timeoutTask, activityCheckTask);

                if (completedTask == timeoutTask)
                {
                    if (!process.HasExited) process.Kill();
                    throw new TimeoutException("yt-dlp process timed out");
                }
                else if (completedTask == activityCheckTask && !process.HasExited)
                {
                    if (!process.HasExited) process.Kill();
                    throw new InvalidOperationException("yt-dlp process stuck");
                }

                await outputTask;
            }

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"yt-dlp failed with exit code {process.ExitCode}");
            }

            await Task.Delay(2000); // Wait for file system to update

            // Find the created audio file
            var createdFile = await FindCreatedOrUpdatedFileAsync(outputDir, baseFileName, fileInfoBefore);

            if (string.IsNullOrEmpty(createdFile))
            {
                await DebugMissingFileAsync(outputDir, baseFileName, fileInfoBefore);
                throw new FileNotFoundException("yt-dlp completed but no output file was found");
            }

            // Move file if necessary
            if (!string.Equals(createdFile, outputPath, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (System.IO.File.Exists(outputPath))
                        System.IO.File.Delete(outputPath);
                    System.IO.File.Move(createdFile, outputPath);
                    _progressReporter.ReportInfo($"File moved successfully to {outputPath}");
                }
                catch (Exception ex)
                {
                    _progressReporter.ReportError($"Failed to move file: {ex.Message}");
                }
            }

            var finalPath = System.IO.File.Exists(outputPath) ? outputPath : createdFile;
            if (!System.IO.File.Exists(finalPath))
            {
                throw new FileNotFoundException($"Output file not found at expected location: {finalPath}");
            }

            // Load metadata from the .info.json file created by yt-dlp
            result.Metadata = await LoadMetadataFromInfoJsonAsync(outputDir, baseFileName);

            // Prefer metadata.Title, fallback to video ID
            var fallbackName = baseFileName; // video ID
            result.Name = CreateFileNameFromMetadata(result.Metadata, fallbackName, format);

            // Build the new final path (title-based if available)
            var desiredPath = Path.Combine(
                Path.GetDirectoryName(finalPath)!,
                $"{result.Name}.{format.ToString().ToLower()}"
            );

            // Rename the file if needed
            if (!string.Equals(finalPath, desiredPath, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (System.IO.File.Exists(desiredPath)) System.IO.File.Delete(desiredPath);
                    System.IO.File.Move(finalPath, desiredPath);
                    _progressReporter.ReportInfo($"Renamed file to: {Path.GetFileName(desiredPath)}");
                    finalPath = desiredPath; // update finalPath
                }
                catch (Exception ex)
                {
                    _progressReporter.ReportError($"Failed to rename file: {ex.Message}");
                }
            }

            // Handle thumbnail embedding for formats that support it
            if (format == AudioFormat.FLAC || format == AudioFormat.MP3)
            {
                // Pass result.Name (title-based) instead of video ID
                await HandleThumbnailEmbeddingAsync(finalPath, outputDir, result.Name, format, baseFileName);
            }

            // Clean up leftover metadata files
            await CleanupMetadataFilesAsync(outputDir, baseFileName);

            // Set result properties
            result.Success = true;
            result.OutputPath = finalPath;
            result.FileSizeBytes = new FileInfo(finalPath).Length;

            var fileInfo = new FileInfo(finalPath);
            _progressReporter.ReportInfo($"Extraction completed: {Path.GetFileName(finalPath)} ({fileInfo.Length / (1024.0 * 1024.0):F1} MB)");

            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = $"{ex.GetType().Name}: {ex.Message}";
            _progressReporter.ReportError($"Combined extraction failed: {ex.Message}");
            return result;
        }
        finally
        {
            result.EndTime = DateTime.Now;
        }
    }

    // Add these supporting methods to your YouTubeAudioExtractor class:

    private async Task HandleThumbnailEmbeddingAsync(
        string audioFilePath,
        string outputDir,
        string baseNameForMatching, // title-based
        AudioFormat format,
        string videoId)             // video ID
    {
        try
        {
            _progressReporter.ReportInfo("🖼️ Checking for thumbnail to embed...");

            // Look for downloaded thumbnail files using the title-based name
            var thumbnailFiles = await FindThumbnailFilesAsync(outputDir, baseNameForMatching, videoId);

            if (thumbnailFiles.Length == 0)
            {
                _progressReporter.ReportInfo("No thumbnail files found for embedding");
                return;
            }

            var thumbnailPath = thumbnailFiles.First();
            _progressReporter.ReportInfo($"Found thumbnail: {Path.GetFileName(thumbnailPath)}");

            // Check if thumbnail is already embedded (yt-dlp might have done it automatically)
            if (await IsThumbnailAlreadyEmbeddedAsync(audioFilePath))
            {
                _progressReporter.ReportInfo("✅ Thumbnail already embedded in audio file");
                await CleanupThumbnailFilesAsync(thumbnailFiles);
                return;
            }

            // Convert thumbnail to JPG if needed (for better compatibility)
            var jpgThumbnailPath = await ConvertThumbnailToJpgAsync(thumbnailPath);

            // Embed thumbnail using FFmpeg
            var success = await EmbedThumbnailWithFFmpegAsync(audioFilePath, jpgThumbnailPath, format);

            if (success)
            {
                _progressReporter.ReportInfo("✅ Successfully embedded thumbnail in audio file");
            }
            else
            {
                _progressReporter.ReportError("❌ Failed to embed thumbnail in audio file");
            }

            // Clean up thumbnail files
            await CleanupThumbnailFilesAsync(thumbnailFiles);
            if (jpgThumbnailPath != thumbnailPath && System.IO.File.Exists(jpgThumbnailPath))
            {
                try
                {
                    System.IO.File.Delete(jpgThumbnailPath);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
        catch (Exception ex)
        {
            _progressReporter.ReportError($"Error handling thumbnail embedding: {ex.Message}");
        }
    }

    private async Task<string[]> FindThumbnailFilesAsync(string outputDir, string baseNameForMatching, string videoId = "")
    {
        try
        {
            if (!Directory.Exists(outputDir))
                return Array.Empty<string>();

            var thumbnailExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp", ".avif" };
            var allFiles = Directory.GetFiles(outputDir, "*.*");

            var thumbnailFiles = allFiles
                .Where(file =>
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    var extension = Path.GetExtension(file).ToLower();

                    // Match against title-based name OR video ID
                    return thumbnailExtensions.Contains(extension) &&
                           (fileName.Equals(baseNameForMatching, StringComparison.OrdinalIgnoreCase) ||
                            fileName.StartsWith(baseNameForMatching, StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrEmpty(videoId) &&
                             (fileName.Equals(videoId, StringComparison.OrdinalIgnoreCase) ||
                              fileName.StartsWith(videoId, StringComparison.OrdinalIgnoreCase))));
                })
                .OrderByDescending(f => new FileInfo(f).Length) // Prefer larger thumbnails
                .ToArray();

            return thumbnailFiles;
        }
        catch (Exception ex)
        {
            _progressReporter.ReportError($"Error finding thumbnail files: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    private async Task<bool> IsThumbnailAlreadyEmbeddedAsync(string audioFilePath)
    {
        try
        {
            // Use FFprobe to check if the audio file already has embedded artwork
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffprobe",
                    Arguments = $"-v quiet -select_streams v:0 -show_entries stream=codec_name -of csv=p=0 \"{audioFilePath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();

            if (!process.WaitForExit(10000)) // 10 second timeout
            {
                process.Kill();
                return false;
            }

            var output = await process.StandardOutput.ReadToEndAsync();

            // If there's video stream output, it likely means there's embedded artwork
            return !string.IsNullOrWhiteSpace(output) &&
                   (output.Contains("mjpeg") || output.Contains("png") || output.Contains("jpg"));
        }
        catch (Exception ex)
        {
            _progressReporter.ReportError($"Error checking for embedded thumbnail: {ex.Message}");
            return false; // Assume not embedded if we can't check
        }
    }

    private async Task<string> ConvertThumbnailToJpgAsync(string thumbnailPath)
    {
        var extension = Path.GetExtension(thumbnailPath).ToLower();
        if (extension == ".jpg" || extension == ".jpeg")
            return thumbnailPath;

        var jpgPath = Path.ChangeExtension(thumbnailPath, ".jpg");

        try
        {
            _progressReporter.ReportInfo($"Converting thumbnail to JPG: {Path.GetFileName(thumbnailPath)}");

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = $"-i \"{thumbnailPath}\" -q:v 2 -y \"{jpgPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();

            if (!process.WaitForExit(30000)) // 30 second timeout
            {
                process.Kill();
                throw new TimeoutException("Thumbnail conversion timed out");
            }

            if (process.ExitCode == 0 && System.IO.File.Exists(jpgPath))
            {
                _progressReporter.ReportInfo("✅ Thumbnail converted to JPG successfully");
                return jpgPath;
            }
            else
            {
                var error = await process.StandardError.ReadToEndAsync();
                throw new InvalidOperationException($"FFmpeg conversion failed: {error}");
            }
        }
        catch (Exception ex)
        {
            _progressReporter.ReportError($"Failed to convert thumbnail to JPG: {ex.Message}");
            return thumbnailPath; // Return original if conversion fails
        }
    }

    private async Task<bool> EmbedThumbnailWithFFmpegAsync(
       string audioFilePath,
       string thumbnailPath,
       AudioFormat format)
    {
        try
        {
            if (!System.IO.File.Exists(audioFilePath) || !System.IO.File.Exists(thumbnailPath))
            {
                _progressReporter.ReportError("Audio file or thumbnail not found for embedding");
                return false;
            }

            var tempOutputPath = Path.ChangeExtension(audioFilePath, ".temp" + Path.GetExtension(audioFilePath));

            _progressReporter.ReportInfo("🖼️ Embedding thumbnail into audio file...");

            // Build FFmpeg arguments based on format
            string ffmpegArgs;
            if (format == AudioFormat.FLAC)
            {
                ffmpegArgs = $"-i \"{audioFilePath}\" -i \"{thumbnailPath}\" " +
                             "-map 0:0 -map 1:0 " +
                             "-c:a copy -c:v:0 mjpeg " +
                             "-disposition:v:0 attached_pic " +
                             "-metadata:s:v title=\"Album cover\" " +
                             "-metadata:s:v comment=\"Cover (front)\" " +
                             $"-y \"{tempOutputPath}\"";
            }
            else if (format == AudioFormat.MP3)
            {
                ffmpegArgs = $"-i \"{audioFilePath}\" -i \"{thumbnailPath}\" " +
                             "-map 0:0 -map 1:0 " +
                             "-c:a copy -c:v:0 mjpeg " +
                             "-id3v2_version 3 " +
                             "-metadata:s:v title=\"Album cover\" " +
                             "-metadata:s:v comment=\"Cover (front)\" " +
                             $"-y \"{tempOutputPath}\"";
            }
            else
            {
                _progressReporter.ReportError($"Thumbnail embedding not supported for {format} format");
                return false;
            }

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    Arguments = ffmpegArgs,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();

            if (!process.WaitForExit(60000)) // 60 second timeout
            {
                process.Kill();
                throw new TimeoutException("Thumbnail embedding timed out");
            }

            var stderr = await process.StandardError.ReadToEndAsync();

            if (process.ExitCode == 0 && System.IO.File.Exists(tempOutputPath))
            {
                // Replace original file with the new one
                var originalSize = new FileInfo(audioFilePath).Length;
                var newSize = new FileInfo(tempOutputPath).Length;

                System.IO.File.Delete(audioFilePath);
                System.IO.File.Move(tempOutputPath, audioFilePath);

                _progressReporter.ReportInfo(
                    $"✅ Thumbnail embedded successfully (size: {originalSize / 1024:N0} KB → {newSize / 1024:N0} KB)"
                );
                return true;
            }
            else
            {
                _progressReporter.ReportError($"FFmpeg thumbnail embedding failed: {stderr}");

                // Clean up temp file if it exists
                if (System.IO.File.Exists(tempOutputPath))
                {
                    try { System.IO.File.Delete(tempOutputPath); } catch { /* ignore */ }
                }

                return false;
            }
        }
        catch (Exception ex)
        {
            _progressReporter.ReportError($"Failed to embed thumbnail: {ex.Message}");
            return false;
        }
    }

    private async Task CleanupThumbnailFilesAsync(string[] thumbnailFiles)
    {
        foreach (var thumbnailFile in thumbnailFiles)
        {
            try
            {
                if (System.IO.File.Exists(thumbnailFile))
                {
                    System.IO.File.Delete(thumbnailFile);
                    _progressReporter.ReportInfo($"🗑️ Cleaned up thumbnail file: {Path.GetFileName(thumbnailFile)}");
                }
            }
            catch (Exception ex)
            {
                _progressReporter.ReportError($"Failed to delete thumbnail file {Path.GetFileName(thumbnailFile)}: {ex.Message}");
                // Continue with other files - don't fail the entire process
            }
        }
    }

    private async Task<VideoMetadata?> LoadMetadataFromInfoJsonAsync(string outputDir, string baseFileName)
    {
        try
        {
            // Look for .info.json file created by yt-dlp
            var infoJsonFiles = Directory.GetFiles(outputDir, "*.info.json")
                .Where(f => Path.GetFileNameWithoutExtension(f).Contains(baseFileName, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (infoJsonFiles.Length == 0)
            {
                _progressReporter.ReportInfo("No .info.json file found, metadata will be limited");
                return null;
            }

            var infoJsonPath = infoJsonFiles.First();
            _progressReporter.ReportInfo($"Loading metadata from: {Path.GetFileName(infoJsonPath)}");

            var jsonContent = await System.IO.File.ReadAllTextAsync(infoJsonPath);
            var jsonDoc = JsonDocument.Parse(jsonContent);
            var root = jsonDoc.RootElement;

            var metadata = new VideoMetadata();

            // Extract metadata using the same logic as before
            if (root.TryGetProperty("title", out var titleProp))
                metadata.Title = titleProp.GetString();

            if (root.TryGetProperty("uploader", out var uploaderProp))
                metadata.Uploader = uploaderProp.GetString();

            if (root.TryGetProperty("duration", out var durationProp))
                metadata.Duration = TimeSpan.FromSeconds(durationProp.GetDouble());

            if (root.TryGetProperty("description", out var descProp))
                metadata.Description = descProp.GetString();

            if (root.TryGetProperty("view_count", out var viewsProp))
                metadata.ViewCount = viewsProp.GetInt64();

            if (root.TryGetProperty("like_count", out var likesProp))
                metadata.LikeCount = likesProp.GetInt64();

            if (root.TryGetProperty("upload_date", out var uploadProp))
            {
                if (DateTime.TryParseExact(uploadProp.GetString(), "yyyyMMdd", null, DateTimeStyles.None, out var uploadDate))
                    metadata.UploadDate = uploadDate;
            }

            if (root.TryGetProperty("thumbnail", out var thumbProp))
                metadata.ThumbnailUrl = thumbProp.GetString();

            // Extract music-specific metadata
            ExtractMusicMetadata(root, metadata);

            // Store raw metadata
            foreach (var prop in root.EnumerateObject())
            {
                try
                {
                    metadata.RawMetadata[prop.Name] = prop.Value.Clone();
                }
                catch
                {
                    // Skip properties that can't be cloned
                }
            }

            return metadata;
        }
        catch (Exception ex)
        {
            _progressReporter.ReportError($"Failed to load metadata from info.json: {ex.Message}");
            return null;
        }
    }

    private void ExtractMusicMetadata(JsonElement root, VideoMetadata metadata)
    {
        // Try to extract artist from various fields
        if (root.TryGetProperty("artist", out var artistProp))
        {
            metadata.Artist = artistProp.GetString();
        }
        else if (root.TryGetProperty("creator", out var creatorProp))
        {
            metadata.Artist = creatorProp.GetString();
        }
        else if (root.TryGetProperty("uploader", out var uploaderProp))
        {
            // Use uploader as artist if no specific artist field
            var uploader = uploaderProp.GetString();
            if (!string.IsNullOrEmpty(uploader) &&
                !uploader.Contains("VEVO", StringComparison.OrdinalIgnoreCase) &&
                !uploader.Contains("Records", StringComparison.OrdinalIgnoreCase))
            {
                metadata.Artist = uploader;
            }
        }

        // Try to extract track/song title
        if (root.TryGetProperty("track", out var trackProp))
        {
            metadata.Track = trackProp.GetString();
        }
        else if (root.TryGetProperty("title", out var titleProp))
        {
            var title = titleProp.GetString();
            // Try to parse "Artist - Song" format
            if (!string.IsNullOrEmpty(title) && title.Contains(" - "))
            {
                var parts = title.Split(" - ", 2);
                if (parts.Length == 2)
                {
                    if (string.IsNullOrEmpty(metadata.Artist))
                        metadata.Artist = parts[0].Trim();
                    metadata.Track = parts[1].Trim();
                }
            }
            else
            {
                metadata.Track = title;
            }
        }

        // Try to extract album
        if (root.TryGetProperty("album", out var albumProp))
            metadata.Album = albumProp.GetString();

        // Try to extract genre
        if (root.TryGetProperty("genre", out var genreProp))
            metadata.Genre = genreProp.GetString();

        // Try to extract year from upload date or title
        if (root.TryGetProperty("release_year", out var yearProp))
        {
            metadata.Year = yearProp.GetInt32();
        }
        else if (metadata.UploadDate.HasValue)
        {
            metadata.Year = metadata.UploadDate.Value.Year;
        }
    }

    public async Task<ExtractionResult> ExtractAudioWithMetadataAndRetryAsync(
        string youtubeUrl,
        string outputPath,
        AudioFormat format = AudioFormat.FLAC)
    {
        ExtractionResult? lastResult = null;

        for (int attempt = 1; attempt <= _config.MaxRetries; attempt++)
        {
            try
            {
                var result = await ExtractAudioWithMetadataAsync(youtubeUrl, outputPath, format);
                if (result.Success)
                {
                    return result;
                }

                lastResult = result;

                if (attempt < _config.MaxRetries)
                {
                    var backoffDelay = TimeSpan.FromSeconds(Math.Pow(2, attempt) * 30);
                    _progressReporter.ReportInfo($"Retrying in {backoffDelay.TotalMinutes:F1} minutes...");
                    await CountdownDelayAsync((int)backoffDelay.TotalMilliseconds, "Retry Delay", CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                lastResult = new ExtractionResult
                {
                    Url = youtubeUrl,
                    Success = false,
                    Error = $"{ex.GetType().Name}: {ex.Message}",
                    StartTime = DateTime.Now,
                    EndTime = DateTime.Now
                };

                if (attempt < _config.MaxRetries)
                {
                    var backoffDelay = TimeSpan.FromSeconds(Math.Pow(2, attempt) * 30);
                    _progressReporter.ReportInfo($"Retrying in {backoffDelay.TotalMinutes:F1} minutes...");
                    await CountdownDelayAsync((int)backoffDelay.TotalMilliseconds, "Retry Delay", CancellationToken.None);
                }
            }
        }

        return lastResult ?? new ExtractionResult
        {
            Url = youtubeUrl,
            Success = false,
            Error = "All retry attempts failed",
            StartTime = DateTime.Now,
            EndTime = DateTime.Now
        };
    }

    private async Task<string> GetWorkingCookieArgumentAsync()
    {
        // Try manual cookies file first
        var cookiesPath = string.Empty;
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "youtube_cookies.txt");
        if (System.IO.File.Exists(cookiesPath))
        {
            _progressReporter.ReportInfo($"Using manual cookies file: {cookiesPath}");
            return $"--cookies \"{cookiesPath}\"";
        }

        // Try different browsers in order of preference
        var browserOptions = new[] { "chrome", "firefox", "edge", "safari" };

        foreach (var browser in browserOptions)
        {
            _progressReporter.ReportInfo($"Testing {browser} cookies...");

            var cookieArg = $"--cookies-from-browser {browser}";

            // Test this browser's cookies with a simple command
            if (await TestCookieArgumentAsync(cookieArg))
            {
                _progressReporter.ReportInfo($"✅ {browser} cookies work!");
                return cookieArg;
            }
            else
            {
                _progressReporter.ReportInfo($"❌ {browser} cookies failed");
            }
        }

        _progressReporter.ReportInfo("⚠️ No browser cookies work, falling back to no-cookies mode");
        return "--no-cookies";
    }

    private async Task<bool> TestCookieArgumentAsync(string cookieArg)
    {
        try
        {
            // Test with a simple yt-dlp command that just gets video info
            var testArgs = $"{cookieArg} --dump-json --no-warnings \"https://www.youtube.com/watch?v=dQw4w9WgXcQ\""; // Rick Roll as test video

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _ytDlpPath,
                    Arguments = testArgs,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();

            // Give it 30 seconds to test
            if (!process.WaitForExit(30000))
            {
                process.Kill();
                return false;
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();

            // If exit code is 0 and we got JSON output, cookies work
            if (process.ExitCode == 0 && !string.IsNullOrEmpty(output) && output.Contains("\"title\""))
            {
                return true;
            }

            // Check if the error indicates bot detection
            if (error.Contains("Sign in to confirm") || error.Contains("not a bot"))
            {
                return false; // This browser's cookies don't help with bot detection
            }

            // If it's some other error, the cookies might still be valid
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _progressReporter.ReportError($"Error testing {cookieArg}: {ex.Message}");
            return false;
        }
    }

    private async Task MonitorProcessActivityAsync(Process process)
    {
        var lastActivityTime = DateTime.Now;
        var lastCpuTime = TimeSpan.Zero;
        var stuckThreshold = TimeSpan.FromMinutes(5); // Consider stuck after 5 minutes of no activity

        try
        {
            if (process.HasExited)
                return;
            lastCpuTime = process.TotalProcessorTime;
        }
        catch
        {
            return; // Process might have exited or be inaccessible
        }

        while (!process.HasExited)
        {
            await Task.Delay(10000); // Check every 10 seconds

            try
            {
                // Double-check if process has exited before accessing properties
                if (process.HasExited)
                    return;

                var currentCpuTime = process.TotalProcessorTime;
                var cpuDelta = currentCpuTime - lastCpuTime;

                // If CPU time increased significantly, process is active
                if (cpuDelta.TotalMilliseconds > 50)
                {
                    lastActivityTime = DateTime.Now;
                    lastCpuTime = currentCpuTime;
                    // Remove this line to reduce noise: _progressReporter.ReportInfo($"Process active - CPU: {cpuDelta.TotalMilliseconds:F0}ms");
                }
                else
                {
                    // Check if we've been inactive too long
                    var inactiveTime = DateTime.Now - lastActivityTime;
                    if (inactiveTime > stuckThreshold)
                    {
                        _progressReporter.ReportError($"Process inactive for {inactiveTime.TotalMinutes:F1} minutes");
                        return; // This will cause the task to complete, triggering the stuck detection
                    }
                    // Remove this line to reduce noise: _progressReporter.ReportInfo($"Process idle for {inactiveTime.TotalMinutes:F1} minutes");
                }
            }
            catch (InvalidOperationException)
            {
                // Process has exited - this is normal, just return
                return;
            }
            catch (Exception ex)
            {
                _progressReporter.ReportError($"Activity monitoring error: {ex.Message}");
                return;
            }
        }
    }

    // Add this new method for file deletion
    private async Task DeleteExistingFilesAsync(string outputDir, string baseFileName)
    {
        try
        {
            if (!Directory.Exists(outputDir))
                return;

            var audioExtensions = new[] { ".flac", ".mp3", ".wav", ".aac", ".m4a", ".opus" };
            var filesToDelete = new List<string>();

            // Find all audio files that match our base name
            var allFiles = Directory.GetFiles(outputDir, "*.*");

            foreach (var file in allFiles)
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                var extension = Path.GetExtension(file).ToLower();

                // Check if this file matches our target name and is an audio file
                if (fileName.Equals(baseFileName, StringComparison.OrdinalIgnoreCase) &&
                    audioExtensions.Contains(extension))
                {
                    filesToDelete.Add(file);
                }
            }

            // Delete matching files
            foreach (var file in filesToDelete)
            {
                try
                {
                    var fileInfo = new FileInfo(file);
                    var fileSizeMB = fileInfo.Length / (1024.0 * 1024.0);

                    _progressReporter.ReportInfo($"Deleting existing file: {Path.GetFileName(file)} ({fileSizeMB:F1} MB)");

                    System.IO.File.Delete(file);

                    _progressReporter.ReportInfo($"✅ Deleted: {Path.GetFileName(file)}");
                }
                catch (Exception ex)
                {
                    _progressReporter.ReportError($"Failed to delete {Path.GetFileName(file)}: {ex.Message}");
                    // Continue with other files - don't fail the entire process
                }
            }

            if (filesToDelete.Count == 0)
            {
                _progressReporter.ReportInfo("No existing files to delete");
            }
            else
            {
                // Small delay to ensure file system updates
                await Task.Delay(1000);
            }
        }
        catch (Exception ex)
        {
            _progressReporter.ReportError($"Error during file cleanup: {ex.Message}");
            // Don't throw - continue with extraction even if cleanup fails
        }
    }

    private Dictionary<string, (DateTime lastWrite, long size)> GetFileInfoSnapshot(string directory)
    {
        var snapshot = new Dictionary<string, (DateTime lastWrite, long size)>();

        try
        {
            if (!Directory.Exists(directory))
                return snapshot;

            var files = Directory.GetFiles(directory, "*.*");
            foreach (var file in files)
            {
                try
                {
                    var fileInfo = new FileInfo(file);
                    snapshot[file] = (fileInfo.LastWriteTime, fileInfo.Length);
                }
                catch (Exception ex)
                {
                    _progressReporter.ReportError($"Error getting file info for {file}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _progressReporter.ReportError($"Error getting directory snapshot: {ex.Message}");
        }

        return snapshot;
    }

    private async Task<string?> FindCreatedOrUpdatedFileAsync(string outputDir, string baseFileName, Dictionary<string, (DateTime lastWrite, long size)> fileInfoBefore)
    {
        try
        {
            if (!Directory.Exists(outputDir))
            {
                _progressReporter.ReportError($"Output directory doesn't exist: {outputDir}");
                return null;
            }

            // Get current files
            var filesAfter = Directory.GetFiles(outputDir, "*.*");
            _progressReporter.ReportInfo($"Found {filesAfter.Length} files in output directory after extraction");

            // Look for new or updated files
            var candidateFiles = new List<(string path, bool isNew, DateTime lastWrite, long size, string reason)>();

            foreach (var file in filesAfter)
            {
                try
                {
                    var fileInfo = new FileInfo(file);
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    var extension = Path.GetExtension(file).ToLower();

                    // Check if it's an audio file
                    var audioExtensions = new[] { ".flac", ".mp3", ".wav", ".aac", ".m4a", ".opus" };
                    if (!audioExtensions.Contains(extension))
                        continue;

                    // Check if this could be our target file (more flexible matching)
                    var isMatch = fileName.Contains(baseFileName, StringComparison.OrdinalIgnoreCase) ||
                                 baseFileName.Contains(fileName, StringComparison.OrdinalIgnoreCase) ||
                                 fileName.Equals(baseFileName, StringComparison.OrdinalIgnoreCase);

                    if (!isMatch)
                    {
                        // Also check if it's a recently created audio file (within last 5 minutes)
                        var isRecent = DateTime.Now - fileInfo.CreationTime < TimeSpan.FromMinutes(5) ||
                                      DateTime.Now - fileInfo.LastWriteTime < TimeSpan.FromMinutes(5);

                        if (isRecent)
                        {
                            isMatch = true;
                            _progressReporter.ReportInfo($"Found recent audio file that might be our target: {Path.GetFileName(file)}");
                        }
                    }

                    if (!isMatch)
                        continue;

                    if (fileInfoBefore.TryGetValue(file, out var beforeInfo))
                    {
                        // File existed before - check if it was updated
                        if (fileInfo.LastWriteTime > beforeInfo.lastWrite || fileInfo.Length != beforeInfo.size)
                        {
                            candidateFiles.Add((file, false, fileInfo.LastWriteTime, fileInfo.Length, "updated existing file"));
                            _progressReporter.ReportInfo($"Found updated file: {Path.GetFileName(file)}");
                        }
                    }
                    else
                    {
                        // New file
                        candidateFiles.Add((file, true, fileInfo.LastWriteTime, fileInfo.Length, "new file"));
                        _progressReporter.ReportInfo($"Found new file: {Path.GetFileName(file)}");
                    }
                }
                catch (Exception ex)
                {
                    _progressReporter.ReportError($"Error checking file {file}: {ex.Message}");
                }
            }

            if (candidateFiles.Count == 0)
            {
                _progressReporter.ReportError("No new or updated audio files found");

                // List all audio files in directory for debugging
                var allAudioFiles = filesAfter.Where(f =>
                {
                    var ext = Path.GetExtension(f).ToLower();
                    return new[] { ".flac", ".mp3", ".wav", ".aac", ".m4a", ".opus" }.Contains(ext);
                }).ToArray();

                if (allAudioFiles.Length > 0)
                {
                    _progressReporter.ReportInfo($"Found {allAudioFiles.Length} audio files in directory:");
                    foreach (var audioFile in allAudioFiles.Take(10))
                    {
                        var info = new FileInfo(audioFile);
                        _progressReporter.ReportInfo($"  - {Path.GetFileName(audioFile)} ({info.Length / (1024.0 * 1024.0):F1} MB, created: {info.CreationTime:yyyy-MM-dd HH:mm:ss})");
                    }
                }

                return null;
            }

            // Prefer new files over updated files, then by most recent, then by largest size
            var bestFile = candidateFiles
                .OrderByDescending(f => f.isNew)
                .ThenByDescending(f => f.lastWrite)
                .ThenByDescending(f => f.size)
                .First();

            _progressReporter.ReportInfo($"Selected best candidate: {Path.GetFileName(bestFile.path)} ({bestFile.reason})");
            return bestFile.path;
        }
        catch (Exception ex)
        {
            _progressReporter.ReportError($"Error finding created file: {ex.Message}");
            return null;
        }
    }

    private async Task DebugMissingFileAsync(string outputDir, string baseFileName, Dictionary<string, (DateTime lastWrite, long size)> fileInfoBefore)
    {
        try
        {
            _progressReporter.ReportError("🔍 DEBUGGING: File not found after extraction");

            if (!Directory.Exists(outputDir))
            {
                _progressReporter.ReportError($"❌ Output directory doesn't exist: {outputDir}");
                return;
            }

            var filesAfter = Directory.GetFiles(outputDir, "*.*");

            _progressReporter.ReportInfo($"📁 Output directory: {outputDir}");
            _progressReporter.ReportInfo($"📊 Files before: {fileInfoBefore.Count}");
            _progressReporter.ReportInfo($"📊 Files after: {filesAfter.Length}");

            // Show all files with the base name
            var matchingFiles = filesAfter.Where(f =>
                Path.GetFileNameWithoutExtension(f).Contains(baseFileName, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (matchingFiles.Length > 0)
            {
                _progressReporter.ReportInfo("🔍 Files matching base name:");
                foreach (var file in matchingFiles.Take(5))
                {
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        var status = fileInfoBefore.ContainsKey(file) ? "existed before" : "NEW";
                        _progressReporter.ReportInfo($"   📄 {Path.GetFileName(file)} ({fileInfo.Length / 1024.0:F1} KB) - {status}");
                    }
                    catch (Exception ex)
                    {
                        _progressReporter.ReportError($"   Error reading {file}: {ex.Message}");
                    }
                }
            }
            else
            {
                _progressReporter.ReportError($"❌ No files found matching '{baseFileName}'");

                // Show all files in directory
                _progressReporter.ReportInfo("📁 All files in directory:");
                foreach (var file in filesAfter.Take(10))
                {
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        _progressReporter.ReportInfo($"   📄 {Path.GetFileName(file)} ({fileInfo.Length / 1024.0:F1} KB)");
                    }
                    catch (Exception ex)
                    {
                        _progressReporter.ReportError($"   Error reading {file}: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _progressReporter.ReportError($"Error during debugging: {ex.Message}");
        }
    }

    private async Task MonitorProcessOutputAsync(Process process)
    {
        try
        {
            var outputTask = Task.Run(async () =>
            {
                try
                {
                    while (!process.HasExited)
                    {
                        var line = await process.StandardOutput.ReadLineAsync();
                        if (line != null)
                        {
                            // DEBUG: Log all output lines to see what we're getting
                            _progressReporter.ReportInfo($"yt-dlp output: {line}");

                            // Parse download progress - try multiple patterns
                            if (line.Contains("[download]"))
                            {
                                double? extractedPercent = null;
                                string speed = "";
                                string eta = "";

                                // Pattern 1: Standard percentage
                                var percentMatch = System.Text.RegularExpressions.Regex.Match(line, @"(\d+\.?\d*)%");
                                if (percentMatch.Success && double.TryParse(percentMatch.Groups[1].Value, out var percentValue))
                                {
                                    extractedPercent = percentValue;

                                    var speedMatch = System.Text.RegularExpressions.Regex.Match(line, @"(\d+\.?\d*\w+/s)");
                                    var etaMatch = System.Text.RegularExpressions.Regex.Match(line, @"ETA (\d+:\d+)");

                                    speed = speedMatch.Success ? speedMatch.Groups[1].Value : "";
                                    eta = etaMatch.Success ? etaMatch.Groups[1].Value : "";
                                }
                                // Pattern 2: File size progress
                                else if (line.Contains("MiB") || line.Contains("MB") || line.Contains("KiB") || line.Contains("KB"))
                                {
                                    // Try to extract progress from file size info
                                    var sizeMatch = System.Text.RegularExpressions.Regex.Match(line, @"(\d+\.?\d*)\s*[MK]i?B\s*of\s*(\d+\.?\d*)\s*[MK]i?B");
                                    if (sizeMatch.Success)
                                    {
                                        if (double.TryParse(sizeMatch.Groups[1].Value, out var downloaded) &&
                                            double.TryParse(sizeMatch.Groups[2].Value, out var total) && total > 0)
                                        {
                                            extractedPercent = (downloaded / total) * 100;
                                        }
                                    }
                                }

                                // Report progress if we found any
                                if (extractedPercent.HasValue && _progressReporter is EnhancedConsoleProgressReporter enhancedReporter)
                                {
                                    enhancedReporter.ReportDownloadProgress(extractedPercent.Value, speed, eta);
                                }
                            }
                            else if (line.Contains("[ExtractAudio]"))
                            {
                                if (_progressReporter is EnhancedConsoleProgressReporter enhancedReporter)
                                {
                                    enhancedReporter.ReportExtractionProgress("Converting to audio...");
                                }
                            }
                            else if (line.Contains("[ffmpeg]"))
                            {
                                if (_progressReporter is EnhancedConsoleProgressReporter enhancedReporter)
                                {
                                    enhancedReporter.ReportExtractionProgress("Processing audio...");
                                }
                            }
                            else if (line.Contains("ERROR"))
                            {
                                _progressReporter.ReportError($"yt-dlp: {line}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _progressReporter.ReportError($"Output monitoring error: {ex.Message}");
                }
            });

            var errorTask = Task.Run(async () =>
            {
                try
                {
                    while (!process.HasExited)
                    {
                        var line = await process.StandardError.ReadLineAsync();
                        if (line != null)
                        {
                            // DEBUG: Log error output too
                            if (!line.Contains("[debug]"))
                            {
                                _progressReporter.ReportError($"yt-dlp stderr: {line}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _progressReporter.ReportError($"Error monitoring error: {ex.Message}");
                }
            });

            await Task.WhenAll(outputTask, errorTask);
        }
        catch (Exception ex)
        {
            _progressReporter.ReportError($"Process monitoring failed: {ex.Message}");
        }
    }

    public async IAsyncEnumerable<ExtractionResult> ExtractBatchAsync(
        IEnumerable<string> videos,
        string outputDirectory,
        AudioFormat format = AudioFormat.FLAC,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var videoList = videos.ToArray();
        _progressReporter.ReportInfo($"Processing {videoList.Length} personal YouTube videos");
        _progressReporter.ReportInfo($"Output format: {format}");
        _progressReporter.ReportInfo($"Output directory: {outputDirectory}");

        // Start batch tracking
        var enhancedReporter = _progressReporter as EnhancedConsoleProgressReporter;
        enhancedReporter?.StartBatch(videoList.Length);

        var shuffledVideos = videoList.OrderBy(_ => _random.Next()).ToArray();

        for (int i = 0; i < shuffledVideos.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var url = shuffledVideos[i];
            var result = new ExtractionResult
            {
                Url = url,
                StartTime = DateTime.Now
            };

            // Using Uri and query parsing
            var uri = new Uri(url);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            string? fallbackname = query?["v"];

            try
            {
                enhancedReporter?.StartNewVideo(i + 1, url);

                var extension = format.ToString().ToLower();
                var preliminaryFileName = $"{SanitizeFileName(fallbackname ?? "unknown")}.{extension}";
                var preliminaryOutputPath = Path.Combine(outputDirectory, preliminaryFileName);

                // Single call that gets both metadata and audio
                var extractionResult = await ExtractAudioWithMetadataAndRetryAsync(url, preliminaryOutputPath, format);

                // Update the result with the extraction results
                result.Success = extractionResult.Success;
                result.Error = extractionResult.Error;
                result.OutputPath = extractionResult.OutputPath;
                result.FileSizeBytes = extractionResult.FileSizeBytes;
                result.Metadata = extractionResult.Metadata;
                result.Name = extractionResult.Name ?? CreateFileNameFromMetadata(result.Metadata, fallbackname, format);

                if (result.Metadata != null)
                {
                    _progressReporter.ReportInfo($"📋 Title: {result.Metadata.Title}");
                    _progressReporter.ReportInfo($"🎤 Artist: {result.Metadata.Artist ?? "Unknown"}");
                    _progressReporter.ReportInfo($"⏱️ Duration: {result.Metadata.Duration?.ToString(@"mm\:ss") ?? "Unknown"}");
                }

                enhancedReporter?.CompleteVideo(Path.GetFileName(result.OutputPath ?? result.Name));
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = $"{ex.GetType().Name}: {ex.Message}";

                // Log the full exception for debugging
                Console.WriteLine(); // Blank line
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"❌ EXCEPTION during extraction of {result.Name}:");
                Console.WriteLine($"   Type: {ex.GetType().Name}");
                Console.WriteLine($"   Message: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"   Inner Exception: {ex.InnerException.Message}");
                }
                Console.ResetColor();
                Console.WriteLine(); // Blank line
                Console.Out.Flush();
            }
            finally
            {
                result.EndTime = DateTime.Now;
            }

            yield return result;

            // Clean transition between videos
            if (i < shuffledVideos.Length - 1)
            {
                // Stop any progress updates during the pause
                enhancedReporter?.StopProgressUpdates();

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"🔄 Completed {i + 1}/{shuffledVideos.Length} videos");
                Console.WriteLine($"📝 Next: {shuffledVideos[i + 1]}");
                Console.ResetColor();
            }
        }

        // Complete batch tracking
        enhancedReporter?.CompleteBatch();
    }

    private string CreateFileNameFromMetadata(VideoMetadata? metadata, string? fallbackName, AudioFormat format)
    {
        if (metadata == null)
            return $"{SanitizeFileName(fallbackName ?? "unknown")}";

        var artist = metadata.Artist ?? "Unknown Artist";
        var track =  metadata.Title ?? fallbackName ?? "unknown";

        var fileName = $"{track}";
        return SanitizeFileName(fileName) ?? "unknown";
    }

    private async Task CountdownDelayAsync(int delayMs, string operation, CancellationToken cancellationToken)
    {
        var totalSeconds = delayMs / 1000;
        var updateInterval = Math.Max(1000, delayMs / 100); // Update every 1% or 1 second, whichever is larger

        for (int elapsed = 0; elapsed < delayMs; elapsed += updateInterval)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remainingMs = delayMs - elapsed;
            var remainingSeconds = remainingMs / 1000;
            var percentage = (double)elapsed / delayMs * 100;

            _progressReporter.ReportProgress(
                $"{operation}: {remainingSeconds}s remaining",
                percentage);

            await Task.Delay(Math.Min(updateInterval, remainingMs), cancellationToken);
        }

        _progressReporter.ReportProgress($"{operation}: Complete", 100);
    }

    public void Dispose()
    {
        _argumentsCache?.Clear();
        _httpClient?.Dispose();
        if (_progressReporter is IDisposable disposableReporter)
        {
            disposableReporter.Dispose();
        }
    }
}
