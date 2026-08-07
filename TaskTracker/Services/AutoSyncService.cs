using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Linq;
using System.Windows.Threading;
using TaskTracker.Core.GitHub;
using TaskTracker.Messages;

namespace TaskTracker.Services
{
    /// <summary>
    /// Optional background refresh: every 15 minutes, syncs all GitHub-linked,
    /// non-archived projects when enabled in settings. A failure is logged and
    /// broadcast as an <see cref="AutoSyncFailedMessage"/>, so the affected board can
    /// say so rather than just quietly stopping.
    /// </summary>
    public class AutoSyncService : IDisposable
    {
        private readonly IProjectsService _projectsService;
        private readonly ISettingsService _settingsService;
        private readonly GitHubSyncService _sync;
        private readonly IGitHubApiFactory _apiFactory;
        private readonly DispatcherTimer _timer;
        private bool _running;

        public AutoSyncService(IProjectsService projectsService, ISettingsService settingsService,
                               GitHubSyncService sync, IGitHubApiFactory apiFactory)
        {
            _projectsService = projectsService;
            _settingsService = settingsService;
            _sync = sync;
            _apiFactory = apiFactory;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
            _timer.Tick += async (_, _) => await TickAsync();
            _timer.Start();

            // One early sync shortly after startup (when enabled), so linked
            // boards are fresh without waiting for the first 15-minute tick.
            var startupTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            startupTimer.Tick += async (_, _) =>
            {
                startupTimer.Stop();
                await TickAsync();
            };
            startupTimer.Start();
        }

        private async System.Threading.Tasks.Task TickAsync()
        {
            if (_running || !_settingsService.Settings.AutoSyncEnabled)
                return;

            var token = TokenProtector.Unprotect(
                _settingsService.Settings.GitHubTokenProtected,
                _settingsService.Settings.GitHubTokenIsPlaintext);
            if (string.IsNullOrEmpty(token))
                return;

            _running = true;
            try
            {
                var api = _apiFactory.Create(token);
                foreach (var project in _projectsService.projectModels.Where(p => p.IsGitHubLinked && !p.IsArchived).ToList())
                {
                    try
                    {
                        var result = await _sync.SyncAsync(project, api);

                        // Only a background sync announces itself. A sync the user clicked
                        // reports into the board they are already looking at, and a balloon
                        // on top of that is just noise.
                        if (result.Imported > 0 && _settingsService.Settings.NotifyOnNewIssues)
                        {
                            WeakReferenceMessenger.Default.Send(
                                new NewIssuesImportedMessage(project.Id, project.Name, result.ImportedIssues));
                        }
                    }
                    catch (Exception ex)
                    {
                        // Logged and announced. Silence made a bad token or a renamed
                        // repository look exactly like "nothing changed", so a board could
                        // quietly stop updating for days.
                        Core.Storage.AppLog.Write("auto-sync", $"{project.Name}: {ex.Message}");
                        WeakReferenceMessenger.Default.Send(new AutoSyncFailedMessage(project.Id, ex.Message));
                    }
                }
            }
            finally
            {
                _running = false;
            }
        }

        public void Dispose() => _timer.Stop();
    }
}
