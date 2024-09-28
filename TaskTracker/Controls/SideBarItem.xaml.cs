using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace TaskTracker.Controls
{
    /// <summary>
    /// Interaction logic for SideBarItem.xaml
    /// </summary>
    public partial class SideBarItem : UserControl
    {
        public event RoutedEventHandler Click;

        public SideBarItem()
        {
            this.MouseDown += SideBarItem_MouseDown;
            this.MouseUp += SideBarItem_MouseUp;
        }

        private void SideBarItem_MouseDown(object sender, MouseButtonEventArgs e)
        {
            IsPressed = true;
        }

        private void SideBarItem_MouseUp(object sender, MouseButtonEventArgs e)
        {
            IsPressed = false;
        }

        public static readonly DependencyProperty IsPressedProperty =
        DependencyProperty.Register("IsPressed", typeof(bool), typeof(SideBarItem), new PropertyMetadata(false));

        public bool IsPressed
        {
            get { return (bool)GetValue(IsPressedProperty); }
            set { SetValue(IsPressedProperty, value); }
        }

        public static readonly DependencyProperty ImageProperty =
            DependencyProperty.Register("Image", typeof(string), typeof(SideBarItem), new PropertyMetadata(string.Empty));

        public string Image
        {
            get { return (string)GetValue(ImageProperty); }
            set { SetValue(ImageProperty, value); }
        }

        protected virtual void OnClick()
        {
            Click?.Invoke(this, new RoutedEventArgs());
        }
    }
}
