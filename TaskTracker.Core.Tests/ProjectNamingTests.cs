using TaskTracker.Core.Models;
using TaskTracker.Core.Services;

namespace TaskTracker.Core.Tests;

public class ProjectNamingTests
{
    private static List<ProjectModel> Projects(params string[] names) =>
        names.Select(n => new ProjectModel { Name = n }).ToList();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void ABlankNameIsNeverAvailable(string? name)
    {
        Assert.False(ProjectNaming.IsAvailable(Projects("Alpha"), name));
    }

    [Fact]
    public void AnUnusedNameIsAvailable()
    {
        Assert.True(ProjectNaming.IsAvailable(Projects("Alpha", "Beta"), "Gamma"));
    }

    [Fact]
    public void AnExactDuplicateIsNotAvailable()
    {
        Assert.False(ProjectNaming.IsAvailable(Projects("Alpha"), "Alpha"));
    }

    [Theory]
    [InlineData("alpha")]
    [InlineData("ALPHA")]
    [InlineData("AlPhA")]
    public void CaseIsNotEnoughToMakeANameDistinct(string name)
    {
        // Two sidebar rows reading "Work" and "work" are indistinguishable in use.
        Assert.False(ProjectNaming.IsAvailable(Projects("Alpha"), name));
    }

    [Theory]
    [InlineData("  Alpha")]
    [InlineData("Alpha  ")]
    [InlineData(" Alpha ")]
    public void SurroundingWhitespaceIsNotEnoughEither(string name)
    {
        Assert.False(ProjectNaming.IsAvailable(Projects("Alpha"), name));
    }

    [Fact]
    public void WhitespaceOnTheExistingNameIsAlsoIgnored()
    {
        Assert.False(ProjectNaming.IsAvailable(Projects(" Alpha "), "Alpha"));
    }

    [Fact]
    public void RenamingAProjectToItsOwnNameIsAllowed()
    {
        // Opening the edit dialog and pressing OK without touching the name must not
        // fail, which is what excluding the project itself is for.
        var projects = Projects("Alpha", "Beta");
        Assert.True(ProjectNaming.IsAvailable(projects, "Alpha", excluding: projects[0]));
    }

    [Fact]
    public void RenamingAProjectOntoAnotherOnesNameIsRejected()
    {
        var projects = Projects("Alpha", "Beta");
        Assert.False(ProjectNaming.IsAvailable(projects, "Beta", excluding: projects[0]));
    }

    [Fact]
    public void RenamingChangesOnlyCaseOfItsOwnName_IsAllowed()
    {
        var projects = Projects("alpha");
        Assert.True(ProjectNaming.IsAvailable(projects, "Alpha", excluding: projects[0]));
    }

    [Fact]
    public void AnyNameIsAvailableWhenThereAreNoProjects()
    {
        Assert.True(ProjectNaming.IsAvailable([], "Alpha"));
    }
}
