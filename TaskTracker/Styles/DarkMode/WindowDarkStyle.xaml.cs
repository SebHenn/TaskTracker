using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows;

namespace TaskTracker.Styles.DarkMode
{
    public partial class WindowDarkStyle : ResourceDictionary
    {
        public WindowDarkStyle()
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
