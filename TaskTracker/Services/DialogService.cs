using System.Windows;

namespace TaskTracker.Services
{
    public class DialogService : IDialogService
    {
        private const string Caption = "TaskTracker";

        public void Error(string message) =>
            MessageBox.Show(message, Caption, MessageBoxButton.OK, MessageBoxImage.Error);

        public void Info(string message) =>
            MessageBox.Show(message, Caption, MessageBoxButton.OK, MessageBoxImage.Information);

        public bool Confirm(string message) =>
            MessageBox.Show(message, Caption, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }
}
