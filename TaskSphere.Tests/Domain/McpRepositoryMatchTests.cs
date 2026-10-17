using TaskSphere.Domain.DataTransferObjects.GitHub;
using TaskSphere.Mcp;

namespace TaskSphere.Tests.Domain;

public class McpRepositoryMatchTests
{
    private static readonly BranchRepositoryOptionDto Api = new(11, "rigon-org/api", "main");
    private static readonly BranchRepositoryOptionDto Web = new(12, "rigon-org/web", "develop");
    private static readonly BranchRepositoryOptionDto ForkedApi = new(13, "someone/api", "main");

    [Fact]
    public void No_name_and_one_linked_repository_picks_it()
    {
        var result = McpRepositoryMatch.Resolve(null, [Api], "TS-1");

        Assert.True(result.IsSuccess);
        Assert.Equal(11, result.Value!.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void No_name_and_two_linked_repositories_asks_for_one_and_lists_them(string? name)
    {
        var result = McpRepositoryMatch.Resolve(name, [Api, Web], "TS-1");

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "TS-1's project links more than one repository; pass repository. Linked repositories: rigon-org/api, rigon-org/web.",
            result.Errors[0].Description);
    }

    [Theory]
    [InlineData("web")]
    [InlineData("WEB")]
    [InlineData(" web ")]
    [InlineData("rigon-org/web")]
    [InlineData("Rigon-Org/Web")]
    public void Matches_by_short_or_full_name_ignoring_case(string name)
    {
        var result = McpRepositoryMatch.Resolve(name, [Api, Web], "TS-1");

        Assert.True(result.IsSuccess);
        Assert.Equal(12, result.Value!.Id);
    }

    [Fact]
    public void A_full_name_does_not_match_on_its_short_part_alone()
    {
        // "other-org/web" must not resolve to rigon-org/web just because both end in /web.
        var result = McpRepositoryMatch.Resolve("other-org/web", [Api, Web], "TS-1");

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public void An_unknown_name_is_refused_with_the_linked_list()
    {
        var result = McpRepositoryMatch.Resolve("mobile", [Api, Web], "TS-1");

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "No repository named mobile is linked to TS-1's project. Linked repositories: rigon-org/api, rigon-org/web.",
            result.Errors[0].Description);
    }

    [Fact]
    public void A_short_name_under_two_owners_is_ambiguous()
    {
        var result = McpRepositoryMatch.Resolve("api", [Api, ForkedApi], "TS-1");

        Assert.False(result.IsSuccess);
        Assert.Equal(
            "api matches more than one linked repository; pass owner/name. Linked repositories: rigon-org/api, someone/api.",
            result.Errors[0].Description);
    }

    [Fact]
    public void A_given_name_is_still_checked_when_only_one_repository_is_linked()
    {
        // With one link the name is optional, but a wrong one is a mistake worth reporting.
        var result = McpRepositoryMatch.Resolve("web", [Api], "TS-1");

        Assert.False(result.IsSuccess);
    }
}
