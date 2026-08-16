using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.GitHub;
using TaskSphere.Domain.Interfaces;

// The entities, not the namespace: TaskSphere.Domain.Entities.Task shadows Task otherwise.
using GitHubBranch = TaskSphere.Domain.Entities.GitHubBranch;
using GitHubInstallation = TaskSphere.Domain.Entities.GitHubInstallation;

namespace TaskSphere.Infrastructure.Services;

public class GitHubActivitySyncService : IGitHubActivitySyncService
{
    /// <summary>
    /// One number, one meaning — deliberately not configurable. A fixed window is always safe
    /// to re-run, which a per-repository watermark is not: force-push and rebase make "what
    /// changed since last time" genuinely hard to answer correctly.
    /// Nothing reads it yet: branches are fetched whole, so the window only starts applying
    /// when the commits pass lands.
    /// </summary>
    private const int SyncWindowDays = 30;

    private readonly IGitHubApiClient _apiClient;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IGitHubTaskLinkResolver _resolver;

    public GitHubActivitySyncService(
        IGitHubApiClient apiClient,
        IUnitOfWork unitOfWork,
        IGitHubTaskLinkResolver resolver)
    {
        _apiClient = apiClient;
        _unitOfWork = unitOfWork;
        _resolver = resolver;
    }

    public async Task<Result<SyncActivityResultDto>> SyncCompanyAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var installation = await _unitOfWork.GitHubInstallations.GetByCompanyAsync(companyId, cancellationToken);

        if (installation is null)
        {
            return Result<SyncActivityResultDto>.Failure(new Error(
                "GitHub.NotConnected",
                "This company is not connected to GitHub."));
        }

        var linkedRepositoryIds = await _unitOfWork.ProjectRepositoryLinks
            .GetByCompany(companyId)
            .Select(l => l.GitHubRepositoryId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var repositories = await _unitOfWork.GitHubRepositories
            .GetByCompany(companyId)
            .Where(r => linkedRepositoryIds.Contains(r.Id))
            .OrderBy(r => r.FullName)
            .ToListAsync(cancellationToken);

        var failures = new List<SyncFailureDto>();

        var synced = 0;
        var branchCount = 0;

        foreach (var repository in repositories)
        {
            var result = await SyncBranchesAsync(installation, repository.Id, repository.FullName, cancellationToken);

            if (!result.IsSuccess)
            {
                failures.Add(new SyncFailureDto(repository.FullName, result.Errors[0].Description));
                continue;
            }

            branchCount += result.Value;
            synced++;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var resolution = await _resolver.ResolveAsync(companyId, cancellationToken);

        if (synced > 0)
        {
            installation.ActivitySyncedAtUtc = DateTime.UtcNow;
            await _unitOfWork.GitHubInstallations.Update(installation, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        // Named, because six positional members of which four are ints is a transposition
        // waiting to happen — and it says out loud that the two zeroes are unimplemented
        // passes rather than counts that came back empty.
        return Result<SyncActivityResultDto>.Success(new SyncActivityResultDto(
            RepositoriesSynced: synced,
            Commits: 0,
            Branches: branchCount,
            PullRequests: 0,
            LinksCreated: resolution.LinksCreated,
            Failures: failures));
    }

    private async Task<Result<int>> SyncBranchesAsync(
        GitHubInstallation installation,
        int repositoryRowId,
        string fullName,
        CancellationToken cancellationToken)
    {
        var url = $"https://api.github.com/repos/{fullName}/branches?per_page=100";

        var response = await _apiClient.GetAsync(installation.InstallationId, url, cancellationToken);

        if (!response.IsSuccess)
            return Result<int>.Failure(response.Errors[0]);

        List<BranchPayload>? payload;

        try
        {
            payload = JsonSerializer.Deserialize<List<BranchPayload>>(response.Value!.Body);
        }
        catch (JsonException)
        {
            return Result<int>.Failure(new Error("GitHub.SyncFailed", $"GitHub returned an unreadable branches response for {fullName}."));
        }

        if (payload is null)
            return Result<int>.Failure(new Error("GitHub.SyncFailed", $"GitHub returned no branches list for {fullName}."));

        // The store decides what "the same branch" means, and it is case-insensitive:
        // GitHubBranches.Name sits under SQL_Latin1_General_CP1_CI_AS and
        // IX_GitHubBranches_RepositoryId_Name is unique, so two casings of one name are one
        // row. Comparing ordinally here soft-deleted a branch on the very pass that updated it
        // (payload "Feature-X" against stored "feature-x"), and two casings inside one payload
        // hit the index and threw. First casing seen wins; the second cannot get its own row.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var branch in payload)
        {
            if (string.IsNullOrEmpty(branch.Name))
                continue;

            if (!seen.Add(branch.Name))
                continue;

            // IgnoreQueryFilters, because IX_GitHubBranches_RepositoryId_Name is unfiltered.
            // A merged-then-recreated branch has a soft-deleted row that a filtered lookup
            // would miss, and the insert would violate the index.
            var existing = await _unitOfWork.GitHubBranches
                .GetByNameIncludingDeletedAsync(repositoryRowId, branch.Name, cancellationToken);

            if (existing is null)
            {
                await _unitOfWork.GitHubBranches.AddAsync(new GitHubBranch
                {
                    GitHubRepositoryId = repositoryRowId,
                    CompanyId = installation.CompanyId,
                    Name = branch.Name,
                    HeadSha = branch.Commit?.Sha ?? "",
                }, cancellationToken);

                continue;
            }

            existing.CompanyId = installation.CompanyId;
            existing.HeadSha = branch.Commit?.Sha ?? "";
            existing.IsDeleted = false;
            existing.DeletedAt = null;

            await _unitOfWork.GitHubBranches.Update(existing, cancellationToken);
        }

        // Flushed per repository rather than once at the end, so the change tracker holds one
        // repository's branches at a time. The absent pass below does not depend on it: every
        // row the loop touched is in `seen` by construction, and the pass only acts on the
        // live rows that are not.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var live = await _unitOfWork.GitHubBranches
            .GetByRepository(installation.CompanyId, repositoryRowId)
            .ToListAsync(cancellationToken);

        foreach (var branch in live.Where(b => !seen.Contains(b.Name)))
        {
            // Soft-deleted, never removed: the TaskLink stays behind so a task's history does
            // not lose the branch it was worked on.
            branch.IsDeleted = true;
            branch.DeletedAt = DateTime.UtcNow;
            await _unitOfWork.GitHubBranches.Update(branch, cancellationToken);
        }

        return Result<int>.Success(seen.Count);
    }

    private sealed record BranchPayload(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("commit")] BranchCommitPayload? Commit);

    private sealed record BranchCommitPayload(
        [property: JsonPropertyName("sha")] string? Sha);
}
