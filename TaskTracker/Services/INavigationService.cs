using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TaskTracker.Services
{
    public interface INavigationService
    {
        /// <summary>Null until the first <see cref="NavigateTo{T}"/> call.</summary>
        ObservableObject? CurrentView { get; }
        void NavigateTo<T>() where T : ObservableObject;
    }
}
