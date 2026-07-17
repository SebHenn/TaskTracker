# TaskTracker

A Jira-style desktop task tracker for Windows (WPF, .NET 8) with GitHub issue
synchronisation and an MCP server that lets Claude Code (or any MCP client)
read and manage your projects.

## Features

- **Projects & Kanban board with custom columns** — define any columns per
  project (default Backlog / In Progress / Done), flag which ones count as
  "done", and drag tasks between them
- **Due dates, priorities, labels** — tasks carry an optional due date
  (overdue tasks are highlighted), a Low/Medium/High priority shown as a
  colored card edge, and free-form labels with per-project filtering
- **Subtasks & task details** — click a card for a detail panel with a
  multi-line description, checklist (progress shown on the card), metadata,
  and quick edits
- **Today dashboard & reminders** — the home page lists overdue / due today /
  due this week across all projects; a tray icon shows a reminder balloon on
  startup
- **Cross-project search** — search box in the sidebar finds tasks by title,
  description, or label across all projects
- **Favourites, archive & recents** — pin favourites, archive finished
  projects, and jump back into the five most recent ones from the home page
- **Statistics** — per-project open/done/overdue counts, completion percent,
  and a tasks-completed-per-week chart
- **GitHub issue sync (two-way)** — link a project to a repository; open
  issues import as tasks, completing a task closes the issue and vice versa,
  and local tasks can be pushed to GitHub as new issues
- **MCP server** — `TaskTracker.Mcp` exposes the same data over the Model
  Context Protocol so Claude Code can manage projects, tasks, columns, and
  checklists
- **Safety** — autosave with atomic writes and three rolling backups,
  project-delete confirmation, task-delete undo, single-instance guard;
  external edits to the save file are picked up live
- **Export/import** — JSON round-trip (safe to re-import) and CSV export
- **Shortcuts** — Ctrl+N new project, Ctrl+T new task, Ctrl+F search,
  Esc closes the detail panel; window placement is remembered
- **Dark & light theme, English & German UI**

## Solution layout

| Project | Target | Purpose |
|---|---|---|
| `TaskTracker` | `net8.0-windows` | WPF desktop app (Windows only) |
| `TaskTracker.Core` | `net8.0` | Models, storage, search, stats, GitHub sync — cross-platform |
| `TaskTracker.Mcp` | `net8.0` | MCP stdio server for Claude Code — cross-platform |
| `TaskTracker.Core.Tests` | `net8.0` | xunit tests for Core |

## Building

On Windows:

```
dotnet build TaskTracker.sln
```

On Linux/macOS the WPF app can be *compiled* (not run) with the
Microsoft-built .NET SDK:

```
dotnet build TaskTracker.sln -p:EnableWindowsTargeting=true
```

> Note: distro-packaged SDKs (e.g. Ubuntu's `dotnet-sdk-8.0`) do not ship the
> WindowsDesktop targets; build the cross-platform projects individually
> (`dotnet build TaskTracker.Core TaskTracker.Mcp` / `dotnet test`) there and
> let CI validate the WPF head.

Run tests with `dotnet test TaskTracker.Core.Tests`.

## Data & settings

All data lives in `Documents/TaskTracker/`:

- `Save.json` — all projects and tasks (versioned envelope; v1 files from
  older builds are migrated automatically). `Save.json.bak1`–`.bak3` are
  rolling backups.
- `Settings.json` — theme, language, GitHub token, auto-sync flag.

The WPF app and the MCP server share these files safely via a lock file and
atomic writes; the app reloads live when the MCP server changes anything.

## GitHub issue synchronisation

1. Create a GitHub Personal Access Token with issue access (classic `repo`
   scope, or a fine-grained token with *Issues: Read and write*).
2. Paste it in **Settings → GitHub**. On Windows it is stored encrypted with
   DPAPI; on other systems it is stored base64-encoded with a warning.
3. Open a project, click **Link to GitHub**, and enter the repository's owner
   and name.
4. Click **Sync**. Open issues import as tasks (issue number shown on the
   card). After that, sync is two-way: closing an issue marks the task done,
   marking a task done closes the issue, with last-write-wins on conflicts.
   Title and labels of linked tasks follow GitHub.

Optionally enable auto-sync in Settings to refresh linked projects every
15 minutes while the app runs.

## Claude Code / MCP

The repository ships a `.mcp.json` that registers the `tasktracker` MCP
server via `dotnet run`. Open the repo in Claude Code and approve the server,
then ask things like *"what's still open in project X?"* or *"add a task to
prepare the release notes, due Friday, high priority"*.

Available tools: `list_projects`, `get_project`, `create_project`,
`update_project`, `delete_project`, `project_stats`, `list_tasks`,
`create_task`, `update_task`, `move_task`, `delete_task`, `add_subtask`,
`update_subtask`, `search_tasks`, `due_overview`, `github_sync`.

For faster startup you can publish the server once
(`dotnet publish TaskTracker.Mcp -c Release`) and point `.mcp.json` at the
resulting executable.

## Verification status

Core logic (storage, sync, search, stats, MCP handlers) is covered by unit
tests and runs on any OS. The WPF UI is validated by compilation in CI; a
manual pass on a Windows machine is recommended after UI changes
(theme switch, drag & drop, dialogs, sync button).
