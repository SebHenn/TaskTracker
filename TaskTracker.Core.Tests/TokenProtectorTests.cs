using System.Runtime.InteropServices;
using TaskTracker.Core.GitHub;

namespace TaskTracker.Core.Tests;

/// <summary>
/// The GitHub token's only protection. Untested until now, which is a poor place for
/// that to be true — a silent failure here is either a token the app cannot read back
/// or one written to disk less protected than the caller believes.
/// </summary>
public class TokenProtectorTests
{
    [Fact]
    public void RoundTripsAToken()
    {
        var (value, isPlaintext) = TokenProtector.Protect("ghp_example_token_value");

        Assert.NotNull(value);
        Assert.NotEqual("ghp_example_token_value", value); // never stored verbatim
        Assert.Equal("ghp_example_token_value", TokenProtector.Unprotect(value, isPlaintext));
    }

    [Fact]
    public void ReportsWhetherTheOsActuallyEncryptedIt()
    {
        // The flag is the contract: it tells the settings page whether to warn that the
        // token is only base64 on this platform. Getting it backwards would either hide a
        // real warning or cry wolf on Windows.
        var (_, isPlaintext) = TokenProtector.Protect("ghp_example");

        Assert.Equal(!RuntimeInformation.IsOSPlatform(OSPlatform.Windows), isPlaintext);
    }

    [Fact]
    public void RoundTripsThroughThePlaintextPathExplicitly()
    {
        // Exercises the non-Windows branch on every platform, so CI on ubuntu and a dev
        // machine on Windows both cover it.
        var encoded = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("ghp_plain"));

        Assert.Equal("ghp_plain", TokenProtector.Unprotect(encoded, isPlaintext: true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void UnprotectsNothingToNull(string? stored)
    {
        Assert.Null(TokenProtector.Unprotect(stored, isPlaintext: false));
        Assert.Null(TokenProtector.Unprotect(stored, isPlaintext: true));
    }

    [Fact]
    public void UnprotectReturnsNullRatherThanThrowingOnGarbage()
    {
        // A settings file copied from another machine has a token this machine's DPAPI
        // key cannot open. That has to read as "no token", not crash the app on startup.
        Assert.Null(TokenProtector.Unprotect("not-base64-at-all!!", isPlaintext: true));
        Assert.Null(TokenProtector.Unprotect("bm90LWRwYXBpLWRhdGE=", isPlaintext: false));
    }

    [Fact]
    public void EmptyInputStaysEmptyThroughTheRoundTrip()
    {
        // It does not come back null — DPAPI happily encrypts zero bytes — but it does
        // come back empty, which is what every caller actually tests for before deciding
        // there is no token configured.
        var (value, isPlaintext) = TokenProtector.Protect("");

        Assert.True(string.IsNullOrEmpty(TokenProtector.Unprotect(value, isPlaintext)));
    }
}
