using TaskTracker.Core.Services;

namespace TaskTracker.Core.Tests
{
    public class StartupCommandTests
    {
        [Fact]
        public void Build_quotes_the_path()
        {
            Assert.Equal("\"C:\\Apps\\TaskTracker.exe\" --minimized",
                StartupCommand.Build(@"C:\Apps\TaskTracker.exe"));
        }

        [Fact]
        public void Build_survives_a_path_containing_spaces()
        {
            // The whole reason this is quoted: unquoted, Windows would run
            // "C:\Program" with "Files\..." as an argument, and the entry would
            // fail at every login with nothing to show for it.
            var command = StartupCommand.Build(@"C:\Program Files\TaskTracker\TaskTracker.exe");

            Assert.Equal("\"C:\\Program Files\\TaskTracker\\TaskTracker.exe\" --minimized", command);
            Assert.StartsWith("\"", command);
        }

        [Fact]
        public void Build_trims_surrounding_whitespace()
        {
            Assert.Equal("\"C:\\a.exe\" --minimized", StartupCommand.Build("  C:\\a.exe  "));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void Build_rejects_an_empty_path(string? path)
        {
            Assert.Throws<ArgumentException>(() => StartupCommand.Build(path!));
        }

        [Fact]
        public void StartsMinimized_detects_the_flag()
        {
            Assert.True(StartupCommand.StartsMinimized(new[] { "--minimized" }));
            Assert.True(StartupCommand.StartsMinimized(new[] { "--other", "--minimized" }));
        }

        [Fact]
        public void StartsMinimized_is_case_insensitive()
        {
            Assert.True(StartupCommand.StartsMinimized(new[] { "--MINIMIZED" }));
        }

        [Fact]
        public void StartsMinimized_is_false_without_the_flag()
        {
            Assert.False(StartupCommand.StartsMinimized(new[] { "--something" }));
            Assert.False(StartupCommand.StartsMinimized(Array.Empty<string>()));
            Assert.False(StartupCommand.StartsMinimized(null));
        }

        [Fact]
        public void Matches_accepts_the_value_Build_produced()
        {
            var path = @"C:\Apps\TaskTracker.exe";
            Assert.True(StartupCommand.Matches(StartupCommand.Build(path), path));
        }

        [Fact]
        public void Matches_ignores_case_and_surrounding_whitespace()
        {
            var path = @"C:\Apps\TaskTracker.exe";
            Assert.True(StartupCommand.Matches("  " + StartupCommand.Build(path).ToUpperInvariant() + " ", path));
        }

        [Fact]
        public void Matches_rejects_an_entry_pointing_somewhere_else()
        {
            // The case that makes Reconcile worth having: the app was republished to a
            // new folder and the old entry still points at a path that is now gone.
            var stale = StartupCommand.Build(@"C:\Old\TaskTracker.exe");

            Assert.False(StartupCommand.Matches(stale, @"C:\New\TaskTracker.exe"));
        }

        [Fact]
        public void Matches_rejects_a_value_missing_the_flag()
        {
            Assert.False(StartupCommand.Matches("\"C:\\Apps\\TaskTracker.exe\"", @"C:\Apps\TaskTracker.exe"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void Matches_rejects_a_missing_entry(string? existing)
        {
            Assert.False(StartupCommand.Matches(existing, @"C:\Apps\TaskTracker.exe"));
        }
    }
}
