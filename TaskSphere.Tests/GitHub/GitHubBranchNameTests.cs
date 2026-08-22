using TaskSphere.Domain.Common;

namespace TaskSphere.Tests.GitHub;

public class GitHubBranchNameTests
{
    [Theory]
    [InlineData("CRUD for Product")]
    [InlineData("Fix: user's @mentions // v2")]
    [InlineData("!!!")]
    [InlineData("2 factor auth")]
    [InlineData("Ship it 🚀 today")]
    [InlineData("   leading and trailing   ")]
    [InlineData("--- dashes --- everywhere ---")]
    [InlineData("Already mentions TS-42 inline")]
    public void EveryGeneratedName_ScansBackToItsOwnKey(string title)
    {
        var key = new TaskKey("TS", 42);

        var name = GitHubBranchNameBuilder.Build(key, title);

        Assert.Contains(key, TaskKeyScanner.Scan(name));
    }

    [Theory]
    [InlineData("CRUD for Product", "TS-42/crud-for-product")]
    [InlineData("Fix: user's @mentions // v2", "TS-42/fix-users-mentions-v2")]
    [InlineData("2 factor auth", "TS-42/2-factor-auth")]
    [InlineData("   leading and trailing   ", "TS-42/leading-and-trailing")]
    [InlineData("--- dashes --- everywhere ---", "TS-42/dashes-everywhere")]
    public void Slugs_TheTitleOnly_LeavingTheKeyUppercase(string title, string expected)
    {
        Assert.Equal(expected, GitHubBranchNameBuilder.Build(new TaskKey("TS", 42), title));
    }

    [Theory]
    [InlineData("!!!")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ATitleThatSlugsToNothing_FallsBackToTask(string? title)
    {
        Assert.Equal("TS-42/task", GitHubBranchNameBuilder.Build(new TaskKey("TS", 42), title));
    }

    [Fact]
    public void ALongTitle_IsCappedOnASeparatorAndStillScans()
    {
        var key = new TaskKey("TS", 42);
        var title = string.Join(' ', Enumerable.Repeat("verylongword", 20));

        var name = GitHubBranchNameBuilder.Build(key, title);

        Assert.True(name.Length <= 3 + 1 + 60, $"'{name}' is longer than key + '/' + 60.");
        Assert.False(name.EndsWith('-'), "A capped slug must not end on a separator.");
        Assert.Contains(key, TaskKeyScanner.Scan(name));
    }
}
