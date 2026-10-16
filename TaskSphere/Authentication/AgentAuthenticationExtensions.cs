using Microsoft.AspNetCore.Authentication;

namespace TaskSphere.Authentication;

public static class AgentAuthenticationExtensions
{
    public const string PolicyName = "AgentOnly";

    /// <summary>
    /// Adds the AgentToken scheme NEXT TO JWT, never as the default and with no forwarding
    /// selector: a selector would make every [Authorize] controller accept agent tokens. Only
    /// endpoints that name this policy — /mcp — accept them.
    /// </summary>
    public static IServiceCollection AddAgentAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, AgentTokenAuthenticationHandler>(
                AgentTokenAuthenticationHandler.SchemeName, configureOptions: null);

        services.AddAuthorization(options =>
            options.AddPolicy(PolicyName, policy => policy
                .AddAuthenticationSchemes(AgentTokenAuthenticationHandler.SchemeName)
                .RequireAuthenticatedUser()));

        return services;
    }
}
