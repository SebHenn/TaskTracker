using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaskTracker.Views.Windows;

namespace TaskTracker.ViewModels.Windows
{
    public partial class NewProjectWindowViewModel : ObservableObject
    {
        public NewProjectWindowViewModel() 
        {

        }

        [ObservableProperty]
        private string _name = "";

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
    }
}
