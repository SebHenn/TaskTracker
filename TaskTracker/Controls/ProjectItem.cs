using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace TaskTracker.Controls
{
    public class ProjectItem : RadioButton
    {
        public static readonly DependencyProperty ProjectImageProperty =
            DependencyProperty.Register("Image", typeof(string), typeof(ProjectItem), new PropertyMetadata(string.Empty));

        public ProjectItem()
        {

        }

        public string Image
        {
            get { return (string)GetValue(ProjectImageProperty); }
            set { SetValue(ProjectImageProperty, value); }
        }
    }
}
