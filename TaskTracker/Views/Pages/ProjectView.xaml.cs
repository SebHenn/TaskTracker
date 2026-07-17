using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TaskTracker.Core.Models;
using TaskTracker.ViewModels.Pages;

namespace TaskTracker.Views.Pages
{
    /// <summary>
    /// Interaction logic for ProjectView.xaml
    /// </summary>
    public partial class ProjectView : UserControl
    {
        private Point _dragStart;
        private TaskModel? _pressedTask;
        private bool _dragStarted;

        public ProjectView()
        {
            InitializeComponent();
        }

        private void OnDropLane(object sender, DragEventArgs e)
        {
            if (DataContext is not ProjectViewModel viewModel)
                return;
            if ((sender as FrameworkElement)?.DataContext is not ColumnLaneViewModel lane)
                return;
            if (e.Data.GetDataPresent(typeof(Guid)))
                viewModel.MoveTask((Guid)e.Data.GetData(typeof(Guid)), lane);
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(typeof(Guid)) ? DragDropEffects.Move : DragDropEffects.None;
        }

        // A press arms a potential drag; actual dragging starts only after the
        // mouse moves past the system threshold, so a plain click falls through
        // to mouse-up and opens the detail panel instead.
        private void OnTaskPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _pressedTask = (sender as FrameworkElement)?.DataContext as TaskModel;
            _dragStart = e.GetPosition(this);
            _dragStarted = false;
        }

        private void OnTaskPreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_pressedTask == null || _dragStarted || e.LeftButton != MouseButtonState.Pressed)
                return;

            var delta = e.GetPosition(this) - _dragStart;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            _dragStarted = true;
            DragDrop.DoDragDrop(sender as DependencyObject, _pressedTask.Id, DragDropEffects.Move);
            _pressedTask = null;
        }

        private void OnTaskPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_pressedTask != null && !_dragStarted &&
                (sender as FrameworkElement)?.DataContext == _pressedTask &&
                DataContext is ProjectViewModel viewModel)
            {
                viewModel.OnOpenTaskDetail(_pressedTask);
            }
            _pressedTask = null;
            _dragStarted = false;
        }
    }
}
