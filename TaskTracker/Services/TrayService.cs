using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows;
using TaskTracker.Core.Services;
using TaskTracker.Messages;

namespace TaskTracker.Services
{
    /// <summary>
    /// System tray icon (WinForms NotifyIcon): double-click restores the main
    /// window, and a balloon tip on startup summarizes overdue/due-today tasks.
    /// Also surfaces issues that a background sync imported.
    /// </summary>
    public class TrayService : IDisposable
    {
        private readonly IProjectsService _projectsService;
        private readonly ILanguageService _languageService;
        private readonly HashSet<Guid> _notifiedTaskIds = new();
        private System.Windows.Forms.NotifyIcon? _icon;
        private System.Windows.Threading.DispatcherTimer? _dueCheckTimer;

        /// <summary>
        /// Project to open when the current balloon is clicked. Null for the due-task
        /// balloon, which spans projects and has nowhere specific to go.
        /// </summary>
        private Guid? _balloonProjectId;

        public TrayService(IProjectsService projectsService, ILanguageService languageService)
        {
            _projectsService = projectsService;
            _languageService = languageService;
        }

        public void Initialize()
        {
            Icon trayIcon;
            try
            {
                // The exe carries the app icon; fall back to the generic one.
                trayIcon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application;
            }
            catch (Exception)
            {
                trayIcon = SystemIcons.Application;
            }

            _icon = new System.Windows.Forms.NotifyIcon
            {
                Icon = trayIcon,
                Text = "TaskTracker",
                Visible = true,
            };
            _icon.DoubleClick += (_, _) => RestoreMainWindow();
            _icon.BalloonTipClicked += (_, _) => OnBalloonClicked();

            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add(_languageService.GetString("TrayOpen"), null, (_, _) => RestoreMainWindow());
            menu.Items.Add(_languageService.GetString("TrayExit"), null, (_, _) => System.Windows.Application.Current.Shutdown());
            _icon.ContextMenuStrip = menu;

            NotifyNewlyDueTasks();

            // Re-check while the app runs so tasks that *become* due get a balloon too.
            _dueCheckTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
            _dueCheckTimer.Tick += (_, _) => NotifyNewlyDueTasks();
            _dueCheckTimer.Start();

            WeakReferenceMessenger.Default.Register<NewIssuesImportedMessage>(this, (_, message) => NotifyNewIssues(message));
        }

        private void NotifyNewIssues(NewIssuesImportedMessage message)
        {
            if (_icon == null || message.Issues.Count == 0)
                return;

            var text = message.Issues.Count == 1
                ? $"#{message.Issues[0].Number} {message.Issues[0].Title}"
                : string.Format(_languageService.GetString("NewIssuesCount"), message.Issues.Count);

            _balloonProjectId = message.ProjectId;
            _icon.ShowBalloonTip(7000, $"{message.ProjectName} — {_languageService.GetString("NewIssuesTitle")}",
                text, System.Windows.Forms.ToolTipIcon.Info);
        }

        private void OnBalloonClicked()
        {
            RestoreMainWindow();
            if (_balloonProjectId is not { } id)
                return;

            // Resolved at click time, not when the balloon was raised: a live reload can
            // replace the model in between, and navigating to a discarded instance would
            // open a board that no longer tracks the store.
            var project = _projectsService.projectModels.FirstOrDefault(p => p.Id == id);
            if (project != null)
                WeakReferenceMessenger.Default.Send(new ProjectSelectClickMessage(project));
        }

        private void NotifyNewlyDueTasks()
        {
            var overview = DueTasks.Collect(_projectsService.projectModels);
            var fresh = overview.Overdue.Concat(overview.DueToday)
                .Where(i => _notifiedTaskIds.Add(i.Task.Id))
                .ToList();
            if (fresh.Count == 0)
                return;

            var text = fresh.Count == 1
                ? $"{fresh[0].Project.Name} ▸ {fresh[0].Task.Title}"
                : $"{overview.Overdue.Count} {_languageService.GetString("OverdueSection")}, " +
                  $"{overview.DueToday.Count} {_languageService.GetString("DueTodaySection")}";
            // Cleared, not left over: without this a click on the due-task balloon would
            // still navigate to whichever project the previous new-issue balloon named.
            _balloonProjectId = null;
            _icon?.ShowBalloonTip(5000, "TaskTracker", text, System.Windows.Forms.ToolTipIcon.Info);
        }

        private static void RestoreMainWindow()
        {
            var window = System.Windows.Application.Current.MainWindow;
            if (window == null)
                return;
            window.Show();
            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;
            window.Activate();
        }

        public void Dispose()
        {
            WeakReferenceMessenger.Default.Unregister<NewIssuesImportedMessage>(this);
            _dueCheckTimer?.Stop();
            if (_icon != null)
            {
                _icon.Visible = false;
                _icon.Dispose();
                _icon = null;
            }
        }
    }
}
