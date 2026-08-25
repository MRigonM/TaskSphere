using System.Text.Json;
using System.Text.Json.Serialization;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Enums;
using TaskSphere.Domain.Interfaces;
using TaskSphere.Application.Interfaces;

namespace TaskSphere.Infrastructure.Services;

/// <summary>
/// One GitHub call per repository, upserted into the mirror. Extracted from
/// <see cref="GitHubActivitySyncService"/> because two callers need it: the full company sync,
/// and the project-scoped refresh a board triggers.
/// <para>
/// This is the cheap half of a sync. The commits pass costs 1 + B calls for B branches; this
/// costs one, and it is the only input the merge → Done transition reads.
/// </para>
/// </summary>
public class GitHubPullRequestMirror
{
    private readonly IGitHubApiClient _apiClient;
    private readonly IUnitOfWork _unitOfWork;

    public GitHubPullRequestMirror(IGitHubApiClient apiClient, IUnitOfWork unitOfWork)
    {
        _apiClient = apiClient;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<int>> RefreshAsync(
        GitHubInstallation installation,
        int repositoryRowId,
        string fullName,
        DateTime since,
        CancellationToken cancellationToken)
    {
        var url = $"https://api.github.com/repos/{fullName}/pulls" +
                  "?state=all&sort=updated&direction=desc&per_page=100";

        var response = await _apiClient.GetAsync(installation.InstallationId, url, cancellationToken);

        if (!response.IsSuccess)
            return Result<int>.Failure(response.Errors[0]);

        List<PullRequestPayload>? payload;

        try
        {
            payload = JsonSerializer.Deserialize<List<PullRequestPayload>>(response.Value!.Body);
        }
        catch (JsonException)
        {
            return Result<int>.Failure(new Error("GitHub.SyncFailed", $"GitHub returned an unreadable pull requests response for {fullName}."));
        }

        if (payload is null)
            return Result<int>.Failure(new Error("GitHub.SyncFailed", $"GitHub returned no pull requests list for {fullName}."));

        var touched = 0;

        foreach (var pull in payload)
        {
            var updatedAt = pull.UpdatedAt ?? DateTime.UtcNow;

            if (updatedAt < since)
                break;

            var existing = await _unitOfWork.GitHubPullRequests
                .GetByNumberIncludingDeletedAsync(repositoryRowId, pull.Number, cancellationToken);

            // Merged is derived, not reported: GitHub sends state "closed" with a merged_at
            // timestamp, so branching on state alone calls every merged PR closed.
            var state = pull.MergedAt is not null
                ? PullRequestState.Merged
                : string.Equals(pull.State, "closed", StringComparison.OrdinalIgnoreCase)
                    ? PullRequestState.Closed
                    : PullRequestState.Open;

            if (existing is null)
            {
                await _unitOfWork.GitHubPullRequests.AddAsync(new GitHubPullRequest
                {
                    GitHubRepositoryId = repositoryRowId,
                    CompanyId = installation.CompanyId,
                    Number = pull.Number,
                    Title = pull.Title ?? "",
                    Body = pull.Body,
                    State = state,
                    AuthorLogin = pull.User?.Login ?? "",
                    HeadBranch = pull.Head?.Ref ?? "",
                    OpenedAtUtc = pull.CreatedAt ?? updatedAt,
                    GitHubUpdatedAtUtc = updatedAt,
                    MergedAtUtc = pull.MergedAt,
                    HtmlUrl = pull.HtmlUrl ?? "",
                }, cancellationToken);
            }
            else
            {
                // A pull request is a state machine: everything mutable is overwritten.
                existing.CompanyId = installation.CompanyId;
                existing.Title = pull.Title ?? "";
                existing.Body = pull.Body;
                existing.State = state;
                existing.AuthorLogin = pull.User?.Login ?? "";
                existing.HeadBranch = pull.Head?.Ref ?? "";
                existing.GitHubUpdatedAtUtc = updatedAt;
                existing.MergedAtUtc = pull.MergedAt;
                existing.HtmlUrl = pull.HtmlUrl ?? "";
                // MergeTransitionAppliedAtUtc is deliberately NOT overwritten: it is
                // TaskSphere's own marker, not a GitHub-sourced field, and clearing it here
                // would re-apply every merge transition on the next sync.
                existing.IsDeleted = false;
                existing.DeletedAt = null;

                await _unitOfWork.GitHubPullRequests.Update(existing, cancellationToken);
            }

            touched++;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result<int>.Success(touched);
    }

    private sealed record PullRequestPayload(
        [property: JsonPropertyName("number")] int Number,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("state")] string? State,
        [property: JsonPropertyName("user")] PullRequestUserPayload? User,
        [property: JsonPropertyName("head")] PullRequestHeadPayload? Head,
        [property: JsonPropertyName("created_at")] DateTime? CreatedAt,
        [property: JsonPropertyName("updated_at")] DateTime? UpdatedAt,
        [property: JsonPropertyName("merged_at")] DateTime? MergedAt,
        [property: JsonPropertyName("html_url")] string? HtmlUrl);

    private sealed record PullRequestUserPayload(
        [property: JsonPropertyName("login")] string? Login);

    private sealed record PullRequestHeadPayload(
        [property: JsonPropertyName("ref")] string? Ref);
}
