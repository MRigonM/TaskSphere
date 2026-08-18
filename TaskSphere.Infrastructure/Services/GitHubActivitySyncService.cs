using System.Globalization;
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
    /// The window bounds the commits query: only commits after DateTime.UtcNow.AddDays(-SyncWindowDays)
    /// are fetched, and it is applied on every run so the pass is naturally idempotent.
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
        var commitCount = 0;
        var since = DateTime.UtcNow.AddDays(-SyncWindowDays);

        foreach (var repository in repositories)
        {
            var branchResult = await SyncBranchesAsync(installation, repository.Id, repository.FullName, cancellationToken);

            if (!branchResult.IsSuccess)
            {
                failures.Add(new SyncFailureDto(repository.FullName, branchResult.Errors[0].Description));
                continue;
            }

            branchCount += branchResult.Value!.Count;

            var commitResult = await SyncCommitsAsync(
                installation, repository.Id, repository.FullName, branchResult.Value!, since, cancellationToken);

            if (!commitResult.IsSuccess)
            {
                failures.Add(new SyncFailureDto(repository.FullName, commitResult.Errors[0].Description));
                continue;
            }

            commitCount += commitResult.Value;
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
        // waiting to happen — and it says out loud that the zero is an unimplemented
        // pass rather than a count that came back empty.
        return Result<SyncActivityResultDto>.Success(new SyncActivityResultDto(
            RepositoriesSynced: synced,
            Commits: commitCount,
            Branches: branchCount,
            PullRequests: 0,
            LinksCreated: resolution.LinksCreated,
            Failures: failures));
    }

    private async Task<Result<List<string>>> SyncBranchesAsync(
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

    /// <summary>
    /// One listing per branch, filtered by <c>since</c>. A commit reachable from two branches
    /// comes back twice and collapses on the natural key, which is why the upsert is keyed on
    /// (repository, sha) rather than on the branch it arrived through.
    /// </summary>
    private async Task<Result<int>> SyncCommitsAsync(
        GitHubInstallation installation,
        int repositoryRowId,
        string fullName,
        List<string> branches,
        DateTime since,
        CancellationToken cancellationToken)
    {
        var inserted = 0;

        foreach (var branch in branches)
        {
            // Formatted invariantly, not interpolated: ':' in a custom format string is the
            // culture's TIME SEPARATOR, so `$"{since:...HH:mm:ss}"` emits "10.00.00Z" under a
            // culture like fi-FI and GitHub rejects the timestamp. Proven with a probe, not
            // reasoned about — and no test could have caught it, because DateTime.Parse with a
            // null provider reads the same ambient culture the bug came from.
            var sinceParameter = since.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

            // Escaped: branch names contain slashes, and an unescaped one changes the path
            // rather than the query.
            var url = $"https://api.github.com/repos/{fullName}/commits" +
                      $"?sha={Uri.EscapeDataString(branch)}" +
                      $"&since={sinceParameter}" +
                      "&per_page=100";

            var response = await _apiClient.GetAsync(installation.InstallationId, url, cancellationToken);

            if (!response.IsSuccess)
                return Result<int>.Failure(response.Errors[0]);

            List<CommitPayload>? payload;

            try
            {
                payload = JsonSerializer.Deserialize<List<CommitPayload>>(response.Value!.Body);
            }
            catch (JsonException)
            {
                return Result<int>.Failure(new Error("GitHub.SyncFailed", $"GitHub returned an unreadable commits response for {fullName}."));
            }

            if (payload is null)
                return Result<int>.Failure(new Error("GitHub.SyncFailed", $"GitHub returned no commits list for {fullName}."));

            foreach (var commit in payload)
            {
                if (string.IsNullOrEmpty(commit.Sha))
                    continue;

                var existing = await _unitOfWork.GitHubCommits
                    .GetByShaIncludingDeletedAsync(repositoryRowId, commit.Sha, cancellationToken);

                if (existing is not null)
                {
                    // A commit is immutable; the only thing worth doing to an existing row is
                    // reviving it, so a repository that came back is not missing its history.
                    if (existing.IsDeleted)
                    {
                        existing.IsDeleted = false;
                        existing.DeletedAt = null;
                        await _unitOfWork.GitHubCommits.Update(existing, cancellationToken);
                    }

                    continue;
                }

                await _unitOfWork.GitHubCommits.AddAsync(new TaskSphere.Domain.Entities.GitHubCommit
                {
                    GitHubRepositoryId = repositoryRowId,
                    CompanyId = installation.CompanyId,
                    Sha = commit.Sha,
                    Message = commit.Commit?.Message ?? "",
                    AuthorName = commit.Commit?.Author?.Name ?? "",
                    AuthorLogin = commit.Author?.Login,
                    CommittedAtUtc = commit.Commit?.Author?.Date ?? DateTime.UtcNow,
                    HtmlUrl = commit.HtmlUrl ?? "",
                }, cancellationToken);

                inserted++;

                // Saved per commit so the next iteration's GetByShaIncludingDeletedAsync sees
                // it: the same sha can arrive twice within one run, from two branches.
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }

        return Result<int>.Success(inserted);
    }

    private sealed record BranchPayload(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("commit")] BranchCommitPayload? Commit);

    private sealed record BranchCommitPayload(
        [property: JsonPropertyName("sha")] string? Sha);

    private sealed record CommitPayload(
        [property: JsonPropertyName("sha")] string? Sha,
        [property: JsonPropertyName("html_url")] string? HtmlUrl,
        [property: JsonPropertyName("commit")] CommitDetailPayload? Commit,
        [property: JsonPropertyName("author")] CommitAuthorAccountPayload? Author);

    private sealed record CommitDetailPayload(
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("author")] CommitAuthorPayload? Author);

    private sealed record CommitAuthorPayload(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("date")] DateTime? Date);

    /// <summary>Null when GitHub cannot match the commit to an account.</summary>
    private sealed record CommitAuthorAccountPayload(
        [property: JsonPropertyName("login")] string? Login);
}
