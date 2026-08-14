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
