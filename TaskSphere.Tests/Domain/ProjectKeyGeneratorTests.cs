using TaskSphere.Domain.Common;

namespace TaskSphere.Tests.Domain;

public class ProjectKeyGeneratorTests
{
    private static HashSet<string> None() => new(StringComparer.Ordinal);

    [Theory]
    [InlineData("TaskSphere", "TS")]
    [InlineData("StayEase", "SE")]
    [InlineData("BaseClean", "BC")]
    [InlineData("My Great Project", "MGP")]
    public void Derive_UsesUppercaseInitials(string name, string expected)
    {
        Assert.Equal(expected, ProjectKeyGenerator.Derive(name, None()));
    }

    [Theory]
    [InlineData("backend", "BAC")]
    [InlineData("api", "API")]
    [InlineData("Website", "WEB")]
    public void Derive_FallsBackToFirstThreeLetters_WhenFewerThanTwoCapitals(string name, string expected)
    {
        Assert.Equal(expected, ProjectKeyGenerator.Derive(name, None()));
    }

    [Fact]
    public void Derive_SuffixesOnCollision()
    {
        var taken = new HashSet<string>(StringComparer.Ordinal) { "TS" };
        Assert.Equal("TS2", ProjectKeyGenerator.Derive("TaskSphere", taken));
    }

    [Fact]
    public void Derive_KeepsSuffixingUntilFree()
    {
        var taken = new HashSet<string>(StringComparer.Ordinal) { "TS", "TS2", "TS3" };
        Assert.Equal("TS4", ProjectKeyGenerator.Derive("TaskSphere", taken));
    }

    [Fact]
    public void Derive_PrefixesWhenNameStartsWithDigits()
    {
        Assert.Equal("P123", ProjectKeyGenerator.Derive("123", None()));
    }

    [Fact]
    public void Derive_FallsBackWhenNameHasNoAsciiCharacters()
    {
        Assert.Equal("PRJ", ProjectKeyGenerator.Derive("проект", None()));
    }

    [Fact]
    public void Derive_PadsSingleCharacterResults()
    {
        Assert.Equal("AX", ProjectKeyGenerator.Derive("a", None()));
    }

    [Fact]
    public void Derive_NeverExceedsTenCharacters()
    {
        var result = ProjectKeyGenerator.Derive("ABCDEFGHIJKLMNOP", None());
        Assert.True(result.Length <= 10, $"'{result}' is longer than 10 characters");
    }

    [Fact]
    public void Derive_AlwaysProducesAValidProjectKey()
    {
        string[] names = ["TaskSphere", "backend", "123", "проект", "a", "ABCDEFGHIJKLMNOP", "My Great Project"];

        foreach (var name in names)
            Assert.True(TaskKey.IsValidProjectKey(ProjectKeyGenerator.Derive(name, None())),
                $"'{name}' produced an invalid key");
    }
}
