using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using TaskTracker.Core.Models;
using TaskTracker.Core.Services;
using TaskTracker.Core.Storage;

namespace TaskTracker.Mcp;

/// <summary>
/// Board column management. The rules live in Core.ColumnOperations, shared with the
/// desktop's column dialog, so both heads refuse the same edits.
/// </summary>
public static partial class TaskTrackerTools
{
    [McpServerTool(Name = "list_columns", ReadOnly = true, Title = "List board columns"),
     Description("List a project's board columns in order, with WIP limits and task counts.")]
    public static string ListColumns(
        [Description("Project id (GUID from list_projects)")] string projectId)
    {
        var data = LoadData();
        var project = FindProject(data, projectId);
        return ToJson(project.Columns.Select((c, i) => new
        {
            c.Id,
            Position = i,
            c.Name,
            c.IsDoneColumn,
            c.WipLimit,
            TaskCount = project.Tasks.Count(t => project.ColumnOf(t)?.Id == c.Id),
        }));
    }

    [McpServerTool(Name = "add_column", Idempotent = false, Title = "Add a board column"),
     Description("Add a column to a project's board.")]
    public static string AddColumn(
        [Description("Project id (GUID from list_projects)")] string projectId,
        [Description("Column name")] string name,
        [Description("Tasks in this column count as done (default false)")] bool isDoneColumn = false,
        [Description("Zero-based position; omit to append")] int? position = null,
        [Description("WIP limit; omit or 0 for no limit")] int? wipLimit = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new McpException("Column name must not be empty.");

        BoardColumn? added = null;
        UpdateData(data =>
        {
            var project = FindProject(data, projectId);
            added = ColumnOperations.Add(project, name, isDoneColumn, position, wipLimit);
        });
        return ToJson(new { added!.Id, added.Name, added.IsDoneColumn, added.WipLimit });
    }

    [McpServerTool(Name = "update_column", Idempotent = true, Title = "Update a board column"),
     Description("Rename a column, change whether it counts as done, or set its WIP limit.")]
    public static string UpdateColumn(
        [Description("Column id (GUID from list_columns)")] string columnId,
        [Description("New name")] string? name = null,
        [Description("Whether tasks here count as done")] bool? isDoneColumn = null,
        [Description("New WIP limit, or 0 to remove the limit")] int? wipLimit = null)
    {
        BoardColumn? column = null;
        UpdateData(data =>
        {
            var (project, found) = FindColumnById(data, columnId);
            column = found;
            var refusal = ColumnOperations.Update(
                project, found, name, isDoneColumn,
                wipLimit, clearWipLimit: wipLimit is <= 0);
            if (refusal != null)
                throw new McpException(refusal);
        });
        return ToJson(new { column!.Id, column.Name, column.IsDoneColumn, column.WipLimit });
    }

    [McpServerTool(Name = "delete_column", Idempotent = true, Title = "Delete a board column"),
     Description("Delete a column, moving its tasks to another column. Refused if it is the last column or the last done column.")]
    public static string DeleteColumn(
        [Description("Column id (GUID from list_columns)")] string columnId,
        [Description("Column (name or GUID) to move the tasks to; defaults to the first open column")] string? moveTasksTo = null)
    {
        var moved = 0;
        string? deletedName = null;
        string? target = null;

        UpdateData(data =>
        {
            var (project, column) = FindColumnById(data, columnId);
            deletedName = column.Name;
            var destination = moveTasksTo == null ? null : FindColumn(project, moveTasksTo);
            moved = project.Tasks.Count(t => t.ColumnId == column.Id);

            var refusal = ColumnOperations.Delete(project, column, destination);
            if (refusal != null)
                throw new McpException(refusal);

            target = (destination ?? project.FirstColumn)?.Name;
        });

        return ToJson(new { deleted = true, column = deletedName, tasksMoved = moved, movedTo = target });
    }

    [McpServerTool(Name = "reorder_columns", Idempotent = true, Title = "Reorder board columns"),
     Description("Set the left-to-right order of a project's columns. Provide every column id exactly once.")]
    public static string ReorderColumns(
        [Description("Project id (GUID from list_projects)")] string projectId,
        [Description("Column ids (GUIDs) in the desired order — all of them")] string[] columnIds)
    {
        var ordered = (columnIds ?? Array.Empty<string>())
            .Select(id => Guid.TryParse(id, out var g)
                ? g
                : throw new McpException($"'{id}' is not a valid column id (expected a GUID)."))
            .ToList();

        List<string> names = new();
        UpdateData(data =>
        {
            var project = FindProject(data, projectId);
            var refusal = ColumnOperations.Reorder(project, ordered);
            if (refusal != null)
                throw new McpException(refusal);
            names = project.Columns.Select(c => c.Name).ToList();
        });

        return ToJson(new { reordered = true, columns = names });
    }

    private static (ProjectModel Project, BoardColumn Column) FindColumnById(StoreData data, string columnId)
    {
        if (!Guid.TryParse(columnId, out var id))
            throw new McpException($"'{columnId}' is not a valid column id (expected a GUID).");
        foreach (var project in data.Projects)
        {
            var column = project.Columns.FirstOrDefault(c => c.Id == id);
            if (column != null)
                return (project, column);
        }
        throw new McpException($"No column with id {columnId}. Use list_columns to see a project's columns.");
    }
}
