using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace TaskTracker.Core.Models
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
        [property: JsonIgnore]
        private bool _isSelected = false;

        [ObservableProperty]
        private bool _isFavourite = false;

        [ObservableProperty]
        private Guid _id = Guid.NewGuid();

        /// <summary>Owner of the linked GitHub repository, or null when not linked.</summary>
        [ObservableProperty]
        private string? _gitHubOwner;

        /// <summary>Name of the linked GitHub repository, or null when not linked.</summary>
        [ObservableProperty]
        private string? _gitHubRepo;

        [ObservableProperty]
        private DateTime? _lastSyncedAtUtc;

        [ObservableProperty]
        private bool _isArchived = false;

        [ObservableProperty]
        private DateTime? _archivedAtUtc;

        [JsonIgnore]
        public bool IsGitHubLinked => !string.IsNullOrWhiteSpace(GitHubOwner) && !string.IsNullOrWhiteSpace(GitHubRepo);
    }
}
