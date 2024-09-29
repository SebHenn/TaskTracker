using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaskTracker.Services;

namespace TaskTracker.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        private INavigationService _avigationService;

        public INavigationService NavigationService
        {
            get { return _avigationService; }
            set 
            { 
                _avigationService = value; 
                OnPropertyChanged();
            }
        }

        public RelayCommand NavigateToHomeCommand { get; set; }

        public MainViewModel(INavigationService navigationService)
        {
            NavigationService = navigationService;
            NavigateToHomeCommand = new RelayCommand( () => NavigationService.NavigateTo<HomeViewModel>(), () => true );
            NavigationService.NavigateTo<HomeViewModel>();
        }
    }
}
