using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using TaskTracker.Core.Models;

namespace TaskTracker.Core.Storage
{
    /// <summary>
    /// The v2 on-disk envelope. Version 1 files (a bare JSON array of projects)
    /// are still readable; they are migrated to this shape on the next save.
    /// </summary>
    public class StoreData
    {
        [JsonPropertyName("version")]
        public int Version { get; set; } = 2;

        /// <summary>Fresh GUID per write; lets a process recognise its own writes.</summary>
        [JsonPropertyName("revision")]
        public Guid Revision { get; set; }

        [JsonPropertyName("savedAtUtc")]
        public DateTime SavedAtUtc { get; set; }

        /// <summary>Most-recently-opened project ids, newest first. References into <see cref="Projects"/>.</summary>
        [JsonPropertyName("recentProjectIds")]
        public List<Guid> RecentProjectIds { get; set; } = new();

        [JsonPropertyName("projects")]
        public ObservableCollection<ProjectModel> Projects { get; set; } = new();
    }
}
