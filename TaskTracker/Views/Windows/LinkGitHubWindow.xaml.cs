using System.Windows;
using System.Windows.Input;
using TaskTracker.ViewModels.Windows;

namespace TaskTracker.Views.Windows
{
    /// <summary>
    /// Interaction logic for LinkGitHubWindow.xaml
    /// </summary>
    public partial class LinkGitHubWindow : Window
    {
        public LinkGitHubWindow(LinkGitHubViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void Rectangle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
