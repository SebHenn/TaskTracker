using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;

namespace TaskTracker.Services
{
    /// <summary>
    /// The one undo bar, shared by every destructive action in the app.
    ///
    /// It started as a private timer and a list of trashed tasks inside the board view
    /// model, which is why deleting a task could be undone but deleting a column, moving
    /// a batch of cards, archiving, or deleting a whole project could not. Hoisting it to
    /// the shell means an action anywhere can offer a way back, and there is exactly one
    /// bar rather than one per page competing for the same corner.
    ///
    /// The offer is a closure, so each caller decides what "undo" means for it. Nothing
    /// here is a general undo stack: one step, one window, and the durable safety net for
    /// tasks is still the trash.
    /// </summary>
    public partial class UndoService : ObservableObject
    {
        /// <summary>How long the bar stays up. Long enough to notice a mistake, short enough not to linger.</summary>
        public static readonly TimeSpan Window = TimeSpan.FromSeconds(8);

        private readonly System.Windows.Threading.DispatcherTimer _timer;
        private Action? _undo;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsVisible))]
        private string _text = "";

        public bool IsVisible => _undo != null;

        public UndoService()
        {
            _timer = new System.Windows.Threading.DispatcherTimer { Interval = Window };
            _timer.Tick += (_, _) => Dismiss();
        }

        /// <summary>
        /// Offers a way back from something just done. Replaces any pending offer —
        /// a second action supersedes the first, and holding both would need a real stack
        /// and a way to show it.
        /// </summary>
        public void Offer(string text, Action undo)
        {
            ArgumentNullException.ThrowIfNull(undo);

            _undo = undo;
            Text = text;
            OnPropertyChanged(nameof(IsVisible));

            _timer.Stop();
            _timer.Start();
        }

        [RelayCommand]
        private void OnUndo()
        {
            var undo = _undo;
            // Cleared before invoking: the callback may itself offer a new undo, and
            // clearing afterwards would discard it.
            Dismiss();
            undo?.Invoke();
        }

        /// <summary>Drops the offer without running it — the window expiring, or a page change.</summary>
        public void Dismiss()
        {
            _timer.Stop();
            _undo = null;
            Text = "";
            OnPropertyChanged(nameof(IsVisible));
        }
    }
}
