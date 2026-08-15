using TaskTracker.Core.Services;

namespace TaskTracker.Core.Tests;

public class HotkeyBindingTests
{
    [Fact]
    public void ParsesTheDefault()
    {
        var binding = HotkeyBinding.Parse(HotkeyBinding.Default);

        Assert.NotNull(binding);
        Assert.Equal(HotkeyBinding.ModControl | HotkeyBinding.ModAlt, binding!.Modifiers);
        Assert.Equal(0x54u, binding.VirtualKey); // VK_T
        Assert.Equal("Ctrl+Alt+T", binding.Text);
    }

    [Theory]
    [InlineData("ctrl+alt+t")]
    [InlineData("CTRL + ALT + T")]
    [InlineData("Control+Alt+t")]
    public void IsCaseAndSpaceInsensitive(string text)
        => Assert.Equal("Ctrl+Alt+T", HotkeyBinding.Parse(text)!.Text);

    [Fact]
    public void SupportsEveryModifier()
    {
        var binding = HotkeyBinding.Parse("Ctrl+Alt+Shift+Win+Q");

        Assert.Equal(
            HotkeyBinding.ModControl | HotkeyBinding.ModAlt | HotkeyBinding.ModShift | HotkeyBinding.ModWin,
            binding!.Modifiers);
    }

    [Theory]
    [InlineData("Ctrl+F1", 0x70u)]
    [InlineData("Ctrl+F12", 0x7Bu)]
    [InlineData("Ctrl+F24", 0x87u)]
    [InlineData("Ctrl+5", (uint)'5')]
    public void SupportsFunctionKeysAndDigits(string text, uint expected)
        => Assert.Equal(expected, HotkeyBinding.Parse(text)!.VirtualKey);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("T")]              // no modifier — would swallow the key desktop-wide
    [InlineData("Ctrl")]           // no key
    [InlineData("Ctrl+Alt")]       // still no key
    [InlineData("Ctrl+Alt+T+Q")]   // two keys
    [InlineData("Ctrl+F25")]       // past VK_F24
    [InlineData("Ctrl+F0")]
    [InlineData("Ctrl+Enter")]     // not in the supported set
    [InlineData("Ctrl++")]
    public void RejectsWhatWouldNotRegister(string? text)
        => Assert.Null(HotkeyBinding.Parse(text));

    [Fact]
    public void NormalizesTheTextItRoundTrips()
    {
        // The settings box shows Text, so parsing its own output has to be stable.
        var once = HotkeyBinding.Parse("control+SHIFT+q")!;
        var twice = HotkeyBinding.Parse(once.Text)!;

        Assert.Equal(once.Text, twice.Text);
        Assert.Equal(once.Modifiers, twice.Modifiers);
        Assert.Equal(once.VirtualKey, twice.VirtualKey);
    }
}
