using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.Enums;
using TaskSphere.Domain.Interfaces;

// The entities, not the namespace: importing TaskSphere.Domain.Entities wholesale pulls in an
// entity called Task and makes every bare Task in this file ambiguous.
using GitHubInstallation = TaskSphere.Domain.Entities.GitHubInstallation;
using GitHubRepository = TaskSphere.Domain.Entities.GitHubRepository;

namespace TaskSphere.Infrastructure.Services;

public class GitHubRepositorySyncService : IGitHubRepositorySyncService
{
    private const string RepositoriesUrl = "https://api.github.com/installation/repositories?per_page=100";

    // An installation with more repositories than this is not something B1 needs to walk
    // forever; the sync fails closed and the user can retry.
    private const int MaxPages = 50;

    private readonly IGitHubApiClient _apiClient;
    private readonly IUnitOfWork _unitOfWork;

    public GitHubRepositorySyncService(IGitHubApiClient apiClient, IUnitOfWork unitOfWork)
    {
        _apiClient = apiClient;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<int>> SyncAsync(GitHubInstallation installation, CancellationToken cancellationToken = default)
    {
        var seen = new HashSet<long>();
        RepositorySelection? selection = null;

        string? url = RepositoriesUrl;

        for (var page = 0; page < MaxPages && url is not null; page++)
        {
            var response = await _apiClient.GetAsync(installation.InstallationId, url, cancellationToken);

            if (!response.IsSuccess)
                return Result<int>.Failure(response.Errors[0]);

            RepositoriesResponse? payload;

            try
            {
                payload = JsonSerializer.Deserialize<RepositoriesResponse>(response.Value!.Body);
            }
            catch (JsonException)
            {
                return Failure("GitHub returned an unreadable repositories response.");
            }

            if (payload?.Repositories is null)
                return Failure("GitHub returned no repositories list.");

            if (selection is null)
            {
                if (!RepositorySelectionParser.TryParse(payload.RepositorySelection, out var parsed))
                {
                    return Result<int>.Failure(new Error(
                        "GitHub.UnknownRepositorySelection",
                        $"GitHub reported an unrecognised repository selection '{payload.RepositorySelection}'."));
                }

                selection = parsed;
            }

            foreach (var repository in payload.Repositories)
            {
                seen.Add(repository.Id);
                await UpsertAsync(installation, repository, cancellationToken);
            }

            url = NextPageUrl(response.Value.LinkHeader);
        }

        if (selection is not null && installation.RepositorySelection != selection)
        {
            installation.RepositorySelection = selection.Value;
            await _unitOfWork.GitHubInstallations.Update(installation, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await SoftDeleteRepositoriesAbsentFromGitHubAsync(installation, seen, cancellationToken);

        return Result<int>.Success(seen.Count);
    }

    /// <summary>
    /// Matched on GitHub's numeric id, never on <c>FullName</c> — a rename would otherwise
    /// orphan the old row and create a second one, taking every project link with it.
    /// </summary>
    private async Task UpsertAsync(GitHubInstallation installation, RepositoryPayload payload, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters, because IX_GitHubRepositories_RepositoryId is unfiltered (§0m).
        // After a disconnect the rows are soft-deleted but the index still holds their ids, so
        // a filtered lookup would find nothing, insert, and 500 on the first reconnect.
        var existing = await _unitOfWork.GitHubRepositories
            .GetByGitHubIdIncludingDeletedAsync(payload.Id, cancellationToken);

        if (existing is null)
        {
            await _unitOfWork.GitHubRepositories.AddAsync(new GitHubRepository
            {
                RepositoryId = payload.Id,
                GitHubInstallationId = installation.Id,
                CompanyId = installation.CompanyId,
                FullName = payload.FullName ?? "",
                DefaultBranch = payload.DefaultBranch ?? "",
                IsPrivate = payload.Private,
            }, cancellationToken);

            return;
        }

        if (existing.CompanyId != installation.CompanyId)
        {
            // Not in the plan, but reachable through the flow the plan documents: after a
            // GitHub-side uninstall a different company can connect the same org, and the
            // repository ids come back unchanged. The unfiltered unique index means there can
            // only ever be one row per repository, so ownership has to move — and the previous
            // company's project links must not move with it.
            await SoftDeleteLinksForRepositoryAsync(existing.Id, cancellationToken);
            existing.CompanyId = installation.CompanyId;
        }

        existing.GitHubInstallationId = installation.Id;
        existing.FullName = payload.FullName ?? "";
        existing.DefaultBranch = payload.DefaultBranch ?? "";
        existing.IsPrivate = payload.Private;
        // PullRequestsRefreshedAtUtc is deliberately NOT overwritten: it is
        // TaskSphere's own cooldown stamp, not a GitHub-sourced field, and clearing it
        // here would make every repository sync reset every cooldown.
        existing.IsDeleted = false;
        existing.DeletedAt = null;

        await _unitOfWork.GitHubRepositories.Update(existing, cancellationToken);
    }

    private async Task SoftDeleteLinksForRepositoryAsync(int repositoryId, CancellationToken cancellationToken)
    {
        var links = await _unitOfWork.ProjectRepositoryLinks
            .GetAll()
            .Where(l => l.GitHubRepositoryId == repositoryId)
            .ToListAsync(cancellationToken);

        foreach (var link in links)
        {
            link.IsDeleted = true;
            link.DeletedAt = DateTime.UtcNow;
            await _unitOfWork.ProjectRepositoryLinks.Update(link, cancellationToken);
        }
    }

    /// <summary>
    /// Repositories the installation can no longer see are soft-deleted, not removed: their
    /// project links stay behind so history does not lie.
    /// </summary>
    private async Task SoftDeleteRepositoriesAbsentFromGitHubAsync(
        GitHubInstallation installation,
        HashSet<long> seen,
        CancellationToken cancellationToken)
    {
        var stale = await _unitOfWork.GitHubRepositories
            .GetByCompany(installation.CompanyId)
            .Where(r => r.GitHubInstallationId == installation.Id)
            .ToListAsync(cancellationToken);

        var removed = stale.Where(r => !seen.Contains(r.RepositoryId)).ToList();

        if (removed.Count == 0)
            return;

        foreach (var repository in removed)
        {
            repository.IsDeleted = true;
            repository.DeletedAt = DateTime.UtcNow;
            await _unitOfWork.GitHubRepositories.Update(repository, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static string? NextPageUrl(string? linkHeader)
    {
        if (string.IsNullOrEmpty(linkHeader))
            return null;

        foreach (var segment in linkHeader.Split(','))
        {
            if (!segment.Contains("rel=\"next\"", StringComparison.OrdinalIgnoreCase))
                continue;

            var start = segment.IndexOf('<');
            var end = segment.IndexOf('>');

            if (start >= 0 && end > start)
                return segment[(start + 1)..end];
        }

        return null;
    }

    private static Result<int> Failure(string message)
        => Result<int>.Failure(new Error("GitHub.SyncFailed", message));

    private sealed record RepositoriesResponse(
        [property: JsonPropertyName("repository_selection")] string? RepositorySelection,
        [property: JsonPropertyName("repositories")] List<RepositoryPayload>? Repositories);

    private sealed record RepositoryPayload(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("full_name")] string? FullName,
        [property: JsonPropertyName("default_branch")] string? DefaultBranch,
        [property: JsonPropertyName("private")] bool Private);
}
