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
            TitleString = projectViewModel.IsEditing == true ? "Change current Project" : "Create new Project";
        }
    }
}
