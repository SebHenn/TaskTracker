namespace TaskTracker.Core.Services
{
    /// <summary>
    /// The command line an OS "run at login" entry points at, and the flag that tells a
    /// launched process it started that way.
    ///
    /// Pure string work on purpose. Registering the entry is Windows-only and lives in the
    /// WPF head, but the quoting is the part that actually breaks: an install path
    /// containing a space silently splits into two arguments without it, and the entry
    /// then fails at every login with nothing to show for it.
    /// </summary>
    public static class StartupCommand
    {
        /// <summary>Passed to the launched process so it can come up minimised.</summary>
        public const string MinimizedFlag = "--minimized";

        /// <summary>Registry-ready command line: the quoted executable plus the flag.</summary>
        public static string Build(string executablePath)
        {
            if (string.IsNullOrWhiteSpace(executablePath))
                throw new ArgumentException("Executable path is required.", nameof(executablePath));
            return $"\"{executablePath.Trim()}\" {MinimizedFlag}";
        }

        /// <summary>True when the process was launched by the startup entry.</summary>
        public static bool StartsMinimized(IEnumerable<string>? args) =>
            args?.Any(a => string.Equals(a, MinimizedFlag, StringComparison.OrdinalIgnoreCase)) == true;

        /// <summary>
        /// True when an already-registered value still points at this executable.
        ///
        /// Worth checking on every start rather than only when the toggle is flipped: the
        /// app is published over itself and can be moved, and a stale entry would keep
        /// launching a path that no longer exists.
        /// </summary>
        public static bool Matches(string? registeredValue, string executablePath) =>
            !string.IsNullOrWhiteSpace(registeredValue) &&
            string.Equals(registeredValue.Trim(), Build(executablePath), StringComparison.OrdinalIgnoreCase);
    }
}
