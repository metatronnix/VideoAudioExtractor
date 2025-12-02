using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VideoAudioExtractor.Implementations
{
    // Enhanced Progress Reporter with progress bar and timing
    internal class EnhancedConsoleProgressReporter : IProgressReporter, IDisposable
    {
        private readonly object _lock = new object();
        private string _currentOperation = "";
        private string _currentVideoName = "";
        private int _currentVideoIndex = 0;
        private int _totalVideos = 0;
        private DateTime _batchStartTime = DateTime.Now;
        private readonly Stopwatch _totalStopwatch = Stopwatch.StartNew();
        protected Timer? _updateTimer;
        private bool _disposed = false;
        public bool _suppressOutput = false;

        // New field to track the console line where "Starting ..." was printed
        private int _lastVideoLine = -1;

        // Fields for download progress
        private double _currentVideoProgress = 0.0;
        private string _downloadSpeed = "";
        private string _downloadETA = "";
        private bool _isExtracting = false;
        private DateTime _videoStartTime = DateTime.Now;

        // Add this method to the EnhancedConsoleProgressReporter class:
        public void StopProgressUpdates()
        {
            lock (_lock)
            {
                _updateTimer?.Dispose();
                _updateTimer = null;
            }
        }

        public void RestartProgressUpdates()
        {
            lock (_lock)
            {
                _updateTimer?.Dispose();
                _updateTimer = new Timer(TimerCallback, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
            }
        }

        public void ReportProgress(string message, double? percentage = null)
        {
            lock (_lock)
            {
                _currentOperation = message;
                // Don't call UpdateProgressDisplay here - let timer handle it
            }
        }

        public void ReportDownloadProgress(double percentage, string speed = "", string eta = "")
        {
            lock (_lock)
            {
                _currentVideoProgress = Math.Max(_currentVideoProgress, percentage); // Ensure progress only goes forward
                _downloadSpeed = speed;
                _downloadETA = eta;
                _isExtracting = false;
            }
        }

        public void ReportExtractionProgress(string stage)
        {
            lock (_lock)
            {
                _isExtracting = true;
                _currentOperation = stage;
                // Set progress to 90-99% during extraction phases, but don't go backwards
                var extractionProgress = stage switch
                {
                    var s when s.Contains("Converting") => 90.0,
                    var s when s.Contains("Processing") => 95.0,
                    var s when s.Contains("ffmpeg") => 98.0,
                    _ => 90.0
                };
                _currentVideoProgress = Math.Max(_currentVideoProgress, extractionProgress);
            }
        }

        public void ReportError(string error)
        {
            lock (_lock)
            {
                ClearProgressLine();
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"❌ {error}");
                Console.ResetColor();
                Console.Out.Flush();
            }
        }

        public void ReportCompletion(string message)
        {
            lock (_lock)
            {
                if (!_suppressOutput)
                {
                    ClearProgressLine();
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"✅ {message}");
                    Console.ResetColor();
                    Console.Out.Flush();
                }
            }
        }

        public void ReportInfo(string info)
        {
            lock (_lock)
            {
                if (!_suppressOutput)
                {
                    ClearProgressLine();
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"ℹ️  {info}");
                    Console.ResetColor();
                    Console.Out.Flush();
                }
            }
        }

        public void StartBatch(int totalVideos)
        {
            lock (_lock)
            {
                _totalVideos = totalVideos;
                _currentVideoIndex = 0;
                _batchStartTime = DateTime.Now;
                _totalStopwatch.Restart();
                _suppressOutput = false;

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"🚀 Starting batch processing: {totalVideos} videos");
                Console.ResetColor();
                Console.WriteLine(); // Leave space for progress bar

                _updateTimer?.Dispose();
                _updateTimer = new Timer(TimerCallback, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
            }
        }

        public void StartNewVideo(int videoIndex, string videoName)
        {
            lock (_lock)
            {
                StopProgressUpdates();

                _currentVideoIndex = videoIndex;
                _currentVideoName = videoName;
                _currentOperation = $"Starting: {videoName}";

                _currentVideoProgress = 0.0;
                _downloadSpeed = "";
                _downloadETA = "";
                _isExtracting = false;
                _videoStartTime = DateTime.Now;

                ClearProgressLine();

                // Save the line number where we print this
                _lastVideoLine = Console.CursorTop;

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"🎵 [{videoIndex}/{_totalVideos}] Starting: {videoName}");
                Console.ResetColor();

                _suppressOutput = true;

                Task.Delay(100).ContinueWith(_ => {
                    if (!_disposed) RestartProgressUpdates();
                });
            }
        }

        // New overload: complete with final file name, replacing the same line
        public void CompleteVideo(string finalFileName)
        {
            lock (_lock)
            {
                StopProgressUpdates();
                _currentVideoProgress = 100.0;
                _suppressOutput = false;

                if (_lastVideoLine >= 0)
                {
                    try
                    {
                        // Move cursor back to the "Starting" line
                        Console.SetCursorPosition(0, _lastVideoLine);

                        // Overwrite the line with spaces
                        Console.Write(new string(' ', Console.WindowWidth - 1));
                        Console.SetCursorPosition(0, _lastVideoLine);

                        // Print the completed line in green
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"✅ Completed: {finalFileName}");
                        Console.ResetColor();
                    }
                    catch
                    {
                        // Fallback: just print a new line if cursor ops fail
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"✅ Completed: {finalFileName}");
                        Console.ResetColor();
                    }
                }
                else
                {
                    // Fallback if we never tracked the line
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"✅ Completed: {finalFileName}");
                    Console.ResetColor();
                }

                Console.Out.Flush();
            }
        }

        public void CompleteBatch()
        {
            lock (_lock)
            {
                _updateTimer?.Dispose();
                _updateTimer = null;
                _suppressOutput = false;

                ClearProgressLine();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"🎉 Batch completed! Total time: {_totalStopwatch.Elapsed:hh\\:mm\\:ss}");
                Console.ResetColor();
            }
        }

        private void TimerCallback(object? state)
        {
            if (!_disposed && _updateTimer != null)
            {
                UpdateProgressDisplay();
            }
        }

        public void ClearProgressLine()
        {
            try
            {
                Console.Write("\r");
                Console.Write(new string(' ', Math.Min(Console.WindowWidth - 1, 120)));
                Console.Write("\r");
                Console.Out.Flush();
            }
            catch
            {
                // Ignore console errors
            }
        }

        private void UpdateProgressDisplay()
        {
            if (_totalVideos == 0) return;

            lock (_lock)
            {
                try
                {
                    var totalElapsed = _totalStopwatch.Elapsed;

                    var completedVideos = Math.Max(0, _currentVideoIndex - 1);
                    var overallProgress = (double)completedVideos / _totalVideos * 100;

                    var currentVideoProgress = _currentVideoProgress;

                    if (_currentVideoIndex > 0 && _currentVideoIndex <= _totalVideos)
                    {
                        var videoElapsed = DateTime.Now - _videoStartTime;

                        if (_currentVideoProgress <= 0.0 && videoElapsed.TotalSeconds > 10)
                        {
                            var estimatedVideoTime = TimeSpan.FromMinutes(4);
                            var timeRatio = Math.Min(1.0, videoElapsed.TotalSeconds / estimatedVideoTime.TotalSeconds);
                            var simulatedProgress = 85.0 * (1.0 / (1.0 + Math.Exp(-6 * (timeRatio - 0.5))));
                            currentVideoProgress = Math.Max(0, Math.Min(85.0, simulatedProgress));
                        }

                        var currentVideoWeight = (1.0 / _totalVideos) * 100;
                        overallProgress += currentVideoWeight * (currentVideoProgress / 100.0);
                    }

                    overallProgress = Math.Min(100, overallProgress);

                    var heartbeat = ((int)(totalElapsed.TotalSeconds) % 4) switch
                    {
                        0 => "⠋",
                        1 => "⠙",
                        2 => "⠹",
                        3 => "⠸",
                        _ => "⠼"
                    };

                    string etaString = "";
                    if (completedVideos > 0)
                    {
                        var avgTimePerVideo = totalElapsed.TotalSeconds / Math.Max(1, completedVideos);
                        var remainingVideos = _totalVideos - completedVideos;

                        if (_currentVideoIndex <= _totalVideos && currentVideoProgress > 0)
                        {
                            var currentVideoRemainingTime = avgTimePerVideo * (1 - currentVideoProgress / 100.0);
                            var estimatedRemainingSeconds = (remainingVideos - 1) * avgTimePerVideo + currentVideoRemainingTime;
                            var eta = TimeSpan.FromSeconds(Math.Max(0, estimatedRemainingSeconds));
                            etaString = $" | ETA: {eta:hh\\:mm\\:ss}";
                        }
                        else if (remainingVideos > 0)
                        {
                            var estimatedRemainingSeconds = remainingVideos * avgTimePerVideo;
                            var eta = TimeSpan.FromSeconds(estimatedRemainingSeconds);
                            etaString = $" | ETA: {eta:hh\\:mm\\:ss}";
                        }
                    }

                    var overallProgressBar = CreateProgressBar(overallProgress);
                    var currentVideoProgressBar = CreateProgressBar(currentVideoProgress);

                    var statusLine = $"{heartbeat} {overallProgressBar} {overallProgress:F1}% | Videos: {completedVideos}/{_totalVideos} | Time: {totalElapsed:hh\\:mm\\:ss}{etaString}";

                    if (_currentVideoIndex > 0 && _currentVideoIndex <= _totalVideos)
                    {
                        var videoElapsed = DateTime.Now - _videoStartTime;
                        statusLine += $" | Current: {currentVideoProgressBar} {currentVideoProgress:F1}% ({videoElapsed:mm\\:ss})";

                        if (!string.IsNullOrEmpty(_downloadSpeed))
                        {
                            statusLine += $" @ {_downloadSpeed}";
                        }
                        if (_isExtracting)
                        {
                            statusLine += " (Extracting)";
                        }
                    }

                    if (statusLine.Length > Console.WindowWidth - 1)
                    {
                        statusLine = statusLine.Substring(0, Console.WindowWidth - 4) + "...";
                    }

                    Console.Write("\r" + new string(' ', Math.Min(Console.WindowWidth - 1, 120)) + "\r");
                    Console.Write(statusLine);
                    Console.Out.Flush();
                }
                catch
                {
                    // Ignore display errors
                }
            }
        }

        private string CreateProgressBar(double percentage)
        {
            const int barWidth = 20;
            var filled = (int)Math.Round(percentage / 100.0 * barWidth);
            var empty = barWidth - filled;

            var bar = new string('█', filled) + new string('░', empty);
            return $"[{bar}]";
        }

        public void StartNewOperation(string operationName)
        {
            ReportProgress(operationName);
        }

        public void ReportOverallProgress(int current, int total, string currentItem)
        {
            // handled by timer
        }

        public void Dispose()
        {
            _disposed = true;
            _updateTimer?.Dispose();
        }
    }
}
