using Microsoft.Win32;
using System;
using TaskTracker.Core.Services;
using TaskTracker.Core.Storage;

namespace TaskTracker.Services
{
    /// <summary>
    /// Run-at-login through the per-user Run key. HKCU rather than HKLM on purpose: it
    /// needs no elevation, and the setting belongs to this user rather than the machine.
    /// </summary>
    public class StartupService : IStartupService
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "TaskTracker";

        /// <summary>
        /// The single-file host reports the real executable here. AppContext.BaseDirectory
        /// would point at the extraction folder for a single-file build, which is a
        /// temporary path and useless in a startup entry.
        /// </summary>
        private static string? ExecutablePath => Environment.ProcessPath;

        public bool IsRegistered()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
                return key?.GetValue(ValueName) != null;
            }
            catch (Exception ex)
            {
                AppLog.Write("startup", $"read failed: {ex.Message}");
                return false;
            }
        }

        public bool SetRegistered(bool enabled)
        {
            var path = ExecutablePath;
            if (enabled && string.IsNullOrEmpty(path))
            {
                AppLog.Write("startup", "cannot register: executable path is unknown");
                return false;
            }

            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
                if (key == null)
                {
                    AppLog.Write("startup", "cannot open Run key for writing");
                    return false;
                }

                if (enabled)
                    key.SetValue(ValueName, StartupCommand.Build(path!), RegistryValueKind.String);
                else
                    key.DeleteValue(ValueName, throwOnMissingValue: false);
                return true;
            }
            catch (Exception ex)
            {
                // Group policy can lock this key. Report it rather than leaving the
                // checkbox claiming something the OS never accepted.
                AppLog.Write("startup", $"write failed: {ex.Message}");
                return false;
            }
        }

        public void Reconcile(bool shouldBeRegistered)
        {
            var path = ExecutablePath;
            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                var existing = key?.GetValue(ValueName) as string;

                if (!shouldBeRegistered)
                {
                    if (existing != null)
                        key?.DeleteValue(ValueName, throwOnMissingValue: false);
                    return;
                }

                // Rewrite whenever it drifted: this app is published over itself and can
                // be moved, and an entry pointing at a path that no longer exists fails
                // silently at every login.
                if (!StartupCommand.Matches(existing, path!))
                    SetRegistered(true);
            }
            catch (Exception ex)
            {
                AppLog.Write("startup", $"reconcile failed: {ex.Message}");
            }
        }
    }
}
