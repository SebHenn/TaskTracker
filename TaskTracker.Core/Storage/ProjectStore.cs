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

        /// <summary>An envelope already stamped and serialized, ready to hit the disk.</summary>
        public readonly record struct PreparedSave(string Json, Guid Revision);

        /// <summary>
        /// Stamps and serializes without touching the disk.
        ///
        /// Split out from <see cref="Save"/> so a caller on a UI thread can do this
        /// part — the only part that reads the live model graph — on that thread,
        /// and hand <see cref="Write"/> to a background one. That matters because
        /// <see cref="AcquireLock"/> can block for up to two seconds waiting on the
        /// other process, and backup rotation copies the whole file.
        /// </summary>
        public PreparedSave Prepare(StoreData data)
        {
            data.Version = 2;
            data.Revision = Guid.NewGuid();
            data.SavedAtUtc = DateTime.UtcNow;

            // Claim the revision up front: the file watcher compares against it to
            // recognise our own writes, and must not see a stale value while an
            // async write is still in flight.
            LastWrittenRevision = data.Revision;

            return new PreparedSave(JsonSerializer.Serialize(data, CoreJson.Options), data.Revision);
        }

        /// <summary>Writes a prepared envelope. Safe to call off the UI thread.</summary>
        public void Write(PreparedSave prepared)
        {
            using var _ = AcquireLock();
            WriteUnlocked(prepared);
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

        /// <summary>
        /// Saves only if the file on disk still carries <paramref name="expectedRevision"/>,
        /// i.e. nobody wrote to it since the caller loaded. Returns false instead of saving
        /// when it has moved on.
        ///
        /// For callers that cannot hold the lock across the whole read-modify-write —
        /// GitHub sync does network I/O in the middle, and holding the lock over that would
        /// make the other process's autosave start failing. Optimistic concurrency turns a
        /// silent overwrite into a retryable miss.
        /// </summary>
        public bool TrySaveIfUnchanged(StoreData data, Guid expectedRevision)
        {
            using var _ = AcquireLock();
            // PeekRevision reads Save.json, not the lock file, so it is safe under the lock.
            // Missing and v1 files both peek as null and load as Revision = Guid.Empty, so
            // mapping null that way keeps the first save through this path working.
            if ((PeekRevision() ?? Guid.Empty) != expectedRevision)
                return false;
            SaveUnlocked(data);
            return true;
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
                NormalizeTasks(migrated);
                ClearBackfilledCreatedAt(migrated, json);
                Services.Trash.Purge(migrated);
                return migrated;
            }

            var data = JsonSerializer.Deserialize<StoreData>(json, CoreJson.Options) ?? new StoreData();
            // Drop recent ids that no longer resolve to a project.
            data.RecentProjectIds.RemoveAll(id => data.Projects.All(p => p.Id != id));
            NormalizeColumns(data);
            NormalizeTasks(data);
            ClearBackfilledCreatedAt(data, json);
            // Retention is enforced on load rather than on a timer, so it applies in both
            // processes and to files written by hand or by the MCP server. The purge is
            // persisted by whatever save comes next.
            Services.Trash.Purge(data);
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
                NormalizeColumns(project);
        }

        public static void NormalizeColumns(ProjectModel project)
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

        /// <summary>
        /// Enforces task-level invariants on every load path, alongside
        /// <see cref="NormalizeColumns(StoreData)"/>: an unrecognised recurrence string
        /// becomes "none", and the interval is at least 1.
        ///
        /// Without this, a hand-edited or externally written recurrence value leaves the
        /// task reporting IsRecurring while Recurrence.SpawnNextIfRecurring refuses to
        /// spawn — a recurring task that silently never recurs.
        /// </summary>
        public static void NormalizeTasks(StoreData data)
        {
            foreach (var project in data.Projects)
                NormalizeTasks(project);
        }

        public static void NormalizeTasks(ProjectModel project)
        {
            foreach (var task in project.Tasks.Concat(project.Trash.Select(t => t.Task)))
            {
                task.Recurrence = RecurrenceRules.Normalize(task.Recurrence);
                if (task.RecurrenceInterval < 1)
                    task.RecurrenceInterval = 1;
            }
        }

        /// <summary>
        /// Undoes the constructor's CreatedAtUtc default for tasks whose stored JSON has
        /// no CreatedAtUtc.
        ///
        /// TaskModel stamps CreatedAtUtc on construction so no creation path can forget it.
        /// But System.Text.Json runs that constructor and then overwrites only the
        /// properties present in the file, so every task saved before the field existed
        /// would silently acquire its load time as a creation date — and the next autosave
        /// would persist that, rewriting creation history for the whole store. Nulling
        /// them back out keeps "unknown" honest. Self-terminating: CoreJson writes nulls
        /// as absent, so these stay absent on the next save.
        /// </summary>
        private static void ClearBackfilledCreatedAt(StoreData data, string json)
        {
            HashSet<Guid> missing;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                // v2 envelope: { "projects": [...] }. v1: a bare array of projects.
                var projects = root.ValueKind == JsonValueKind.Array
                    ? root
                    : root.TryGetProperty("projects", out var p) ? p : default;
                if (projects.ValueKind != JsonValueKind.Array)
                    return;

                missing = new HashSet<Guid>();
                foreach (var project in projects.EnumerateArray())
                {
                    CollectMissingCreatedAt(project, "Tasks", missing);
                    if (project.TryGetProperty("Trash", out var trash) && trash.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var entry in trash.EnumerateArray())
                        {
                            if (entry.TryGetProperty("Task", out var task))
                                CollectTaskId(task, missing);
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // The file already deserialized, so this should not happen; if it somehow
                // does, leaving the defaults alone is better than failing the load.
                return;
            }

            if (missing.Count == 0)
                return;

            foreach (var project in data.Projects)
            {
                foreach (var task in project.Tasks.Concat(project.Trash.Select(t => t.Task)))
                {
                    if (missing.Contains(task.Id))
                        task.CreatedAtUtc = null;
                }
            }
        }

        private static void CollectMissingCreatedAt(JsonElement owner, string arrayName, HashSet<Guid> missing)
        {
            if (!owner.TryGetProperty(arrayName, out var array) || array.ValueKind != JsonValueKind.Array)
                return;
            foreach (var task in array.EnumerateArray())
                CollectTaskId(task, missing);
        }

        private static void CollectTaskId(JsonElement task, HashSet<Guid> missing)
        {
            if (task.ValueKind != JsonValueKind.Object)
                return;
            // Present-but-null counts as missing: CoreJson omits nulls, but a hand-edited
            // file may spell it out.
            var hasCreated = task.TryGetProperty("CreatedAtUtc", out var created)
                             && created.ValueKind != JsonValueKind.Null;
            if (hasCreated)
                return;
            if (task.TryGetProperty("Id", out var id) && id.TryGetGuid(out var guid))
                missing.Add(guid);
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

        private void SaveUnlocked(StoreData data) => WriteUnlocked(Prepare(data));

        private void WriteUnlocked(PreparedSave prepared)
        {
            Directory.CreateDirectory(BaseDirectory);

            var tmpPath = SaveFilePath + ".tmp";
            File.WriteAllText(tmpPath, prepared.Json);

            RotateBackups();
            File.Move(tmpPath, SaveFilePath, overwrite: true);
            LastWrittenRevision = prepared.Revision;

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
            throw new StoreLockedException($"Could not acquire store lock at '{LockFilePath}'.", last);
        }
    }
}
