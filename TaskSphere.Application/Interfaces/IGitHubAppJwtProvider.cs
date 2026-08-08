namespace TaskSphere.Application.Interfaces;

public interface IGitHubAppJwtProvider
{
    /// <summary>
    /// Signs a short-lived RS256 JWT identifying the GitHub App itself. Used only to exchange
    /// for an installation access token — never sent to the REST API as an installation credential.
    /// </summary>
    string CreateJwt();
}
