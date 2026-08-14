# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Solution layout

| Project | Target | Purpose |
|---|---|---|
| `TaskTracker` | `net8.0-windows` | WPF desktop app (Windows only) |
| `TaskTracker.Core` | `net8.0` | Models, storage, search, stats, GitHub sync — cross-platform |
| `TaskTracker.Mcp` | `net8.0` | MCP stdio server for Claude Code — cross-platform |
| `TaskTracker.Core.Tests` | `net8.0` | xunit tests for Core and the MCP handlers |

**Core and Mcp must stay free of WPF/WinForms types.** That split is what makes the logic testable and
CI-runnable on Linux; anything worth a unit test belongs in Core, not in the WPF head. See `README.md` for the
user-facing feature list and GitHub-sync setup.

## Commands

```
dotnet build TaskTracker.sln                            # whole solution
dotnet test TaskTracker.Core.Tests                      # 272 tests, ~250ms
dotnet test TaskTracker.Core.Tests --filter FullyQualifiedName~ProjectStoreTests   # one class
dotnet test TaskTracker.Core.Tests --filter "DisplayName~migrates"                 # one test
dotnet format TaskTracker.sln --verify-no-changes        # CI gates on this; run before committing
dotnet run --project TaskTracker\TaskTracker.csproj     # launches the GUI (blocks — run in background)
```

- Baseline on a clean tree: **0 errors, 0 warnings, 272 tests passing**. Only new warnings are yours.
- `Core` and `Mcp` build with `TreatWarningsAsErrors`; the WPF head does not, but is warning-free — keep it that way.
- New files written by tooling often lack the UTF-8 BOM the rest of the tree has, and `dotnet format` adds it.
  Run the format check before committing or CI fails on files that compile fine.
- Non-Windows (and CI) needs `-p:EnableWindowsTargeting=true` to compile the WPF head; distro-packaged SDKs can't,
  so build `TaskTracker.Core TaskTracker.Mcp` individually there. `dotnet format` takes no `-p:`, so it reads the
  same setting from the `EnableWindowsTargeting` environment variable instead.
- CI (`.github/workflows/ci.yml`) runs on ubuntu: Release build of the whole solution, `dotnet test --no-build`,
  then the format check.
- MSBuild output on this machine is localized to German (`Fehler` = error, `Warnung` = warning). Prefix with
  `$env:DOTNET_CLI_UI_LANGUAGE = 'en'` for English.

## Architecture

### Two processes, one store

The desktop app and the MCP server are separate processes over the same files in `Documents/TaskTracker/`. Every
rule below exists to keep that safe.

`TaskTracker.Core/Storage/ProjectStore.cs` is the **only** gateway to `Save.json`: atomic writes (tmp + move),
three rolling backups, a cross-process `store.lock` file, and a fresh `revision` GUID per write. A bare JSON array
is a v1 file and migrates to the v2 envelope on load. Out-of-process callers mutate via `Update(mutate)`, which
does lock → load → mutate → save in one lock.

**Autosave, not explicit saves.** `ChangeTracker` deep-subscribes to the whole model graph (project, task, column,
and subtask property changes plus every nested collection) and raises one `Changed` event; `ProjectsService`
debounces that into a save 500 ms later. Mutating any model property persists on its own — don't add save calls.

**Live reload.** A `FileSystemWatcher` on `Save.json` (300 ms debounce) compares `PeekRevision()` against
`LastWrittenRevision` to ignore the app's own writes, then refills `projectModels` **in place** so bindings
survive, and broadcasts `StoreReloadedMessage`. `MainViewModel`, `HomeViewModel`, and `ProjectViewModel` each
handle it — any new view model holding derived copies of store data must too.

UI-only state must be excluded from both persistence *and* change tracking: `ProjectModel.IsSelected` is
`[JsonIgnore]` **and** listed in `ChangeTracker.IgnoredProperties`. Miss either and selection changes cause save
storms or leak into the file.

### Board model

Each project owns ordered `BoardColumn`s. `ProjectStore.NormalizeColumns` runs on every load path and guarantees
the invariants — at least one column, at least one done column, every task pointing at a real column — so
hand-edited and externally written files are safe to load.

**Deleting goes through `Core/Services/Trash.cs`, not `project.Tasks.Remove`.** It moves the task into
`ProjectModel.Trash` (newest first, 30-day retention, 200 entries per project), stops any running timer, and
returns the entry that `Trash.Restore` takes back. `ProjectStore` purges past-retention entries on every load
path, so both processes enforce it. `project.Tasks.Remove` on its own loses the task outright.

