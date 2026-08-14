namespace TaskTracker.Core.Tests;

/// <summary>
/// Serializes every test class that drives the MCP tools.
///
/// The tools are static, and the seam that points them at a temp directory
/// (<c>TaskTrackerTools.CreateStore</c>) is a static property. xunit runs test classes in
/// parallel by default, so two MCP classes would overwrite each other's store factory and
/// fail in ways that vanish when either is run alone. Every class touching that seam must
/// join this collection.
/// </summary>
[CollectionDefinition(Name)]
public class McpCollection
{
    public const string Name = "mcp-tools";
}

/// <summary>
/// Serializes every test class that redirects <see cref="TaskTracker.Core.Storage.AppLog.LogFilePath"/>.
///
/// Same hazard as above, different static: two classes pointing the log somewhere else at
/// once makes one of them read the other's file, which surfaces as a rare, timing-
/// dependent failure in whichever class happened to lose.
/// </summary>
[CollectionDefinition(Name)]
public class AppLogCollection
{
    public const string Name = "app-log";
}
