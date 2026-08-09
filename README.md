# TaskTracker

A Jira-style desktop task tracker for Windows (WPF, .NET 8) with GitHub issue
synchronisation and an MCP server that lets Claude Code (or any MCP client)
read and manage your projects.

## Features

- **Projects & Kanban board with custom columns** — define any columns per
  project (default Backlog / In Progress / Done), flag which ones count as
  "done", set optional WIP limits, and drag cards between columns *and* to a
  specific position within one; projects can carry a color
- **Recurring tasks** — daily/weekly/monthly repeats; completing one schedules
  the next occurrence automatically
- **Quick capture & alerts** — global Ctrl+Alt+T opens a mini add-task window
  from anywhere; the tray icon notifies when tasks become due or overdue, and
  optionally when a background sync pulls in new GitHub issues (clicking the
  notification opens the project)
- **Optional auto-start** — launch TaskTracker with Windows, minimised to the
  notification area
- **Activity journal & time tracking** — timestamped notes per task and a
  start/stop work timer with accumulated time (exported to CSV)
- **Due dates, priorities, labels** — tasks carry an optional due date
  (overdue tasks are highlighted), a Low/Medium/High priority shown as a
  colored card edge, and free-form labels; the board filters by any
  combination of label, due bucket (overdue / today / this week / undated)
  and priority
- **Bulk actions** — select several cards in a column and move or delete them
  in one go
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
- **Statistics & weekly review** — per-project open/done/overdue counts,
  completion percent and a tasks-completed-per-week chart, plus a "this week"
  section on the home page summarising what was completed and created across
  every project in the last seven days
- **GitHub issue sync (two-way)** — link a project to a repository; open
  issues import as tasks, unfinished tasks are filed as new issues, and
  completing a task closes the issue and vice versa
- **MCP server** — `TaskTracker.Mcp` exposes the same data over the Model
  Context Protocol so Claude Code can manage projects, tasks, columns, and
  checklists
- **Safety** — autosave with atomic writes and three rolling backups,
  project-delete confirmation, single-instance guard; deleted tasks go to a
  per-project trash (30-day retention) so they stay recoverable long after
  the undo bar has gone; external edits to the save file are picked up live
- **Export/import** — JSON round-trip (safe to re-import) and CSV export
- **Keyboard & accessibility** — arrow keys move between cards, Ctrl+←/→
  moves a card between columns, Enter opens it and Delete trashes it; lanes
  and cards expose accessible names to screen readers
- **Shortcuts** — Ctrl+N new project, Ctrl+T new task, Ctrl+F search,
  Esc closes the detail panel; window placement is remembered
- **Dark & light theme, English & German UI**

## Solution layout

| Project | Target | Purpose |
|---|---|---|
| `TaskTracker` | `net8.0-windows` | WPF desktop app (Windows only) |
| `TaskTracker.Core` | `net8.0` | Models, storage, search, stats, GitHub sync — cross-platform |
| `TaskTracker.Mcp` | `net8.0` | MCP stdio server for Claude Code — cross-platform |
| `TaskTracker.Core.Tests` | `net8.0` | xunit tests for Core and the MCP handlers |

## Installing

Tagged releases (`v*`) publish self-contained single-file Windows builds of
the app and the MCP server on the GitHub Releases page — no .NET install
required. Download `TaskTracker-win-x64.zip`, unzip, run `TaskTracker.exe`.

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

Nothing is written into the repository, and no data leaves your machine
except the GitHub API calls you trigger yourself.

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
   card), and unfinished tasks that have no issue yet are filed as new ones.
   After that, sync is two-way: closing an issue marks the task done, marking
   a task done closes the issue, with last-write-wins on conflicts. Title and
   labels of linked tasks follow GitHub.

Only unfinished tasks are filed, mirroring the import direction — linking a
board you have been keeping for a while exports the work still open on it and
leaves its finished history alone. A task whose issue is deleted on GitHub is
unlinked and then left alone, rather than filed again on the next sync; **Push
to GitHub** on the card files it again if that is what you want.

Optionally enable auto-sync in Settings to refresh linked projects every
15 minutes while the app runs. With auto-sync on, **Notify me when new issues
arrive** shows a tray notification naming the issue (or how many arrived);
clicking it opens the project. Only background syncs notify — a sync you
started yourself reports into the board you are already looking at.

## Claude Code / MCP

The repository ships a `.mcp.json` that registers the `tasktracker` MCP
server via `dotnet run`. Open the repo in Claude Code and approve the server,
then ask things like *"what's still open in project X?"* or *"add a task to
prepare the release notes, due Friday, high priority"*.

Available tools: `list_projects`, `get_project`, `create_project`,
`update_project`, `delete_project`, `project_stats`, `list_tasks`,
`create_task`, `create_tasks` (bulk), `update_task`, `move_task`,
`delete_task`, `add_subtask`, `update_subtask`, `add_note`, `search_tasks`,
`due_overview`, `weekly_review`, `github_sync`.

### Using TaskTracker from another repository

The `.mcp.json` above resolves `--project TaskTracker.Mcp` relative to the
working directory, so it only works inside a clone of this repository. To drive
your boards from any other project, install the server as a global tool:

```
dotnet pack TaskTracker.Mcp -c Release          # writes artifacts/nupkg
dotnet tool install --global --add-source artifacts/nupkg SebHenn.TaskTracker.Mcp
```

Then register it in the other repository — no paths, so this `.mcp.json` is
safe to commit and works on every machine that has the tool installed:

```json
{
  "mcpServers": {
    "tasktracker": { "type": "stdio", "command": "tasktracker-mcp", "args": [] }
  }
}
```

Both processes read the same `Documents/TaskTracker/` files, so tasks filed
from another repository show up in the running app immediately. Upgrade later
with `dotnet tool update --global --add-source artifacts/nupkg
SebHenn.TaskTracker.Mcp`.

The alternative is to publish the server once
(`dotnet publish TaskTracker.Mcp -c Release`) and point `.mcp.json` at the
resulting executable by absolute path. That also avoids the `dotnet run`
startup cost, but the path is machine-specific and cannot be shared.

## Verification status

Core logic (storage, board projection, filtering, trash, bulk actions, sync,
search, stats, MCP handlers) is covered by 272 unit tests and runs on any OS.
CI builds the whole solution on Linux, runs those tests, and gates on
`dotnet format`.

The WPF UI is validated by compilation only in CI, so a manual pass on a
Windows machine is expected after UI changes: theme switching at several DPI
scalings, drag & drop, the dialogs, and the sync button.

## Contributing

Issues and pull requests are welcome — see [CONTRIBUTING.md](CONTRIBUTING.md)
for the build, the project layout, and the gotchas worth knowing before you
start. Two things CI checks, so run them first:

```
dotnet test TaskTracker.Core.Tests
dotnet format TaskTracker.sln --verify-no-changes
```

Participation is covered by our [Code of Conduct](CODE_OF_CONDUCT.md). Security
problems go through [SECURITY.md](SECURITY.md) rather than a public issue.
Release history is in [CHANGELOG.md](CHANGELOG.md).

## License

[MIT](LICENSE) — © 2026 Sebastian Henn
