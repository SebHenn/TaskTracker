using System.Text;
using System.Text.Json;
using TaskTracker.Core.Models;
using TaskTracker.Core.Storage;

namespace TaskTracker.Core.Services
{
    /// <summary>JSON/CSV export and JSON import for data portability.</summary>
    public static class ProjectPorter
    {
        public static void ExportJson(IEnumerable<ProjectModel> projects, string path)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(projects.ToList(), CoreJson.Options));
        }

        public static void ExportCsv(IEnumerable<ProjectModel> projects, string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Project,Title,Description,Column,Done,Priority,DueDate,Labels,CreatedAtUtc,CompletedAtUtc,TrackedHours");
            foreach (var project in projects)
            {
                foreach (var task in project.Tasks)
                {
                    sb.AppendLine(string.Join(',',
                        Csv(project.Name),
                        Csv(task.Title),
                        Csv(task.Description),
                        Csv(project.ColumnOf(task)?.Name ?? ""),
                        task.IsDone ? "true" : "false",
                        task.Priority.ToString(),
                        task.DueDate?.ToString("yyyy-MM-dd") ?? "",
                        Csv(string.Join(";", task.Labels)),
                        task.CreatedAtUtc?.ToString("O") ?? "",
                        task.CompletedAtUtc?.ToString("O") ?? "",
                        (task.TrackedSeconds / 3600).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));
                }
            }
            File.WriteAllText(path, sb.ToString());
        }

        /// <summary>
        /// Imports projects from a JSON export. All ids are regenerated (with
        /// task→column references remapped) so importing into a store that
        /// already contains the same data cannot collide; names are deduped
        /// with a numeric suffix.
        /// </summary>
        public static List<ProjectModel> ImportJson(string path, IEnumerable<ProjectModel> existing)
        {
            var imported = JsonSerializer.Deserialize<List<ProjectModel>>(File.ReadAllText(path), CoreJson.Options)
                           ?? new List<ProjectModel>();
            var takenNames = new HashSet<string>(existing.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);

            foreach (var project in imported)
            {
                project.Id = Guid.NewGuid();
                project.Name = Dedupe(project.Name, takenNames);
                takenNames.Add(project.Name);

                var columnIdMap = new Dictionary<Guid, Guid>();
                foreach (var column in project.Columns)
                {
                    var newId = Guid.NewGuid();
                    columnIdMap[column.Id] = newId;
                    column.Id = newId;
                }

                foreach (var task in project.Tasks)
                {
                    task.Id = Guid.NewGuid();
                    task.ColumnId = task.ColumnId.HasValue && columnIdMap.TryGetValue(task.ColumnId.Value, out var mapped)
                        ? mapped
                        : null; // normalization assigns a column by done-state
                    foreach (var subTask in task.SubTasks)
                        subTask.Id = Guid.NewGuid();
                }
            }

            return imported;
        }

        private static string Dedupe(string name, HashSet<string> taken)
        {
            if (!taken.Contains(name))
                return name;
            for (var i = 2; ; i++)
            {
                var candidate = $"{name} ({i})";
                if (!taken.Contains(candidate))
                    return candidate;
            }
        }

        private static string Csv(string value)
        {
            if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
                return $"\"{value.Replace("\"", "\"\"")}\"";
            return value;
        }
    }
}
