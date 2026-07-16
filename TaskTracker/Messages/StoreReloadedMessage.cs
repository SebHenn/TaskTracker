namespace TaskTracker.Messages
{
    /// <summary>
    /// Broadcast after the project store was reloaded from disk because another
    /// process (e.g. the MCP server) changed it. View models holding references
    /// into the project list should re-resolve their state.
    /// </summary>
    public class StoreReloadedMessage
    {
    }
}
