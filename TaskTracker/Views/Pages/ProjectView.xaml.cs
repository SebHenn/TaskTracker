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
            if (sender is not FrameworkElement element || element.DataContext is not ColumnLaneViewModel lane)
                return;
            if (!e.Data.GetDataPresent(typeof(Guid)))
                return;

            var taskId = (Guid)e.Data.GetData(typeof(Guid));
            viewModel.MoveTask(taskId, lane, ComputeInsertIndex(element, lane, taskId, e));
        }

        /// <summary>Visual position for the drop: before the first card whose vertical midpoint is below the pointer.</summary>
        private static int ComputeInsertIndex(FrameworkElement laneRoot, ColumnLaneViewModel lane, Guid draggedId, DragEventArgs e)
        {
            var itemsControl = FindTasksItemsControl(laneRoot);
            if (itemsControl == null)
                return int.MaxValue;

            var index = 0;
            for (var i = 0; i < lane.Tasks.Count; i++)
            {
                if (lane.Tasks[i].Id == draggedId)
                    continue; // position is relative to the list without the dragged card
                if (itemsControl.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement container)
                    break;
                var midpoint = container.TranslatePoint(new Point(0, container.ActualHeight / 2), laneRoot).Y;
                if (e.GetPosition(laneRoot).Y < midpoint)
                    return index;
                index++;
            }
            return index;
        }

        private static ItemsControl? FindTasksItemsControl(DependencyObject root)
        {
            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (child is ItemsControl items)
                    return items;
                if (FindTasksItemsControl(child) is { } nested)
                    return nested;
            }
            return null;
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
