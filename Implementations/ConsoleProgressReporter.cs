namespace VideoAudioExtractor.Implementations
{
    // Original ConsoleProgressReporter for backward compatibility
    internal class ConsoleProgressReporter : IProgressReporter
    {
        public void ReportProgress(string message, double? percentage = null)
        {
            var prefix = percentage.HasValue ? $"[{percentage:F1}%] " : "";
            Console.WriteLine($"📊 {prefix}{message}");
            Console.Out.Flush();
        }

        public void ReportError(string error)
        {
            Console.WriteLine($"❌ {error}");
            Console.Out.Flush(); // ← ADD THIS
        }
        public void ReportCompletion(string message)
        {
            Console.WriteLine($"✅ {message}");
            Console.Out.Flush(); // ← ADD THIS
        }

        public void ReportInfo(string info)
        {
            Console.WriteLine($"ℹ️  {info}");
            Console.Out.Flush(); // ← ADD THIS
        }
    }
}