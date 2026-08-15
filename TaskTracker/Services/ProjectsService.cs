using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using TaskTracker.Core.Models;
using TaskTracker.Core.Services;
using TaskTracker.Core.Storage;
using TaskTracker.Messages;

namespace TaskTracker.Services
{
    public class ProjectsService : IProjectsService, IDisposable
    {
        private const int MaxRecentProjects = 5;

        private readonly ProjectStore _store;
        private readonly ChangeTracker _changeTracker = new();
        private readonly DispatcherTimer _saveTimer;
        private readonly DispatcherTimer _reloadTimer;
        private readonly FileSystemWatcher? _watcher;
        private readonly List<Guid> _recentIds = new();
        private readonly System.Threading.SemaphoreSlim _writeGate = new(1, 1);
        private bool _suppressChangeEvents;

        public ObservableCollection<ProjectModel> projectModels { get; }

        public ObservableCollection<ProjectModel> RecentProjects { get; } = new();

        public ProjectsService()
        {
            _store = new ProjectStore();
            var data = _store.Load();
            projectModels = data.Projects;
            _recentIds.AddRange(data.RecentProjectIds);
            RebuildRecentProjection();

            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveNow(); };

            _reloadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _reloadTimer.Tick += (_, _) => { _reloadTimer.Stop(); ReloadIfExternallyChanged(); };

            _changeTracker.Attach(projectModels);
            _changeTracker.Changed += OnDataChanged;

            try
            {
                Directory.CreateDirectory(_store.BaseDirectory);
                _watcher = new FileSystemWatcher(_store.BaseDirectory, Path.GetFileName(_store.SaveFilePath))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                    EnableRaisingEvents = true,
                };
                _watcher.Changed += OnSaveFileChanged;
                _watcher.Created += OnSaveFileChanged;
                _watcher.Renamed += OnSaveFileChanged;
            }
            catch (IOException ex)
            {
                // Watching is best-effort; without it external edits apply on next start.
                AppLog.Write("watcher", ex.Message);
            }
        }

        private void OnDataChanged(object? sender, EventArgs e)
        {
            if (_suppressChangeEvents)
                return;
            // Recents hide archived projects, so any change may affect the projection.
            RebuildRecentProjection();
            // Debounce: bursts of edits collapse into one save.
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void OnSaveFileChanged(object sender, FileSystemEventArgs e)
        {
            // Raised on a watcher thread; hop to the UI thread and debounce.
            _reloadTimer.Dispatcher.BeginInvoke(() =>
            {
                _reloadTimer.Stop();
                _reloadTimer.Start();
            });
        }

        private void ReloadIfExternallyChanged()
        {
            var revision = _store.PeekRevision();
            if (revision == null || revision == _store.LastWrittenRevision)
                return;

            StoreData data;
            try
            {
                data = _store.Load();
            }
            catch (IOException ex)
            {
                AppLog.Write("reload", ex.Message);
                return;
            }

            _suppressChangeEvents = true;
            try
            {
                // Mutate the existing collection in place so bindings survive.
                projectModels.Clear();
                foreach (var project in data.Projects)
                    projectModels.Add(project);

                _recentIds.Clear();
                _recentIds.AddRange(data.RecentProjectIds);
                RebuildRecentProjection();
            }
            finally
            {
                _suppressChangeEvents = false;
            }

            _changeTracker.Attach(projectModels);
            WeakReferenceMessenger.Default.Send(new StoreReloadedMessage());
        }

        public void SaveNow() => Persist(waitForDisk: false);

        public void Flush() => Persist(waitForDisk: true);

        /// <summary>
        /// Serializing reads the live model graph, so it has to happen here on the
        /// UI thread. Everything after that is file work — waiting on the
        /// cross-process lock (up to two seconds), rotating three backups, writing —
        /// and used to stutter the UI on every autosave tick, so it runs off-thread.
        /// </summary>
        private void Persist(bool waitForDisk)
        {
            _saveTimer.Stop();

            var prepared = _store.Prepare(new StoreData
            {
                Projects = projectModels,
                RecentProjectIds = _recentIds.ToList(),
            });

            var write = WriteAsync(prepared);
            if (waitForDisk)
                write.GetAwaiter().GetResult();
        }

        private async Task WriteAsync(ProjectStore.PreparedSave prepared)
        {
            // One write at a time, in order: a burst of edits must not race two
            // writes onto the same file.
            await _writeGate.WaitAsync().ConfigureAwait(false);
            try
            {
                await Task.Run(() => _store.Write(prepared)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Losing a save is worth a log line; it must not take the app down.
                AppLog.Write("save", ex);
            }
            finally
            {
                _writeGate.Release();
            }
        }

        private void RebuildRecentProjection()
        {
            RecentProjects.Clear();
            foreach (var id in _recentIds)
            {
                var project = projectModels.FirstOrDefault(p => p.Id == id);
                if (project != null && !project.IsArchived)
                    RecentProjects.Add(project);
            }
        }

        public void AddRecentProject(ProjectModel project)
        {
            _recentIds.Remove(project.Id);
            _recentIds.Insert(0, project.Id);
            if (_recentIds.Count > MaxRecentProjects)
                _recentIds.RemoveRange(MaxRecentProjects, _recentIds.Count - MaxRecentProjects);
            RebuildRecentProjection();
            OnDataChanged(this, EventArgs.Empty);
        }

        public ProjectModel AddProject(string project, string description)
        {
            var model = new ProjectModel { Name = project, Description = description };
            foreach (var column in BoardColumnDefaults.NewProjectColumns())
                model.Columns.Add(column);
            projectModels.Add(model);
            return model;
        }

        public void RemoveProject(ProjectModel project)
        {
            projectModels.Remove(project);
            if (_recentIds.Remove(project.Id))
                RebuildRecentProjection();
        }

        public bool ChangeProjectName(ProjectModel project, string newName)
        {
            // Rename used to accept anything, so it could produce two projects with the
            // same name even though creation rejected exactly that.
            if (!ProjectNaming.IsAvailable(projectModels, newName, excluding: project))
                return false;

            project.Name = newName.Trim();
            return true;
        }

        public void ChangeProjectDescription(ProjectModel project, string newDescription)
        {
            project.Description = newDescription;
        }

        public void Dispose()
        {
            _watcher?.Dispose();
            _changeTracker.Dispose();
            _writeGate.Dispose();
        }
    }
}
