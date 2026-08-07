using TaskTracker.Core.GitHub;

namespace TaskTracker.Core.Tests;

public class IssueBodyMarkerTests
{
    private const string Marker = "<!-- cifail:9f2a1c4e -->";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\n  ")]
    [InlineData("Build failed on main.")]
    public void ABodyWithoutATrailingCommentHasNoMarkers(string? body)
    {
        Assert.Empty(IssueBodyMarkers.Trailing(body));
    }

    [Fact]
    public void ATrailingCommentIsAMarker()
    {
        Assert.Equal(new[] { Marker }, IssueBodyMarkers.Trailing($"Build failed on main.\n\n{Marker}"));
    }

    [Fact]
    public void WhitespaceAfterTheMarkerDoesNotHideIt()
    {
        Assert.Equal(new[] { Marker }, IssueBodyMarkers.Trailing($"Build failed.\n\n{Marker}\n\n  "));
    }

    [Fact]
    public void SeveralTrailingMarkersAreAllKept()
    {
        // More than one bot can stamp the same issue; order is the body's order.
        var body = $"Build failed.\n\n{Marker}\n<!-- triage:auto -->";
        Assert.Equal(new[] { Marker, "<!-- triage:auto -->" }, IssueBodyMarkers.Trailing(body));
    }

    [Fact]
    public void AMultiLineCommentIsOneMarker()
    {
        var marker = "<!--\ncifail:9f2a1c4e\nrun:1421\n-->";
        Assert.Equal(new[] { marker }, IssueBodyMarkers.Trailing($"Build failed.\n\n{marker}"));
    }

    [Fact]
    public void ACommentWithProseAfterItIsNotAMarker()
    {
        // Only what sits at the very end is bot bookkeeping — the rest is content the
        // rewrite is entitled to replace.
        Assert.Empty(IssueBodyMarkers.Trailing($"{Marker}\n\nBuild failed on main."));
    }

    [Fact]
    public void OnlyTheTrailingRunOfCommentsIsTaken()
    {
        var body = $"<!-- note -->\n\nBuild failed on main.\n\n{Marker}";
        Assert.Equal(new[] { Marker }, IssueBodyMarkers.Trailing(body));
    }

    [Fact]
    public void AStrayCloserIsNotMistakenForAMarker()
    {
        Assert.Empty(IssueBodyMarkers.Trailing("Build failed: expected `x --> y`"));
    }

    [Fact]
    public void ARewrittenBodyKeepsTheMarker()
    {
        // The whole point: cifail finds its issue by scanning bodies for this string, so
        // a description sync that dropped it would make it file a duplicate instead.
        var rewritten = IssueBodyMarkers.Preserve($"Build failed on main.\n\n{Marker}", "Flaky integration test, being looked at.");
        Assert.Equal($"Flaky integration test, being looked at.\n\n{Marker}", rewritten);
    }

    [Fact]
    public void ATruncatedDescriptionStillGetsTheMarkerBack()
    {
        // Imported descriptions are cut at 500 characters, so a long issue's marker is
        // already gone from the local copy — it has to come off the fetched body.
        var existing = new string('x', 600) + "\n\n" + Marker;
        Assert.EndsWith($"\n\n{Marker}", IssueBodyMarkers.Preserve(existing, new string('x', 500) + "…"));
    }

    [Fact]
    public void AMarkerTheNewBodyAlreadyCarriesIsNotDuplicated()
    {
        var body = $"Short issue body.\n\n{Marker}";
        Assert.Equal(body, IssueBodyMarkers.Preserve(body, body));
    }

    [Fact]
    public void OnlyTheMissingMarkersAreAppended()
    {
        var existing = $"Build failed.\n\n{Marker}\n<!-- triage:auto -->";
        var rewritten = IssueBodyMarkers.Preserve(existing, $"Rewritten.\n\n{Marker}");
        Assert.Equal($"Rewritten.\n\n{Marker}\n\n<!-- triage:auto -->", rewritten);
    }

    [Fact]
    public void ABodyWithNothingToPreserveIsPassedThroughUntouched()
    {
        Assert.Equal("Rewritten.\n\n", IssueBodyMarkers.Preserve("Hand-written issue.", "Rewritten.\n\n"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AnEmptyRewriteStillCarriesTheMarker(string? newBody)
    {
        Assert.Equal(Marker, IssueBodyMarkers.Preserve($"Build failed.\n\n{Marker}", newBody));
    }
}
