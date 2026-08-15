using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using TaskTracker.Core.Services;
using TaskTracker.Messages;
using TaskTracker.Services;

namespace TaskTracker.ViewModels.Pages
{
    /// <summary>
    /// One day of the agenda, with its heading already formatted — the view cannot build
    /// "Today · Fri 14 Aug" from a DateTime without a converter per part, and the strings
    /// are localized.
    /// </summary>
    public record AgendaDayRow(
        DateTime Date,
        string DayName,
        string DateText,
        bool IsToday,
        bool IsWeekend,
        IReadOnlyList<DueTaskItem> Items)
    {
        public bool IsEmpty => Items.Count == 0;
        public int Count => Items.Count;
    }

    /// <summary>
    /// The next week of dated work, a day at a time.
    ///
    /// The home dashboard answers "what is due", in three buckets; this answers "when",
    /// which is the question you ask when deciding what to do today. Both read the same
    /// <see cref="Agenda"/>/<see cref="DueTasks"/> window, so they cannot disagree.
    /// </summary>
    public partial class AgendaViewModel : ObservableObject
    {
        private readonly IProjectsService _projectsService;
        private readonly ILanguageService _languageService;

        [ObservableProperty]
        private ObservableCollection<AgendaDayRow> _days = [];

        [ObservableProperty]
        private ObservableCollection<DueTaskItem> _overdue = [];

        [ObservableProperty]
        private bool _hasOverdue;

        /// <summary>False when the whole window is empty, which gets a sentence rather than eight blank rows.</summary>
        [ObservableProperty]
        private bool _hasAnything;

        [ObservableProperty]
        private string _rangeText = "";

        [ObservableProperty]
        private int _scheduledCount;

        public AgendaViewModel(IProjectsService projectsService, ILanguageService languageService)
        {
            _projectsService = projectsService;
            _languageService = languageService;

            // Day names and the range are formatted strings, so they have to be rebuilt
            // on a language change rather than re-resolved by the binding.
            _languageService.LanguageChanged += Refresh;

            // Mandatory for any page holding derived copies of store data: a live reload
            // replaces the model graph and these rows would otherwise point at the old one.
            WeakReferenceMessenger.Default.Register<StoreReloadedMessage>(
                this, (r, _) => ((AgendaViewModel)r).Refresh());

            Refresh();
        }

        public void Refresh()
        {
            var culture = CultureInfo.CurrentCulture;
            var result = Agenda.Compute(_projectsService.projectModels);

            Days = new ObservableCollection<AgendaDayRow>(result.Days.Select(day => new AgendaDayRow(
                day.Date,
                day.IsToday ? _languageService.GetString("Today") : day.Date.ToString("dddd", culture),
                day.Date.ToString("d MMM", culture),
                day.IsToday,
                day.Date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday,
                day.Items)));

            Overdue = new ObservableCollection<DueTaskItem>(result.Overdue);
            HasOverdue = result.Overdue.Count > 0;
            ScheduledCount = result.TotalScheduled;
            HasAnything = !result.IsEmpty;

            var first = result.Days.Count > 0 ? result.Days[0].Date : DateTime.Today;
            var last = result.Days.Count > 0 ? result.Days[^1].Date : DateTime.Today;
            RangeText = string.Format(
                culture, _languageService.GetString("AgendaRangeFormat"), first, last);
        }

        [RelayCommand]
        private void OnTaskClick(DueTaskItem item)
        {
            // Opens the board *and* selects the task, rather than dropping the user on the
            // project to hunt for the row they just clicked.
            WeakReferenceMessenger.Default.Send(new OpenTaskMessage(item.Project, item.Task));
        }
    }
}
