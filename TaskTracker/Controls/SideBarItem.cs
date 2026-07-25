using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace TaskTracker.Controls
{
    /// <summary>
    /// A command entry with an icon (Add Project, Add Task, a recent project).
    /// Unlike <see cref="ProjectItem"/> it does not stay selected — it acts and returns.
    /// </summary>
    public class SideBarItem : ButtonBase
    {
        /// <inheritdoc cref="ProjectItem.IconProperty" />
        public static readonly DependencyProperty IconProperty =
            DependencyProperty.Register(nameof(Icon), typeof(Geometry), typeof(SideBarItem), new PropertyMetadata(null));

        public Geometry? Icon
        {
            get => (Geometry?)GetValue(IconProperty);
            set => SetValue(IconProperty, value);
        }
    }
}
