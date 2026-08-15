using System.Text.Json;

namespace TaskTracker.Core.Storage
{
    public class SettingsStore
    {
        public string BaseDirectory { get; }
        public string SettingsFilePath { get; }

        public SettingsStore(string? baseDirectory = null)
        {
            BaseDirectory = baseDirectory
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TaskTracker");
            SettingsFilePath = Path.Combine(BaseDirectory, "Settings.json");
        }

        /// <summary>
        /// Reads settings, falling back to the backup and then to defaults. Never throws —
        /// unreadable settings must not stop the app from starting.
        /// </summary>
        public AppSettings Load()
            => TryRead(SettingsFilePath) ?? TryRead(SettingsFilePath + ".bak1") ?? new AppSettings();

        private static AppSettings? TryRead(string path)
        {
            try
            {
                if (File.Exists(path))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), CoreJson.Options);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                AppLog.Write("settings", ex);
            }
            return null;
        }

        /// <summary>
        /// Writes settings atomically, keeping one backup.
        ///
        /// Never throws: settings are saved as a side effect of toggling a checkbox, so a
        /// transient IO failure must not take down the caller mid-interaction. A lost
        /// preference is recoverable by setting it again; an unhandled exception out of a
        /// property setter is not. Failures go to the log.
        ///
        /// Deliberately no cross-process lock, unlike <see cref="ProjectStore"/>: only the
        /// desktop app writes this file and the MCP server only reads the token, so a lock
        /// would add a stale-lockfile failure mode without preventing any real conflict.
        /// </summary>
        public void Save(AppSettings settings)
        {
            try
            {
                Directory.CreateDirectory(BaseDirectory);
                var tmpPath = SettingsFilePath + ".tmp";
                File.WriteAllText(tmpPath, JsonSerializer.Serialize(settings, CoreJson.Options));

                if (File.Exists(SettingsFilePath))
                    File.Copy(SettingsFilePath, SettingsFilePath + ".bak1", overwrite: true);

                File.Move(tmpPath, SettingsFilePath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                AppLog.Write("settings", ex);
            }
        }
    }
}
