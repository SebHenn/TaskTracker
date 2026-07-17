namespace TaskTracker.Core.Models
{
    public static class BoardColumnDefaults
    {
        /// <summary>Columns given to pre-columns projects on load, matching the old fixed board.</summary>
        public static List<BoardColumn> MigrationColumns() => new()
        {
            new BoardColumn { Name = "In Progress" },
            new BoardColumn { Name = "Done", IsDoneColumn = true },
        };

        /// <summary>Columns for newly created projects.</summary>
        public static List<BoardColumn> NewProjectColumns() => new()
        {
            new BoardColumn { Name = "Backlog" },
            new BoardColumn { Name = "In Progress" },
            new BoardColumn { Name = "Done", IsDoneColumn = true },
        };
    }
}