`ProjectModel.MoveTaskToColumn` is the single place task placement happens: it sets `ColumnId`, derives `IsDone`
from the column, and spawns the next occurrence via `Recurrence.SpawnNextIfRecurring`
(`Core/Services/RecurrenceRules.cs`), which it returns. Setting `task.IsDone` or `task.ColumnId` directly
bypasses recurrence and completion timestamps. `task.ColumnId` is nullable and may be stale — resolve it with
`project.ColumnOf(task)`.

**When you know the done-state but not the column, use `Core/Services/TaskCompletion.SetDone(project, task,
value)`** — never `task.IsDone = value`. It picks `FirstDoneColumn`/`FirstColumn`, repairs the board with
`NormalizeColumns` if neither exists, and delegates to `MoveTaskToColumn`. Three callers previously resolved the
column themselves and fell back to `task.IsDone = value`, which silently dropped the next occurrence of every
recurring task completed through GitHub sync or the MCP server.

### MCP server

`TaskTracker.Mcp` is a stdio server; tools are static `[McpServerTool]` methods in `TaskTrackerTools.cs`, each
stateless (lock → load → mutate → save), which is why a running app picks changes up live. **stdout carries the
protocol** — logging goes to stderr, never `Console.WriteLine`. `CreateStore` / `CreateSettingsStore` are the
test seams. `.mcp.json` registers the server via `dotnet run`, which resolves only inside a clone of this
repo; consumers elsewhere install it as the global tool `tasktracker-mcp` (`PackAsTool` in the csproj,
`dotnet pack` → `artifacts/nupkg`), so their `.mcp.json` carries no machine-specific path. It targets
`net8.0` with `RollForward=LatestMajor` — without that it refuses to start on a machine whose only runtime
is newer. That installed tool is a **snapshot** — editing this repo does not change it, and refreshing it needs
both a `<Version>` bump (`dotnet tool update` no-ops on an equal version) and every running `tasktracker-mcp`
closed (Windows locks the old files, and each registered client owns a process). README's *Refreshing the
installed tool* has the recipe; neither failure is loud, so a stale server looks like a broken tool.

### GitHub sync

`Core/GitHub/` — `IGitHubApi` is the seam that makes sync testable without network. Tokens go through
`TokenProtector` (DPAPI on Windows, base64 elsewhere). Conflicts resolve last-write-wins.

`SyncAsync` runs both directions: issues become tasks, then unlinked tasks become issues. The export pass
is last on purpose — the unlink pass ahead of it only knows the issue numbers the API listed, so exporting
earlier would unlink the issues the same sync just filed. It mirrors the import rule (only open issues
import, so only not-done tasks export) and skips `TaskModel.GitHubIssueVanished`, the mark left when an
issue is deleted on GitHub — without it, every sync re-files the issue the user just deleted. Its failures
are counted into `SyncResult.ExportError` instead of thrown: created issues live in memory until the caller
saves, so aborting mid-pass would drop the links and file duplicates next time.

**Issue bodies are never written after creation** — the API surface is create / rename / open / close on
purpose. Bots that file issues (and GitHub's own tooling) identify their own by a trailing HTML comment in the
body, so overwriting one makes the bot stop matching and file duplicates, silently. Any future body write must
run the outgoing body through `IssueBodyMarkers.Preserve` with the freshly fetched body — not through
`task.Description`, which `TrimBody` has already truncated past the marker.

### WPF head

Navigation is ViewModel-first: `NavigationService.CurrentView` holds a *ViewModel instance*, `MainWindow` binds a
`ContentControl` to it, and `App.xaml` `DataTemplate`s map VM type → View. A new page means four edits: VM, View,
`DataTemplate`, DI registration in `App.xaml.cs`. Page VMs are singletons (state survives navigation); dialogs and
their VMs are transient. Page VMs reach shell state through `WeakReferenceMessenger`, not a shell reference.

## Conventions and gotchas

- **MVVM Toolkit generators**: `[ObservableProperty] private T _foo` → `Foo`; `[RelayCommand] void OnDoThing()` →
  **`DoThingCommand`** — the leading `On` is stripped. Bind the trimmed name in XAML.
- **`UseWindowsForms` is on** (tray icon), so the csproj removes the `System.Windows.Forms` and `System.Drawing`
  implicit usings — otherwise every bare `Control`, `Application`, `Point` becomes ambiguous. New WinForms usage
  must be fully qualified, as `TrayService` does.
- **ContextMenu bindings**: a `ContextMenu` is outside the visual tree. The working pattern binds the item's `Tag`
  to the `ItemsControl` DataContext and menu items to `PlacementTarget.Tag.<X>Command` (see `ProjectView.xaml`).
- **Assets** ship only if listed as `<Resource>` in `TaskTracker.csproj`; adding the file is not enough.
- The WPF UI is validated by **compilation only** in CI. After UI changes, a manual Windows pass over theme
  switching, drag & drop, dialogs, and sync is expected.
