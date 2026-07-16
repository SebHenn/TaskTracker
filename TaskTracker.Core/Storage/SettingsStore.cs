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

        public AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFilePath), CoreJson.Options)
                           ?? new AppSettings();
                }
            }
            catch (JsonException) { }
            catch (IOException) { }
            return new AppSettings();
        }

        public void Save(AppSettings settings)
        {
            Directory.CreateDirectory(BaseDirectory);
            var tmpPath = SettingsFilePath + ".tmp";
            File.WriteAllText(tmpPath, JsonSerializer.Serialize(settings, CoreJson.Options));
            File.Move(tmpPath, SettingsFilePath, overwrite: true);
        }
    }
}
