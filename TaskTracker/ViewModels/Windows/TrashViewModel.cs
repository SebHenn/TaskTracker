using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Linq;
using TaskTracker.Core.Models;
using TaskTracker.Core.Services;
using TaskTracker.Services;

namespace TaskTracker.ViewModels.Windows
{
    /// <summary>
    /// The project's deleted tasks, with a way to put them back.
    ///
    /// The undo bar only ever reaches the newest deletion and only for eight seconds;
    /// this is where a mistake noticed later gets fixed.
    /// </summary>
    public partial class TrashViewModel : ObservableObject
    {
        private readonly ILanguageService _languageService;
        private readonly IDialogService _dialogService;
        private ProjectModel? _project;

        /// <summary>
        /// A snapshot, not the live collection: restoring mutates the project's trash
        /// while the list is bound to it, and the view rebuilds from the source anyway.
        /// </summary>
        [ObservableProperty]
        private ObservableCollection<TrashedTask> _entries = [];

        [ObservableProperty]
        private bool _isEmpty = true;

        /// <summary>How long entries survive, said in the window so the promise is explicit.</summary>
        public string RetentionText { get; private set; } = "";

        public TrashViewModel(ILanguageService languageService, IDialogService dialogService)
        {
            _languageService = languageService;
            _dialogService = dialogService;
        }

        public void SetProject(ProjectModel project)
        {
            _project = project;
            RetentionText = string.Format(
                _languageService.GetString("TrashRetentionNotice"), (int)Trash.Retention.TotalDays);
            OnPropertyChanged(nameof(RetentionText));
            Reload();
        }

        private void Reload()
        {
            Entries = new ObservableCollection<TrashedTask>(_project?.Trash ?? []);
            IsEmpty = Entries.Count == 0;
        }

        [RelayCommand]
        private void OnRestore(TrashedTask entry)
        {
            if (_project == null)
                return;
            Trash.Restore(_project, entry);
            Reload();
        }

        [RelayCommand]
        private void OnEmptyTrash()
        {
            // The one irreversible action in here, so it asks first.
            if (_project == null || Entries.Count == 0)
                return;
            if (!_dialogService.Confirm(
                    string.Format(_languageService.GetString("EmptyTrashConfirm"), Entries.Count)))
                return;
            Trash.Empty(_project);
            Reload();
        }

        [RelayCommand]
        private void OnRestoreAll()
        {
            if (_project == null)
                return;
            // Oldest first, so the board ends up in the order things were deleted.
            foreach (var entry in Entries.Reverse().ToList())
                Trash.Restore(_project, entry);
            Reload();
        }
    }
}
