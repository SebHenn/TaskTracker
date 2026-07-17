using System.Windows;
using TaskTracker.ViewModels.Windows;

namespace TaskTracker.Views.Windows
{
    /// <summary>
    /// Interaction logic for QuickAddWindow.xaml
    /// </summary>
    public partial class QuickAddWindow : Window
    {
        public QuickAddWindow(QuickAddViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            Loaded += (_, _) =>
            {
                Activate();
                TitleBox.FocusText();
            };
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                    Confirm_Click(this, new RoutedEventArgs());
                else if (e.Key == System.Windows.Input.Key.Escape)
                    Close();
            };
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            (DataContext as QuickAddViewModel)?.ConfirmCommand.Execute(null);
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
