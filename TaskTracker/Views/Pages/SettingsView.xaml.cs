using System.Windows;
using System.Windows.Controls;
using TaskTracker.ViewModels.Pages;

namespace TaskTracker.Views.Pages
{
    /// <summary>
    /// Interaction logic for SettingsView.xaml
    /// </summary>
    public partial class SettingsView : UserControl
    {
        public SettingsView()
        {
            InitializeComponent();
        }

        private void TokenBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            // PasswordBox.Password is not bindable by design; hand it to the VM.
            if (DataContext is SettingsViewModel vm)
                vm.PendingToken = ((PasswordBox)sender).Password;
        }
    }
}
