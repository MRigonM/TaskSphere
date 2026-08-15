using TaskSphere.Domain.Common;

namespace TaskSphere.Tests.Domain;

public class TaskKeyScannerTests
{
    private static string[] Keys(string? text) =>
        TaskKeyScanner.Scan(text).Select(k => k.ToString()).ToArray();

    [Fact]
    public void Scan_FindsAKeyInsideACommitMessage()
    {
        Assert.Equal(["TS-42"], Keys("TS-42 fix the panel"));
    }

    [Fact]
    public void Scan_FindsAKeyThatIsNotAtTheStart()
    {
        Assert.Equal(["TS-42"], Keys("fixes TS-42, finally"));
    }

    [Fact]
    public void Scan_FindsAKeyInsideABranchName()
    {
        // The case TaskKey.TryParse alone cannot serve: the whole string is not a key.
        Assert.Equal(["TS-42"], Keys("feature/TS-42-activity-panel"));
    }

    [Fact]
    public void Scan_ReadsBeyondTheSubjectLine()
    {
        Assert.Equal(["TS-42", "BS-7"], Keys("TS-42 wire it up\n\nAlso closes BS-7."));
    }

    [Fact]
    public void Scan_ReturnsEveryDistinctKey_Once()
    {
        Assert.Equal(["TS-42", "TS-51"], Keys("TS-42 TS-51 fix both, see TS-42 again"));
    }

    [Fact]
    public void Scan_DoesNotMatchAKeyGluedToSurroundingWordCharacters()
    {
        // The lookbehind's job. Note it cannot be shown with an uppercase prefix: "XTS-42"
        // is a whole valid key for project XTS, not a glued TS-42.
        Assert.Empty(Keys("fixTS-42"));
        Assert.Empty(Keys("abc123TS-42"));

        // The lookahead's job. A task number is at most nine digits, so a ten-digit run is
        // not a key at all rather than a key with its tail chopped off. "TS-421" cannot
        // show this — the number match is greedy and takes 421 with or without the lookahead.
        Assert.Empty(Keys("TS-1234567890"));
        Assert.Equal(["TS-421"], Keys("TS-421"));
    }

    [Fact]
    public void Scan_ReadsAnUppercasePrefixAsPartOfTheProjectKey()
    {
        // XTS satisfies TaskKey's own project-key grammar, so this is one key for project
        // XTS. Whether that project exists is the resolver's question, not the scanner's —
        // an unresolvable key is normal traffic.
        Assert.Equal(["XTS-42"], Keys("XTS-42"));
    }

    [Fact]
    public void Scan_RejectsTheThingsTaskKeyItselfRejects()
    {
        Assert.Empty(Keys("ts-42"));          // lowercase project keys do not exist
        Assert.Empty(Keys("TS-0"));           // task numbers start at 1
        Assert.Empty(Keys("TS-042"));         // no leading zeros
        Assert.Empty(Keys("T-42"));           // project keys are at least two characters
        Assert.Empty(Keys("TOOLONGKEYS-42")); // project keys are at most ten
    }

    [Fact]
    public void Scan_HandlesNullAndEmptyText()
    {
        Assert.Empty(Keys(null));
        Assert.Empty(Keys(""));
        Assert.Empty(Keys("   "));
    }

    [Fact]
    public void Scan_ParsesThroughTaskKey_SoTheKeyCarriesItsParts()
    {
        var key = Assert.Single(TaskKeyScanner.Scan("closes TS-42"));

        Assert.Equal("TS", key.ProjectKey);
        Assert.Equal(42, key.Number);
    }
}
