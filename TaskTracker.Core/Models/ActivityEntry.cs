using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace TaskTracker.Core.Models
{
    /// <summary>A timestamped note on a task — a lightweight activity journal.</summary>
    public partial class ActivityEntry : ObservableObject
    {
        [ObservableProperty]
        private Guid _id = Guid.NewGuid();

        [ObservableProperty]
        private DateTime _atUtc = DateTime.UtcNow;

        [ObservableProperty]
        private string _text = "";
    }
}
