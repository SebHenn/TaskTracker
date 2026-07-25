using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TaskTracker.Controls
{
    /// <summary>
    /// A selectable navigation entry in the sidebar (Home, Settings, each project).
    /// A RadioButton because exactly one destination is current at a time.
    /// </summary>
    public class ProjectItem : RadioButton
    {
        /// <summary>
        /// Geometry rather than an image path: the icon inherits the control's
        /// Foreground, so it follows the theme and its own hover and selected states
        /// instead of staying the one colour it was exported at.
        /// </summary>
        public static readonly DependencyProperty IconProperty =
            DependencyProperty.Register(nameof(Icon), typeof(Geometry), typeof(ProjectItem), new PropertyMetadata(null));

        public Geometry? Icon
        {
            get => (Geometry?)GetValue(IconProperty);
            set => SetValue(IconProperty, value);
        }
    }
}
