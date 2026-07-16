using TaskTracker.Core.Storage;

namespace TaskTracker.Services
{
    public interface ISettingsService
    {
        AppSettings Settings { get; }

        string SettingsFolder { get; }

        void Save();
    }
}
