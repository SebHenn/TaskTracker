using System.Linq;
using System.Windows;
using TaskTracker.Views.Windows;

namespace TaskTracker.Services
{
    /// <summary>
    /// Modal messages, shown in the app's own themed window rather than the native
    /// MessageBox.
    /// </summary>
    public class DialogService : IDialogService
    {
        private readonly ILanguageService _languageService;

        public DialogService(ILanguageService languageService)
        {
            _languageService = languageService;
        }

        public void Error(string message) => Show(MessageWindow.MessageKind.Error, message);

        public void Info(string message) => Show(MessageWindow.MessageKind.Info, message);

        public bool Confirm(string message) => Show(MessageWindow.MessageKind.Confirm, message);

        private bool Show(MessageWindow.MessageKind kind, string message)
        {
            var confirmText = kind == MessageWindow.MessageKind.Confirm
                ? _languageService.GetString("Yes")
                : _languageService.GetString("OK");

            var window = new MessageWindow(kind, message, confirmText, _languageService.GetString("Cancel"))
            {
                // CenterOwner needs an owner, and a dialog raised from a background
                // service may have none — an unowned window centres on screen instead of
                // throwing, but must not try to own itself.
                Owner = ActiveOwner(),
            };
            if (window.Owner == null)
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            return window.ShowDialog() == true;
        }

        private static Window? ActiveOwner()
        {
            var windows = Application.Current?.Windows.OfType<Window>().ToList();
            if (windows == null)
                return null;
            return windows.FirstOrDefault(w => w.IsActive && w.IsLoaded)
                   ?? (Application.Current!.MainWindow?.IsLoaded == true ? Application.Current.MainWindow : null);
        }
    }
}
