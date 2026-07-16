using TaskTracker.Core.Storage;

namespace TaskTracker.Services
{
    public class SettingsService : ISettingsService
    {
        private readonly SettingsStore _store = new();

        public AppSettings Settings { get; }

        public string SettingsFolder => _store.BaseDirectory;

        public SettingsService()
        {
            Settings = _store.Load();
        }

        public void Save() => _store.Save(Settings);
    }
}
