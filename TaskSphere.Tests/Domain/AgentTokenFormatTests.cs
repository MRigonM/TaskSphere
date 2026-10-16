using System.Text.RegularExpressions;
using TaskSphere.Domain.Common;

namespace TaskSphere.Tests.Domain;

public class AgentTokenFormatTests
{
    [Fact]
    public void A_new_token_is_tsk_plus_43_url_safe_characters()
    {
        var (token, _, _) = AgentTokenFormat.Create();

        // 32 random bytes in unpadded base64url is 43 characters.
        Assert.Matches(new Regex("^tsk_[A-Za-z0-9_-]{43}$"), token);
    }

    [Fact]
    public void Two_tokens_are_never_equal()
    {
        Assert.NotEqual(AgentTokenFormat.Create().Token, AgentTokenFormat.Create().Token);
    }

    [Fact]
    public void The_returned_hash_is_the_hash_of_the_returned_token()
    {
        var (token, hash, _) = AgentTokenFormat.Create();

        Assert.Equal(32, hash.Length);
        Assert.Equal(hash, AgentTokenFormat.Hash(token));
    }

    [Fact]
    public void The_display_prefix_is_the_first_twelve_characters()
    {
        var (token, _, prefix) = AgentTokenFormat.Create();

        Assert.Equal(token[..12], prefix);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("tsk_", false)]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.e30.x", false)]
    [InlineData("TSK_abc", false)]
    [InlineData("tsk_abc", true)]
    public void Only_a_tsk_prefixed_value_looks_like_an_agent_token(string? value, bool expected)
    {
        Assert.Equal(expected, AgentTokenFormat.LooksLikeAgentToken(value));
    }
}
