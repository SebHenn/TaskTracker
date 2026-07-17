using System;
using System.Drawing;
using System.Windows;
using TaskTracker.Core.Services;

namespace TaskTracker.Services
{
    /// <summary>
    /// System tray icon (WinForms NotifyIcon): double-click restores the main
    /// window, and a balloon tip on startup summarizes overdue/due-today tasks.
    /// </summary>
    public class TrayService : IDisposable
    {
        private readonly IProjectsService _projectsService;
        private readonly ILanguageService _languageService;
        private System.Windows.Forms.NotifyIcon? _icon;

        public TrayService(IProjectsService projectsService, ILanguageService languageService)
        {
            _projectsService = projectsService;
            _languageService = languageService;
        }

        public void Initialize()
        {
            _icon = new System.Windows.Forms.NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "TaskTracker",
                Visible = true,
            };
            _icon.DoubleClick += (_, _) => RestoreMainWindow();

            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add(_languageService.GetString("TrayOpen"), null, (_, _) => RestoreMainWindow());
            menu.Items.Add(_languageService.GetString("TrayExit"), null, (_, _) => System.Windows.Application.Current.Shutdown());
            _icon.ContextMenuStrip = menu;

            ShowDueSummaryIfAny();
        }

        private void ShowDueSummaryIfAny()
        {
            var overview = DueTasks.Collect(_projectsService.projectModels);
            if (overview.Overdue.Count == 0 && overview.DueToday.Count == 0)
                return;

            var text = $"{overview.Overdue.Count} {_languageService.GetString("OverdueSection")}, " +
                       $"{overview.DueToday.Count} {_languageService.GetString("DueTodaySection")}";
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
            if (_icon != null)
            {
                _icon.Visible = false;
                _icon.Dispose();
                _icon = null;
            }
        }
    }
}
