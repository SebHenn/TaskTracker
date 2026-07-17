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

        private void OnTaskPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is TaskModel task)
            {
                DragDrop.DoDragDrop(sender as DependencyObject, task.Id, DragDropEffects.Move);
            }
        }
    }
}
