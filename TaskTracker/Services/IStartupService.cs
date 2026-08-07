namespace TaskTracker.Services
{
    /// <summary>Registers the app to start when the user logs in.</summary>
    public interface IStartupService
    {
        /// <summary>True when a startup entry for this app currently exists.</summary>
        bool IsRegistered();

        /// <summary>
        /// Adds or removes the startup entry. Returns false when the OS refused — group
        /// policy can lock the key — so the caller can put the toggle back rather than
        /// showing a state that is not real.
        /// </summary>
        bool SetRegistered(bool enabled);

        /// <summary>
        /// Brings the OS entry in line with the saved setting. Repairs a stale path left
        /// behind when the app was moved or republished elsewhere.
        /// </summary>
        void Reconcile(bool shouldBeRegistered);
    }
}
