using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace TaskTracker.Core.Models
{
    public partial class SubTaskModel : ObservableObject
    {
        [ObservableProperty]
        private Guid _id = Guid.NewGuid();

        [ObservableProperty]
        private string _title = "";

        [ObservableProperty]
        private bool _isDone = false;
    }
}
