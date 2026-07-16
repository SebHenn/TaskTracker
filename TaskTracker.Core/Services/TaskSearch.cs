using TaskTracker.Core.Models;

namespace TaskTracker.Core.Services
{
    public record SearchResult(ProjectModel Project, TaskModel Task);

    /// <summary>
    /// Cross-project task search shared by the WPF search page and the MCP
    /// server's search_tasks tool.
    /// </summary>
    public static class TaskSearch
    {
        public static List<SearchResult> Search(
            IEnumerable<ProjectModel> projects,
            string query,
            string? label = null,
            bool includeDone = true,
            bool includeArchived = false)
        {
            var results = new List<SearchResult>();
            var trimmed = query?.Trim() ?? "";

            foreach (var project in projects)
            {
                if (!includeArchived && project.IsArchived)
                    continue;

                foreach (var task in project.Tasks)
                {
                    if (!includeDone && task.IsDone)
                        continue;
                    if (label != null && !task.Labels.Contains(label, StringComparer.OrdinalIgnoreCase))
                        continue;
                    if (trimmed.Length > 0 && !Matches(task, trimmed))
                        continue;
                    results.Add(new SearchResult(project, task));
                }
            }

            return results;
        }

        private static bool Matches(TaskModel task, string query) =>
            task.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
            || task.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
            || task.Labels.Any(l => l.Contains(query, StringComparison.OrdinalIgnoreCase));
    }
}
