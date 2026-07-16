using System.Windows;
using System.Windows.Input;

namespace TaskTracker.Styles.Controls
{
    public partial class WindowStyleDictionary : ResourceDictionary
    {
        public WindowStyleDictionary()
        {
            InitializeComponent();
        }

        private void Rectangle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                var window = Window.GetWindow((DependencyObject)sender);
                window?.DragMove();
            }
        }
    }
}
