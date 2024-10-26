using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace TaskTracker.Models
{
    public partial class ProjectModel : ObservableObject
    {
        [ObservableProperty]
        private string _name = "";

        [ObservableProperty]
        private string _description = "";

        [ObservableProperty]
        private ObservableCollection<TaskModel> _tasks = new ObservableCollection<TaskModel>();

        [ObservableProperty]
        private bool _isSelected = false;
    }
}
