namespace TaskTracker.Core.Storage
{
    /// <summary>
    /// The cross-process store lock could not be taken within the retry window —
    /// the other process (desktop app or MCP server) is mid-write.
    ///
    /// A distinct type because callers want to say "busy, try again" rather than
    /// surface a bare IOException that reads like a corrupt or missing file. It still
    /// derives from IOException so existing catch blocks keep working.
    /// </summary>
    public class StoreLockedException : IOException
    {
        public StoreLockedException(string message, Exception? inner)
            : base(message, inner)
        {
        }
    }
}
