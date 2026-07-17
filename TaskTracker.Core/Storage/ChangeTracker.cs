using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using TaskTracker.Core.Models;

namespace TaskTracker.Core.Storage
{
    /// <summary>
    /// Watches a project collection recursively (project properties, task lists,
    /// task properties) and raises a single <see cref="Changed"/> event for any
    /// mutation, so callers can autosave. Re-attaches automatically when items
    /// are added, removed, or the collections are reset in place.
    /// </summary>
    public class ChangeTracker : IDisposable
    {
        private ObservableCollection<ProjectModel>? _projects;
        private readonly HashSet<ProjectModel> _trackedProjects = new();
        private readonly HashSet<TaskModel> _trackedTasks = new();
        private readonly HashSet<BoardColumn> _trackedColumns = new();
        private readonly HashSet<SubTaskModel> _trackedSubTasks = new();

        public event EventHandler? Changed;

        /// <summary>Properties that are UI-only state and should not trigger a save.</summary>
        private static readonly HashSet<string> IgnoredProperties = new() { nameof(ProjectModel.IsSelected) };

        public void Attach(ObservableCollection<ProjectModel> projects)
        {
            Detach();
            _projects = projects;
            _projects.CollectionChanged += OnProjectsCollectionChanged;
            foreach (var project in projects)
                TrackProject(project);
        }

        public void Detach()
        {
            if (_projects != null)
                _projects.CollectionChanged -= OnProjectsCollectionChanged;
            _projects = null;

            foreach (var project in _trackedProjects)
            {
                project.PropertyChanged -= OnItemPropertyChanged;
                project.Tasks.CollectionChanged -= OnTasksCollectionChanged;
                project.Columns.CollectionChanged -= OnColumnsCollectionChanged;
            }
            foreach (var task in _trackedTasks)
            {
                task.PropertyChanged -= OnItemPropertyChanged;
                task.SubTasks.CollectionChanged -= OnSubTasksCollectionChanged;
            }
            foreach (var column in _trackedColumns)
                column.PropertyChanged -= OnItemPropertyChanged;
            foreach (var subTask in _trackedSubTasks)
                subTask.PropertyChanged -= OnItemPropertyChanged;

            _trackedProjects.Clear();
            _trackedTasks.Clear();
            _trackedColumns.Clear();
            _trackedSubTasks.Clear();
        }

        private void TrackProject(ProjectModel project)
        {
            if (!_trackedProjects.Add(project))
                return;
            project.PropertyChanged += OnItemPropertyChanged;
            project.Tasks.CollectionChanged += OnTasksCollectionChanged;
            project.Columns.CollectionChanged += OnColumnsCollectionChanged;
            foreach (var task in project.Tasks)
                TrackTask(task);
            foreach (var column in project.Columns)
                TrackColumn(column);
        }

        private void TrackColumn(BoardColumn column)
        {
            if (_trackedColumns.Add(column))
                column.PropertyChanged += OnItemPropertyChanged;
        }

        private void OnColumnsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            foreach (var column in e.NewItems?.OfType<BoardColumn>() ?? Enumerable.Empty<BoardColumn>())
                TrackColumn(column);
            RaiseChanged();
        }

        private void TrackTask(TaskModel task)
        {
            if (!_trackedTasks.Add(task))
                return;
            task.PropertyChanged += OnItemPropertyChanged;
            task.SubTasks.CollectionChanged += OnSubTasksCollectionChanged;
            foreach (var subTask in task.SubTasks)
                TrackSubTask(subTask);
        }

        private void TrackSubTask(SubTaskModel subTask)
        {
            if (_trackedSubTasks.Add(subTask))
                subTask.PropertyChanged += OnItemPropertyChanged;
        }

        private void OnSubTasksCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            foreach (var subTask in e.NewItems?.OfType<SubTaskModel>() ?? Enumerable.Empty<SubTaskModel>())
                TrackSubTask(subTask);
            RaiseChanged();
        }

        private void OnProjectsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            foreach (var project in e.NewItems?.OfType<ProjectModel>() ?? Enumerable.Empty<ProjectModel>())
                TrackProject(project);
            RaiseChanged();
        }

        private void OnTasksCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            foreach (var task in e.NewItems?.OfType<TaskModel>() ?? Enumerable.Empty<TaskModel>())
                TrackTask(task);
            RaiseChanged();
        }

        private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != null && IgnoredProperties.Contains(e.PropertyName))
                return;
            // A project's Tasks collection instance can be replaced wholesale; re-hook it.
            if (sender is ProjectModel project && e.PropertyName == nameof(ProjectModel.Tasks))
            {
                project.Tasks.CollectionChanged += OnTasksCollectionChanged;
                foreach (var task in project.Tasks)
                    TrackTask(task);
            }
            RaiseChanged();
        }

        private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

        public void Dispose() => Detach();
    }
}
