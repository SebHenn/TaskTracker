using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

var builder = Host.CreateApplicationBuilder(args);

// stdout carries the MCP protocol — all logging must go to stderr.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new Implementation
        {
            Name = "tasktracker",
            Title = "TaskTracker",
            // Read from the assembly rather than written out, so it cannot drift from
            // <Version> in the csproj. An installed copy of this tool is a snapshot;
            // store_info reports this value so a stale one is diagnosable.
            Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0",
        };

        // Shown to the client automatically, unlike a resource the user must attach.
        // Everything here is something a caller otherwise gets wrong on the first try.
        options.ServerInstructions = """
            TaskTracker is a Kanban task manager. Its desktop app and this server share one
            set of files, so changes made here show up live in the app and vice versa.

            Working with it:
            - Ids are GUIDs. Get project ids from list_projects and task ids from list_tasks
              or search_tasks; do not construct or abbreviate them.
            - Done-ness derives from the board column, not a separate flag. Complete a task
              with update_task(isDone: true) or move_task to a done column — both keep the
              column and the flag in agreement.
            - Completing a recurring task automatically creates its next occurrence.
            - delete_task is recoverable: it moves the task to the project's trash for 30
              days. delete_project is not, and requires confirm=true.
            - List and search results are paged and return compact tasks without
              descriptions. Pass detail=full when you need the body, and use limit/offset
              to page — the response reports the unpaged total.
            - Recurrence is one of none, daily, weekly, monthly.
            """;
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly()
    .WithPromptsFromAssembly();

await builder.Build().RunAsync();
