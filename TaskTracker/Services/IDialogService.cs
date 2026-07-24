namespace TaskTracker.Services
{
    /// <summary>
    /// The view models' only route to a modal message box. A seam rather than a
    /// convenience: with MessageBox.Show called inline, every error and
    /// confirmation path was unreachable from a test, because the call blocks on
    /// a real window.
    /// </summary>
    public interface IDialogService
    {
        /// <summary>Reports a failure the user needs to see.</summary>
        void Error(string message);

        /// <summary>Confirms something succeeded. Distinct from Error so the icon matches the news.</summary>
        void Info(string message);

        /// <summary>Asks a yes/no question. Returns true only on an explicit yes.</summary>
        bool Confirm(string message);
    }
}
