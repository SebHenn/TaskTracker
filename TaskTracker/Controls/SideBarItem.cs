using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TaskTracker.Controls
{
    /// <summary>
    /// A command entry with an icon (Add Project, Add Task, a recent project).
    /// Unlike <see cref="ProjectItem"/> it does not stay selected — it acts and returns.
    ///
    /// Derives from Button rather than ButtonBase, which it used to: ButtonBase creates
    /// no automation peer, so every one of these was absent from the accessibility tree
    /// entirely — a screen reader could not see them, and neither could anything else
    /// driving the UI. Button also brings Space and Enter activation for free.
    /// </summary>
    public class SideBarItem : Button
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
