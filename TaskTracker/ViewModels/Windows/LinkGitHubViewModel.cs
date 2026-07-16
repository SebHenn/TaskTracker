using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace TaskTracker.ViewModels.Windows
{
    public partial class LinkGitHubViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _owner = "";

        [ObservableProperty]
        private string _repo = "";

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
