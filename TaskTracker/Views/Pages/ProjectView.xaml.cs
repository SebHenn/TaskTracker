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

        /// <summary>
        /// Where the drop lands, as an index into the lane *without* the dragged
        /// card — which is the frame of reference MoveTask works in.
        ///
        /// Hit-tests the card under the pointer instead of walking the lane by
        /// index: once the list virtualizes, off-screen rows have no container at
        /// all, so an index walk stops at the first unrealized row and drops the
        /// card in the wrong place as soon as a column is scrolled.
        /// </summary>
        private static int ComputeInsertIndex(FrameworkElement laneRoot, ColumnLaneViewModel lane, Guid draggedId, DragEventArgs e)
        {
            var list = FindTasksItemsControl(laneRoot);
            if (list == null)
                return int.MaxValue;

            var point = e.GetPosition(list);
            if (list.InputHitTest(point) is not DependencyObject hit ||
                list.ContainerFromElement(hit) is not FrameworkElement container)
            {
                return int.MaxValue; // empty space past the last card: append
            }

            var index = list.ItemContainerGenerator.IndexFromContainer(container);
            if (index < 0)
                return int.MaxValue;

            // Past the card's midpoint means "after it".
            var midpoint = container.TranslatePoint(new Point(0, container.ActualHeight / 2), list).Y;
            var target = point.Y < midpoint ? index : index + 1;

            // Pulling the dragged card out first shifts everything below it up one.
            var currentIndex = IndexOfTask(lane, draggedId);
            if (currentIndex >= 0 && currentIndex < target)
                target--;

            return target;
        }

        private static int IndexOfTask(ColumnLaneViewModel lane, Guid id)
        {
            for (var i = 0; i < lane.Tasks.Count; i++)
            {
                if (lane.Tasks[i].Id == id)
                    return i;
            }
            return -1;
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
