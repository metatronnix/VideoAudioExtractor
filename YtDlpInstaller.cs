using System.Diagnostics;
// yt-dlp installer and detector
public static class YtDlpInstaller
{
    public static async Task ConfigureYtDlpAsync()
    {
        //Console.WriteLine("🔍 Locating yt-dlp...");

        // Try to find yt-dlp in common locations
        var ytDlpPath = FindYtDlpInPath();

        if (string.IsNullOrEmpty(ytDlpPath))
        {
            Console.WriteLine("❌ yt-dlp not found in PATH directories");
            Console.WriteLine("🔧 Searching common installation locations...");
            ytDlpPath = await FindYtDlpManuallyAsync();
        }

        if (string.IsNullOrEmpty(ytDlpPath))
        {
            Console.WriteLine("❌ Could not locate yt-dlp executable");
            Console.WriteLine("🧪 Trying shell execution as fallback...");

            // Test if yt-dlp works via shell (without output redirection)
            if (await TestYtDlpViaShellAsync())
            {
                Console.WriteLine("✅ yt-dlp works via shell execution");
                Console.WriteLine("⚠️  Will use shell execution mode (limited output capture)");
            }
            else
            {
                ShowManualInstructions();
                Environment.Exit(1);
            }
        }
        else
        {
            //Console.WriteLine($"✅ Found yt-dlp at: {ytDlpPath}");

            // Test it with full path
            if (!await TestYtDlpAsync(ytDlpPath))
            {
                Console.WriteLine("❌ yt-dlp test failed");
                Environment.Exit(1);
            }
        }
    }

    public static string? FindYtDlpInPath()
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVariable))
        {
            Console.WriteLine("⚠️  PATH environment variable is empty");
            return null;
        }

        var pathDirectories = pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var executableNames = new[] { "yt-dlp.exe", "yt-dlp" };

        //Console.WriteLine($"🔍 Searching {pathDirectories.Length} PATH directories...");
        // Filter to only directories that contain the executables
        var directoriesWithExecutables = pathDirectories
            .Where(directory =>
            {
                try
                {
                    return Directory.Exists(directory) &&
                           executableNames.Any(execName => System.IO.File.Exists(Path.Combine(directory, execName)));
                }
                catch (Exception)
                {
                    return false;
                }
            })
            .ToArray();

        foreach (var directory in directoriesWithExecutables)
        {
            try
            {
                if (!Directory.Exists(directory))
                    continue;

                foreach (var execName in executableNames)
                {
                    var fullPath = Path.Combine(directory, execName);
                    if (System.IO.File.Exists(fullPath))
                    {
                        //Console.WriteLine($"   ✅ Found: {fullPath}");
                        return fullPath;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ⚠️  Error checking {directory}: {ex.Message}");
            }
        }

        Console.WriteLine("   ❌ Not found in any PATH directory");
        return null;
    }

    public static async Task<string?> FindYtDlpManuallyAsync()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var searchLocations = OperatingSystem.IsWindows()
            ? new[]
            {
                @"C:\Program Files\yt-dlp",
                @"C:\Program Files (x86)\yt-dlp",
                @"C:\Tools\yt-dlp",
                @"C:\yt-dlp",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "yt-dlp"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "yt-dlp"),
                Path.Combine(home, "yt-dlp"),
                // Python Scripts directories
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python", "Python311", "Scripts"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python", "Python312", "Scripts"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python", "Python313", "Scripts"),
                @"C:\Python311\Scripts",
                @"C:\Python312\Scripts",
                @"C:\Python313\Scripts",
            }
            : new[]
            {
                "/opt/homebrew/bin",   // Homebrew on Apple Silicon
                "/usr/local/bin",      // Homebrew on Intel macOS / common Linux
                "/usr/bin",
                Path.Combine(home, ".local", "bin"),
                Path.Combine(home, "bin"),
            };

        var executableNames = new[] { "yt-dlp.exe", "yt-dlp" };

        foreach (var location in searchLocations)
        {
            Console.WriteLine($"🔍 Checking: {location}");

            if (!Directory.Exists(location))
            {
                Console.WriteLine($"   📁 Directory doesn't exist");
                continue;
            }

            foreach (var execName in executableNames)
            {
                var fullPath = Path.Combine(location, execName);
                if (System.IO.File.Exists(fullPath))
                {
                    //Console.WriteLine($"   ✅ Found: {fullPath}");
                    if (await TestYtDlpAsync(fullPath))
                    {
                        return fullPath;
                    }
                    else
                    {
                        Console.WriteLine($"   ❌ File exists but doesn't work");
                    }
                }
            }
        }

        return null;
    }

    private static async Task<bool> TestYtDlpAsync(string ytDlpPath)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ytDlpPath,
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(10000)) // 10 second timeout
            {
                process.Kill();
                Console.WriteLine("   ⏰ Test timed out");
                return false;
            }

            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
            {
                return true;
            }
            else
            {
                Console.WriteLine($"   ❌ Exit code: {process.ExitCode}");
                if (!string.IsNullOrEmpty(error))
                    Console.WriteLine($"   ❌ Error: {error.Trim()}");
                return false;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ❌ Test failed: {ex.Message}");
            return false;
        }
    }

    private static async Task<bool> TestYtDlpViaShellAsync()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "yt-dlp",
                    Arguments = "--version",
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                }
            };

            process.Start();

            if (!process.WaitForExit(10000)) // 10 second timeout
            {
                process.Kill();
                Console.WriteLine("   ⏰ Shell test timed out");
                return false;
            }

            Console.WriteLine($"   Exit code: {process.ExitCode}");
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ❌ Shell test failed: {ex.Message}");
            return false;
        }
    }

    private static void ShowManualInstructions()
    {
        Console.WriteLine("\n🔧 Manual Setup Instructions:");
        Console.WriteLine("=============================");
        if (OperatingSystem.IsWindows())
        {
            Console.WriteLine("yt-dlp was not found. Install it (e.g. `winget install yt-dlp` or `pip install yt-dlp`),");
            Console.WriteLine("then confirm it's on your PATH with:");
            Console.WriteLine("   where yt-dlp");
        }
        else
        {
            Console.WriteLine("yt-dlp was not found. Install it with Homebrew (macOS) or your package manager:");
            Console.WriteLine("   brew install yt-dlp ffmpeg");
            Console.WriteLine("then confirm it's on your PATH with:");
            Console.WriteLine("   which yt-dlp");
        }
    }

    // Synchronous version
    public static string? FindYtDlpPath()
    {
        return FindYtDlpInPath() ?? FindYtDlpManuallyAsync().GetAwaiter().GetResult();
    }
}
