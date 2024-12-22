using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TaskTracker.ViewModels.Pages
{
    public partial class HomeViewModel : ObservableObject
    {
        [ObservableProperty]
        public string _name = "Tasktracker";

        [ObservableProperty]
        public string _description = "Keep track of multiple projects by adding Tasks to them";
    }
}
