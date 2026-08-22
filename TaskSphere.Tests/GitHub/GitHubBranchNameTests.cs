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

    [Theory]
    [InlineData("TS-42/crud-for-product")]
    [InlineData("TS-42")]
    [InlineData("feature/TS-42-crud")]
    [InlineData("fix_TS-42")]
    public void LegalRefNames_AreAccepted(string name)
    {
        Assert.True(GitHubBranchNameValidator.IsValidRefName(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("TS-42/has..dots")]
    [InlineData("TS-42//double")]
    [InlineData("TS-42/at@{brace")]
    [InlineData("TS-42/trailing/")]
    [InlineData("TS-42/trailing.")]
    [InlineData("TS-42/thing.lock")]
    [InlineData("/TS-42/leading")]
    [InlineData("TS-42/.hidden")]
    [InlineData("TS-42/has space")]
    [InlineData("TS-42/tilde~1")]
    [InlineData("TS-42/caret^1")]
    [InlineData("TS-42/colon:1")]
    [InlineData("TS-42/question?")]
    [InlineData("TS-42/star*")]
    [InlineData("TS-42/bracket[1]")]
    [InlineData("TS-42/back\\slash")]
    public void IllegalRefNames_AreRejected(string? name)
    {
        Assert.False(GitHubBranchNameValidator.IsValidRefName(name));
    }

    [Fact]
    public void ANameTooLong_IsRejected()
    {
        Assert.False(GitHubBranchNameValidator.IsValidRefName("TS-42/" + new string('a', 200)));
    }

    [Theory]
    [InlineData("TS-42/crud", true)]
    [InlineData("TS-42", true)]
    [InlineData("hotfix/TS-42-now", true)]
    [InlineData("ts-42/crud", false)]
    [InlineData("XTS-42/crud", false)]
    [InlineData("TS-420/crud", false)]
    [InlineData("TS-7/see-also-TS-42", true)]
    public void NamesTask_RequiresThisTasksKey(string name, bool expected)
    {
        Assert.Equal(expected, GitHubBranchNameValidator.NamesTask(name, new TaskKey("TS", 42)));
    }

    [Fact]
    public void AnotherTasksKeyAlone_DoesNotName_ThisTask()
    {
        Assert.False(GitHubBranchNameValidator.NamesTask("TS-7/other-work", new TaskKey("TS", 42)));
    }
}
