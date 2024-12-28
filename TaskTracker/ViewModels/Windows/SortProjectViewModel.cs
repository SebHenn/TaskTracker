using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TaskTracker.ViewModels.Windows
{
    public partial class SortProjectViewModel : ObservableObject
    {
        [ObservableProperty]
        public bool _isShowEmpty;

        [ObservableProperty]
        public bool _isShowDone;

        [ObservableProperty]
        public bool _isShowOnlyFav;

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

        public SortProjectViewModel(MainViewModel mainViewModel)
        {
            IsShowEmpty = mainViewModel.IsShowEmpty;
            IsShowDone = mainViewModel.IsShowDone;
            IsShowOnlyFav = mainViewModel.IsShowOnlyFav;
        }
    }
}
