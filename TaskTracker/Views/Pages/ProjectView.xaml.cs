using CommunityToolkit.Mvvm.Input;
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
using TaskTracker.Models;
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

        private void OnDropNotDone(object sender, DragEventArgs e)
        {
            ProjectViewModel viewModel = (ProjectViewModel)DataContext;
            if (e.Data.GetDataPresent(typeof(Guid)))
            {
                var taskId = (Guid)e.Data.GetData(typeof(Guid));

                var task = viewModel.DoneTasks.FirstOrDefault(t => t.Id == taskId);

                if (task != null)
                {
                    if (!viewModel.NotDoneTasks.Contains(task))
                    {
                        task.IsDone = false;
                        viewModel.NotDoneTasks.Add(task);
                        viewModel.DoneTasks.Remove(task);
                    }
                }
            }
        }

        private void OnDropDone(object sender, DragEventArgs e)
        {
            ProjectViewModel viewModel = (ProjectViewModel)DataContext;
            if (e.Data.GetDataPresent(typeof(Guid)))
            {
                var taskId = (Guid)e.Data.GetData(typeof(Guid));

                var task = viewModel.NotDoneTasks.FirstOrDefault(t => t.Id == taskId);

                if (task != null)
                {
                    if (!viewModel.DoneTasks.Contains(task))
                    {
                        task.IsDone = true;
                        viewModel.DoneTasks.Add(task);
                        viewModel.NotDoneTasks.Remove(task);
                    }
                }
            }
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(Guid)))
            {
                e.Effects = DragDropEffects.Move;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void OnTaskPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var task = (sender as FrameworkElement)?.DataContext as TaskModel;
            if (task != null)
            {
                DragDrop.DoDragDrop(sender as DependencyObject, task.Id, DragDropEffects.Move);
            }
        }
    }
}
