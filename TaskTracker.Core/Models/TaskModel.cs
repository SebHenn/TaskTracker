using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TaskTracker.Core.Models
{
    public partial class TaskModel : ObservableObject
    {
        [ObservableProperty]
        private string _title = "";

        [ObservableProperty]
        private string _description = "";

        [ObservableProperty]
        private bool _isDone = false;

        [ObservableProperty]
        private Guid _id = Guid.NewGuid();
    }
}
