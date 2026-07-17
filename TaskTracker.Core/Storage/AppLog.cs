namespace TaskTracker.Core.Storage
{
    /// <summary>
    /// Minimal diagnostic log next to the save data, so silent failures
    /// (auto-sync, tray, file watcher) stay diagnosable. Rotates at ~1 MB.
    /// Logging must never throw.
    /// </summary>
    public static class AppLog
    {
        private const long MaxBytes = 1_000_000;
        private static readonly object Gate = new();

        public static string LogFilePath { get; set; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TaskTracker", "TaskTracker.log");

        public static void Write(string category, string message)
        {
            try
            {
                lock (Gate)
                {
                    var dir = Path.GetDirectoryName(LogFilePath);
                    if (dir != null)
                        Directory.CreateDirectory(dir);

                    if (File.Exists(LogFilePath) && new FileInfo(LogFilePath).Length > MaxBytes)
                        File.Move(LogFilePath, LogFilePath + ".old", overwrite: true);

                    File.AppendAllText(LogFilePath,
                        $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}Z [{category}] {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // Never let diagnostics take the app down.
            }
        }

        public static void Write(string category, Exception exception)
            => Write(category, exception.ToString());
    }
}
