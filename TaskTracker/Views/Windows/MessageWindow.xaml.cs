using System.Windows;
using System.Windows.Media;

namespace TaskTracker.Views.Windows
{
    /// <summary>
    /// The app's own message box: error, info, and yes/no confirmation.
    ///
    /// The native MessageBox was the last unthemed surface in the app — a light Win32
    /// dialog in the middle of a dark window, every time something went wrong. It also
    /// could not be localized past the OS language for its buttons.
    /// </summary>
    public partial class MessageWindow : Window
    {
        public enum MessageKind
        {
            Error,
            Info,
            Confirm,
        }

        public MessageWindow(MessageKind kind, string message, string confirmText, string? cancelText = null)
        {
            InitializeComponent();

            Message = message;
            IsConfirm = kind == MessageKind.Confirm;
            ConfirmText = confirmText;
            CancelText = cancelText ?? "";
            AccentBrush = kind switch
            {
                MessageKind.Error => Brush("Brush.Danger"),
                MessageKind.Confirm => Brush("Brush.Danger"),
                _ => Brush("Brush.Accent.Strong"),
            };

            DataContext = this;
        }

        public string Message { get; }

        public bool IsConfirm { get; }

        public string ConfirmText { get; }

        public string CancelText { get; }

        public Brush AccentBrush { get; }

        /// <summary>
        /// Resolved once here rather than bound with DynamicResource: the brush depends on
        /// the message kind, which XAML cannot switch on without a converter per case.
        /// Themes are swapped at the application level, and this window never outlives one.
        /// </summary>
        private static Brush Brush(string key)
            => Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // Closing with the title-bar X is a decline, which matters for Confirm and is
        // harmless for the single-button kinds.
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
