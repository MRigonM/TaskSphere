using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Interfaces;
using TaskSphere.Application.Interfaces;

// The entities, not the namespace: TaskSphere.Domain.Entities.Task shadows Task otherwise.
using GitHubBranch = TaskSphere.Domain.Entities.GitHubBranch;
using GitHubInstallation = TaskSphere.Domain.Entities.GitHubInstallation;

namespace TaskSphere.Infrastructure.Services;

/// <summary>
/// One GitHub call per repository, upserted into the mirror. Extracted from
/// <see cref="GitHubActivitySyncService"/> because two callers need it: the full company sync,
/// and the project-scoped refresh a board triggers.
/// <para>
/// This is the cheap half of a sync. The commits pass costs 1 + B calls for B branches; this
/// costs one, and it is the input the task's Activity tab reads through TaskLink rows.
/// </para>
/// </summary>
public class GitHubBranchMirror
{
    private readonly IGitHubApiClient _apiClient;
    private readonly IUnitOfWork _unitOfWork;

    public GitHubBranchMirror(IGitHubApiClient apiClient, IUnitOfWork unitOfWork)
    {
        _apiClient = apiClient;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<List<string>>> RefreshAsync(
        GitHubInstallation installation,
        int repositoryRowId,
        string fullName,
        CancellationToken cancellationToken)
    {
        var url = $"https://api.github.com/repos/{fullName}/branches?per_page=100";

        var response = await _apiClient.GetAsync(installation.InstallationId, url, cancellationToken);

        if (!response.IsSuccess)
            return Result<List<string>>.Failure(response.Errors[0]);

        List<BranchPayload>? payload;

        try
        {
            payload = JsonSerializer.Deserialize<List<BranchPayload>>(response.Value!.Body);
        }
        catch (JsonException)
        {
            return Result<List<string>>.Failure(new Error("GitHub.SyncFailed", $"GitHub returned an unreadable branches response for {fullName}."));
        }

        if (payload is null)
            return Result<List<string>>.Failure(new Error("GitHub.SyncFailed", $"GitHub returned no branches list for {fullName}."));

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

        if (response.Value!.LinkHeader?.Contains("rel=\"next\"") == true)
        {
            // The absent pass is only correct on a complete set. A partial page cannot
            // distinguish "gone" from "on page 2".
            return Result<List<string>>.Success(seen.ToList());
        }

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

        return Result<List<string>>.Success(seen.ToList());
    }

    private sealed record BranchPayload(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("commit")] BranchCommitPayload? Commit);

    private sealed record BranchCommitPayload(
        [property: JsonPropertyName("sha")] string? Sha);
}
