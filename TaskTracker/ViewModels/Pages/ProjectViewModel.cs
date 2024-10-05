using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaskTracker.Models;
using TaskTracker.ViewModels.Windows;

namespace TaskTracker.ViewModels.Pages
{
    public class ProjectViewModel : ObservableObject
    {
        private ProjectModel _currentProject;

        public ProjectModel CurrentProject
        {
            get => _currentProject;
            set
            {
                _currentProject = value;
                OnPropertyChanged();
            }
        }

        public ProjectViewModel(MainViewModel mainViewModel)
        {
            CurrentProject = mainViewModel.SelectedProject;
            mainViewModel.PropertyChanged += (sender, args) =>
            {
                if (args.PropertyName == nameof(mainViewModel.SelectedProject))
                {
                    CurrentProject = mainViewModel.SelectedProject;
                }
            };
        }
    }

}
