using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaskTracker.ViewModels.Pages;
using TaskTracker.Views.Windows;

namespace TaskTracker.ViewModels.Windows
{
    public partial class NewProjectViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _titleString = "";

        [ObservableProperty]
        private string _nameString = "";

        [ObservableProperty]
        private string _descriptionString = "";

        [ObservableProperty]
        private string _name = "";

        [ObservableProperty]
        private string _description = "";
        
        [ObservableProperty]
        private bool _dialogResult = false;

        [RelayCommand]
        private void OnConfirm()
        {
            DialogResult = true;
        }

        [RelayCommand]
        private void OnCancel()
        {
            DialogResult = false;
        }

        public NewProjectViewModel(ProjectViewModel projectViewModel)
        {
            TitleString = projectViewModel.IsEditing ? "Change current Project" : projectViewModel.IsCreateTask ? "Create new Task" : "Create new Project";
            NameString = projectViewModel.IsCreateTask ? "Task Name" : "Project Name";
            DescriptionString = projectViewModel.IsCreateTask ? "Task Description" : "Project Description";
        }
    }
}
