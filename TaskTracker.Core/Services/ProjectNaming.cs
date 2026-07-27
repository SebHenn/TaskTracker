using TaskTracker.Core.Models;

namespace TaskTracker.Core.Services
{
    /// <summary>
    /// The one place that decides whether a project may be called something.
    ///
    /// Creation checked for duplicates and rename did not, so a rename could quietly
    /// produce two projects with the same name — and then the sidebar showed two
    /// identical rows, and the v1 recents migration, which resolves by name, could not
    /// tell them apart.
    /// </summary>
    public static class ProjectNaming
    {
        /// <summary>
        /// Whether <paramref name="name"/> is usable: not blank, and not already taken.
        /// </summary>
        /// <param name="excluding">
        /// The project being renamed, so keeping its own name counts as available rather
        /// than as a clash with itself.
        /// </param>
        public static bool IsAvailable(IEnumerable<ProjectModel> projects, string? name, ProjectModel? excluding = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            // Compared trimmed and case-insensitively: "Work" and "work " are the same
            // project to a person reading the sidebar, whatever the string comparer says.
            var candidate = name.Trim();
            return !projects.Any(project =>
                !ReferenceEquals(project, excluding) &&
                string.Equals(project.Name.Trim(), candidate, StringComparison.OrdinalIgnoreCase));
        }
    }
}
