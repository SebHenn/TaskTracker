using System.Collections.ObjectModel;
using System.Text.Json;
using TaskTracker.Core.Models;

namespace TaskTracker.Core.Storage
{
    /// <summary>
    /// File-based store for all TaskTracker data. Writes are atomic (tmp file +
    /// move) with three rolling backups, and guarded by a cross-process lock file
    /// so the WPF app and the MCP server can share the same files safely.
    /// </summary>
    public class ProjectStore
    {
        public const int BackupCount = 3;

        public string BaseDirectory { get; }
        public string SaveFilePath { get; }
        public string LegacyRecentFilePath { get; }
        private string LockFilePath => Path.Combine(BaseDirectory, "store.lock");

        /// <summary>Revision of the last envelope this instance wrote; used to ignore own file-watcher events.</summary>
        public Guid LastWrittenRevision { get; private set; }

        public ProjectStore(string? baseDirectory = null)
        {
            BaseDirectory = baseDirectory
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TaskTracker");
            SaveFilePath = Path.Combine(BaseDirectory, "Save.json");
            LegacyRecentFilePath = Path.Combine(BaseDirectory, "SaveRecent.json");
        }

        public StoreData Load()
        {
            using var _ = AcquireLock();
            return LoadUnlocked();
        }

        public void Save(StoreData data)
        {
            using var _ = AcquireLock();
            SaveUnlocked(data);
        }

        /// <summary>Load, apply <paramref name="mutate"/>, save — all under one lock. Used by out-of-process callers (MCP).</summary>
        public StoreData Update(Action<StoreData> mutate)
        {
            using var _ = AcquireLock();
            var data = LoadUnlocked();
            mutate(data);
            SaveUnlocked(data);
            return data;
        }

        /// <summary>Reads only the envelope revision without deserializing projects. Null for missing/v1 files.</summary>
        public Guid? PeekRevision()
        {
            try
            {
                using var stream = new FileStream(SaveFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var doc = JsonDocument.Parse(stream);
                if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                    doc.RootElement.TryGetProperty("revision", out var rev) &&
                    rev.TryGetGuid(out var guid))
                {
                    return guid;
                }
            }
            catch (IOException) { }
            catch (JsonException) { }
            return null;
        }

        private StoreData LoadUnlocked()
        {
            if (!File.Exists(SaveFilePath))
                return new StoreData();

            var json = File.ReadAllText(SaveFilePath);
            var firstChar = json.AsSpan().TrimStart();
            if (firstChar.Length == 0)
                return new StoreData();

            if (firstChar[0] == '[')
            {
                var migrated = MigrateFromV1(json);
                NormalizeColumns(migrated);
                return migrated;
            }

            var data = JsonSerializer.Deserialize<StoreData>(json, CoreJson.Options) ?? new StoreData();
            // Drop recent ids that no longer resolve to a project.
            data.RecentProjectIds.RemoveAll(id => data.Projects.All(p => p.Id != id));
            NormalizeColumns(data);
            return data;
        }

        /// <summary>
        /// Enforces board-column invariants on every load path (pre-columns files,
        /// hand-edited or externally written data): each project has at least one
        /// column and one done column, and every task resolves to a real column.
        /// </summary>
        public static void NormalizeColumns(StoreData data)
        {
            foreach (var project in data.Projects)
            {
                if (project.Columns.Count == 0)
                {
                    foreach (var column in BoardColumnDefaults.MigrationColumns())
                        project.Columns.Add(column);
                }
                if (project.Columns.All(c => !c.IsDoneColumn))
                    project.Columns.Add(new BoardColumn { Name = "Done", IsDoneColumn = true });

                foreach (var task in project.Tasks)
                {
                    if (task.ColumnId == null || project.Columns.All(c => c.Id != task.ColumnId.Value))
                        task.ColumnId = (task.IsDone ? project.FirstDoneColumn : project.FirstColumn)!.Id;
                }
            }
        }

        private StoreData MigrateFromV1(string json)
        {
            var projects = JsonSerializer.Deserialize<ObservableCollection<ProjectModel>>(json, CoreJson.Options)
                           ?? new ObservableCollection<ProjectModel>();
            var data = new StoreData { Projects = projects };

            // v1 kept recents as duplicated project copies in a second file, deduped
            // by name. Map them back onto the real projects by name, keeping order.
            if (File.Exists(LegacyRecentFilePath))
            {
                try
                {
                    var recents = JsonSerializer.Deserialize<List<ProjectModel>>(
                        File.ReadAllText(LegacyRecentFilePath), CoreJson.Options);
                    foreach (var recent in recents ?? new List<ProjectModel>())
                    {
                        var match = projects.FirstOrDefault(p => p.Name == recent.Name);
                        if (match != null && !data.RecentProjectIds.Contains(match.Id))
                            data.RecentProjectIds.Add(match.Id);
                    }
                }
                catch (JsonException) { }
            }

            return data;
        }

        private void SaveUnlocked(StoreData data)
        {
            Directory.CreateDirectory(BaseDirectory);

            data.Version = 2;
            data.Revision = Guid.NewGuid();
            data.SavedAtUtc = DateTime.UtcNow;

            var json = JsonSerializer.Serialize(data, CoreJson.Options);
            var tmpPath = SaveFilePath + ".tmp";
            File.WriteAllText(tmpPath, json);

            RotateBackups();
            File.Move(tmpPath, SaveFilePath, overwrite: true);
            LastWrittenRevision = data.Revision;

            // The legacy recents file is superseded by recentProjectIds in the envelope.
            if (File.Exists(LegacyRecentFilePath))
            {
                try { File.Delete(LegacyRecentFilePath); }
                catch (IOException) { }
            }
        }

        private void RotateBackups()
        {
            if (!File.Exists(SaveFilePath))
                return;

            for (var i = BackupCount - 1; i >= 1; i--)
            {
                var older = $"{SaveFilePath}.bak{i}";
                var newer = $"{SaveFilePath}.bak{i + 1}";
                if (File.Exists(older))
                    File.Move(older, newer, overwrite: true);
            }
            File.Copy(SaveFilePath, $"{SaveFilePath}.bak1", overwrite: true);
        }

        private IDisposable AcquireLock()
        {
            Directory.CreateDirectory(BaseDirectory);
            IOException? last = null;
            for (var attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    return new FileStream(LockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException ex)
                {
                    last = ex;
                    Thread.Sleep(100);
                }
            }
            throw new IOException($"Could not acquire store lock at '{LockFilePath}'.", last);
        }
    }
}
