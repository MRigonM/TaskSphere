using TaskSphere.Domain.Common;

namespace TaskSphere.Tests.Domain;

public class TaskKeyTests
{
    [Theory]
    [InlineData("TS-1", "TS", 1)]
    [InlineData("TS-42", "TS", 42)]
    [InlineData("PROJ2-999", "PROJ2", 999)]
    [InlineData("ABCDEFGHIJ-7", "ABCDEFGHIJ", 7)]
    [InlineData("TS-999999999", "TS", 999999999)]
    public void TryParse_AcceptsValidKeys(string input, string expectedProjectKey, int expectedNumber)
    {
        Assert.True(TaskKey.TryParse(input, out var key));
        Assert.Equal(expectedProjectKey, key.ProjectKey);
        Assert.Equal(expectedNumber, key.Number);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("TS42")]           // no separator
    [InlineData("ts-42")]          // lowercase
    [InlineData("T-42")]           // prefix too short
    [InlineData("ABCDEFGHIJK-1")]  // prefix too long (11)
    [InlineData("2TS-42")]         // must start with a letter
    [InlineData("TS-0")]           // zero not allowed
    [InlineData("TS-007")]         // leading zeros not allowed
    [InlineData("TS-")]
    [InlineData("-42")]
    [InlineData("TS-42-1")]
    [InlineData("TS-42\n")]        // trailing newline must not slip past the anchor
    [InlineData("TS-42 ")]         // trailing space
    public void TryParse_RejectsInvalidKeys(string? input)
    {
        Assert.False(TaskKey.TryParse(input, out _));
    }

    [Fact]
    public void TryParse_AcceptsShapeOnly_TheDatabaseIsTheRealFilter()
    {
        // "UTF-8" is shaped exactly like a task key and the parser cannot know otherwise.
        // Resolution against the projects table is what rejects it — see
        // TaskService.GetByKeyAsync. This is why a regex alone is never sufficient.
        Assert.True(TaskKey.TryParse("UTF-8", out var key));
        Assert.Equal("UTF", key.ProjectKey);
        Assert.Equal(8, key.Number);
    }

    [Fact]
    public void ToString_RendersCanonicalForm()
    {
        Assert.Equal("TS-42", new TaskKey("TS", 42).ToString());
    }

    [Theory]
    [InlineData("TS", true)]
    [InlineData("PROJ2", true)]
    [InlineData("ABCDEFGHIJ", true)]
    [InlineData("T", false)]
    [InlineData("ts", false)]
    [InlineData("2TS", false)]
    [InlineData("ABCDEFGHIJK", false)]
    [InlineData("TS-", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("TS\n", false)]    // trailing newline must not slip past the anchor
    public void IsValidProjectKey_EnforcesFormat(string? input, bool expected)
    {
        Assert.Equal(expected, TaskKey.IsValidProjectKey(input));
    }
}
