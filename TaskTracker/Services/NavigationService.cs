using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TaskTracker.Services
{
    public class NavigationService : ObservableObject, INavigationService
    {
        private readonly Func<Type, ObservableObject> _viewModelFactory;
        private ObservableObject _currentView;

        public ObservableObject CurrentView {
            get => _currentView;
            private set
            {
                _currentView = value;
                OnPropertyChanged();
            }
        }

        public NavigationService(Func<Type,ObservableObject> viewModelFactory)
        {
            _viewModelFactory = viewModelFactory;
        }

        public void NavigateTo<TObservableObject>() where TObservableObject : ObservableObject
        {
            ObservableObject ViewModel = _viewModelFactory.Invoke(typeof(TObservableObject));
            CurrentView = ViewModel;            
        }
    }
}
