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

    // The tests below close gaps found by an independent mutation sweep on 2026-08-22. Each one
    // exists because a mutant survived everything above it.

    /// <summary>
    /// The slug loop's character ranges were unpinned at their edges: ranges excluding '0' or
    /// 'Z' passed every other test, because no title above contains either.
    /// </summary>
    [Fact]
    public void Slug_KeepsTheEdgesOfBothCharacterRanges()
    {
        Assert.Equal("TS-42/zebra0-z0", GitHubBranchNameBuilder.Build(new TaskKey("TS", 42), "ZEBRA0 z0"));
    }

    /// <summary>
    /// The cap was pinned only by an upper bound, so both the cap value and the truncation
    /// index could move without failing anything. Three 20-character words put the cut inside
    /// the third word, where no separator is near enough to trigger the word-boundary cut-back
    /// — so the assertion reads the exact truncation point.
    /// </summary>
    [Fact]
    public void Slug_TruncatesAtExactlyTheCap_WhenNoSeparatorIsNear()
    {
        var title = $"{new string('a', 20)} {new string('b', 20)} {new string('c', 20)}";

        var expected = $"TS-42/{new string('a', 20)}-{new string('b', 20)}-{new string('c', 18)}";

        Assert.Equal(expected, GitHubBranchNameBuilder.Build(new TaskKey("TS", 42), title));
    }

    /// <summary>
    /// A slug of exactly the cap length must be returned untouched. Constructed so the last
    /// separator sits at index 50: that is inside the cut-back window, so an off-by-one on the
    /// cap comparison would shorten this name rather than leave it alone.
    /// </summary>
    [Fact]
    public void ASlugExactlyAtTheCap_IsNotTruncated()
    {
        var title = $"{new string('a', 10)} {new string('b', 9)} {new string('c', 9)} "
                    + $"{new string('d', 9)} {new string('e', 9)} {new string('f', 9)}";

        var name = GitHubBranchNameBuilder.Build(new TaskKey("TS", 42), title);

        Assert.Equal(60, name.Length - "TS-42/".Length);
        Assert.EndsWith(new string('f', 9), name);
    }

    /// <summary>
    /// The right single quotation mark is dropped like the ASCII apostrophe, so a title pasted
    /// from a word processor slugs the same way one typed in an editor does. The literal below
    /// is U+2019 and this file is UTF-8; the builder matches it by code point rather than by a
    /// source literal, so the two cannot disagree if an editor rewrites the encoding.
    /// </summary>
    [Fact]
    public void Slug_DropsTheSmartApostrophe_TheSameWayItDropsTheAsciiOne()
    {
        Assert.Equal(
            "TS-42/users-mentions",
            GitHubBranchNameBuilder.Build(new TaskKey("TS", 42), "user’s mentions"));
    }

    /// <summary>
    /// The length limit was pinned only well past the boundary, so the comparison and the
    /// constant were both free to move. 200 is the longest accepted name.
    /// </summary>
    [Theory]
    [InlineData(194, true)]
    [InlineData(195, false)]
    public void TheLengthLimit_IsPinnedAtItsBoundary(int fillLength, bool expected)
    {
        var name = "TS-42/" + new string('a', fillLength);

        Assert.Equal(expected, GitHubBranchNameValidator.IsValidRefName(name));
    }

    /// <summary>
    /// A ref may begin with a digit — nothing above tested one, so the regex's first-character
    /// class could drop digits unnoticed and reject a legal name.
    /// </summary>
    [Fact]
    public void ARefNameStartingWithADigit_IsLegal()
    {
        Assert.True(GitHubBranchNameValidator.IsValidRefName("2fix/TS-42"));
    }

    /// <summary>
    /// The <c>.lock</c> suffix is refused exactly as git spells it, case-sensitively. This pins
    /// our own rule rather than a claim about GitHub: a differently-cased suffix is not ours to
    /// reject, and if GitHub disagrees it answers with a 422 carrying its own message.
    /// </summary>
    [Fact]
    public void TheLockSuffixIsRefusedCaseSensitively_NotBroadly()
    {
        Assert.False(GitHubBranchNameValidator.IsValidRefName("TS-42/thing.lock"));
        Assert.True(GitHubBranchNameValidator.IsValidRefName("TS-42/thing.LOCK"));
    }
}
