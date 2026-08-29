using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.GitHub;
using TaskSphere.Domain.Interfaces;

using GitHubBranchCommit = TaskSphere.Domain.Entities.GitHubBranchCommit;
using GitHubInstallation = TaskSphere.Domain.Entities.GitHubInstallation;

namespace TaskSphere.Infrastructure.Services;

/// <summary>
/// The commits pass, shared by the company-wide sync and the task-scoped refresh. It lives
/// here rather than inside either caller so the ahead-set difference and its four fail-closed
/// guards cannot drift apart between the two paths.
/// </summary>
public class GitHubCommitMirror
{
    private readonly IGitHubApiClient _apiClient;
    private readonly IUnitOfWork _unitOfWork;

    public GitHubCommitMirror(IGitHubApiClient apiClient, IUnitOfWork unitOfWork)
    {
        _apiClient = apiClient;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// One listing per branch, filtered by <c>since</c>. A commit reachable from two branches
    /// comes back twice and collapses on the natural key, which is why the upsert is keyed on
    /// (repository, sha) rather than on the branch it arrived through.
    /// <para>
    /// Failures are collected per branch rather than returned: one listing that does not come
    /// back says nothing about the other thirty-nine, and the rows already added for them are
    /// flushed by the caller either way. Returning a <c>Result</c> here made the summary
    /// under-report work that had in fact been persisted.
    /// </para>
    /// </summary>
    public async Task<(int Inserted, List<SyncFailureDto> Failures)> RefreshAsync(
        GitHubInstallation installation,
        int repositoryRowId,
        string fullName,
        List<string> branches,
        string defaultBranch,
        DateTime since,
        CancellationToken cancellationToken)
    {
        var inserted = 0;
        var failures = new List<SyncFailureDto>();

        // Case-insensitively, because GitHubBranches.Name is SQL_Latin1_General_CP1_CI_AS and
        // comparing ordinally here is the 2026-08-16 soft-delete defect in a new place.
        var defaultName = branches.FirstOrDefault(b => string.Equals(b, defaultBranch, StringComparison.OrdinalIgnoreCase));

        // Default first, explicitly rather than by sorting on a bool: every later branch is
        // differenced against its shas, so it must already be in hand.
        var ordered = new List<string>();

        if (defaultName is not null)
            ordered.Add(defaultName);

        ordered.AddRange(branches.Where(b => !string.Equals(b, defaultName, StringComparison.OrdinalIgnoreCase)));

        // Empty is not "nothing is ahead" — it is "everything is ahead", which is the outcome
        // the whole definition exists to avoid. The guards below (a failed, unreadable, missing,
        // or truncated default-branch listing) keep that failure closed.
        var defaultShas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inheritanceEnabled = defaultName is not null;

        foreach (var branch in ordered)
        {
            var isDefault = string.Equals(branch, defaultName, StringComparison.OrdinalIgnoreCase);
            // Formatted invariantly, not interpolated: ':' in a custom format string is the
            // culture's TIME SEPARATOR, so `$"{since:...HH:mm:ss}"` emits "10.00.00Z" under a
            // culture like fi-FI and GitHub rejects the timestamp. Proven with a probe, not
            // reasoned about — and no test could have caught it, because DateTime.Parse with a
            // null provider reads the same ambient culture the bug came from.
            var sinceParameter = since.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

            // Escaped: branch names contain slashes, and an unescaped one changes the path
            // rather than the query.
            var url = $"https://api.github.com/repos/{fullName}/commits?sha={Uri.EscapeDataString(branch)}" +
                      $"&since={sinceParameter}" +
                      "&per_page=100";

            var response = await _apiClient.GetAsync(installation.InstallationId, url, cancellationToken);

            if (!response.IsSuccess)
            {
                failures.Add(new SyncFailureDto(fullName, response.Errors[0].Description, branch));

                // Fail closed: with no default listing, "not in defaultShas" would be true of
                // every commit in the repository.
                if (isDefault)
                    inheritanceEnabled = false;

                continue;
            }

            List<CommitPayload>? payload;

            try
            {
                payload = JsonSerializer.Deserialize<List<CommitPayload>>(response.Value!.Body);
            }
            catch (JsonException)
            {
                failures.Add(new SyncFailureDto(fullName, $"GitHub returned an unreadable commits response for {fullName}.", branch));

                if (isDefault)
                    inheritanceEnabled = false;

                continue;
            }

            if (payload is null)
            {
                failures.Add(new SyncFailureDto(fullName, $"GitHub returned no commits list for {fullName}.", branch));

                if (isDefault)
                    inheritanceEnabled = false;

                continue;
            }

            // The set difference is only correct on a complete default set: a partial page
            // cannot distinguish "not on the default branch" from "on the default branch's
            // page 2". Failing open here would claim the default branch's own older history
            // as inherited — the "everything reachable" outcome the whole definition exists
            // to avoid. Commits still ingest below; only inheritance is disabled.
            if (isDefault && response.Value!.LinkHeader?.Contains("rel=\"next\"") == true)
            {
                failures.Add(new SyncFailureDto(fullName, $"GitHub's commit history for {fullName} on {branch} is more than one page; inheritance was skipped.", branch));
                inheritanceEnabled = false;
            }

            foreach (var commit in payload)
            {
                if (string.IsNullOrEmpty(commit.Sha))
                    continue;

                if (isDefault)
                    defaultShas.Add(commit.Sha);

                var existing = await _unitOfWork.GitHubCommits
                    .GetByShaIncludingDeletedAsync(repositoryRowId, commit.Sha, cancellationToken);

                int commitRowId;

                if (existing is not null)
                {
                    // A commit is immutable; the only thing worth doing to an existing row is
                    // reviving it, so a repository that came back is not missing its history.
                    if (existing.IsDeleted)
                    {
                        existing.IsDeleted = false;
                        existing.DeletedAt = null;
                        await _unitOfWork.GitHubCommits.Update(existing, cancellationToken);
                        await _unitOfWork.SaveChangesAsync(cancellationToken);
                    }

                    commitRowId = existing.Id;
                }
                else
                {
                    var added = new TaskSphere.Domain.Entities.GitHubCommit
                    {
                        GitHubRepositoryId = repositoryRowId,
                        CompanyId = installation.CompanyId,
                        Sha = commit.Sha,
                        Message = commit.Commit?.Message ?? "",
                        AuthorName = commit.Commit?.Author?.Name ?? "",
                        AuthorLogin = commit.Author?.Login,
                        CommittedAtUtc = commit.Commit?.Author?.Date ?? DateTime.UtcNow,
                        HtmlUrl = commit.HtmlUrl ?? "",
                    };

                    await _unitOfWork.GitHubCommits.AddAsync(added, cancellationToken);
                    inserted++;

                    // Saved per commit so the next iteration's GetByShaIncludingDeletedAsync
                    // sees it: the same sha can arrive twice within one run, from two branches.
                    await _unitOfWork.SaveChangesAsync(cancellationToken);

                    commitRowId = added.Id;
                }

                await RecordAheadAsync(branch, commitRowId, commit.Sha);
            }
        }

        async System.Threading.Tasks.Task RecordAheadAsync(string branch, int commitRowId, string sha)
        {
            if (!inheritanceEnabled)
                return;

            // A commit on main is not ahead of main.
            if (string.Equals(branch, defaultName, StringComparison.OrdinalIgnoreCase))
                return;

            if (defaultShas.Contains(sha))
                return;

            var branchRow = await _unitOfWork.GitHubBranches
                .GetByNameIncludingDeletedAsync(repositoryRowId, branch, cancellationToken);

            if (branchRow is null)
                return;

            if (await _unitOfWork.GitHubBranchCommits.ExistsForPairAsync(branchRow.Id, commitRowId, cancellationToken))
                return;

            await _unitOfWork.GitHubBranchCommits.AddAsync(new GitHubBranchCommit
            {
                CompanyId = installation.CompanyId,
                GitHubBranchId = branchRow.Id,
                GitHubCommitId = commitRowId,
            }, cancellationToken);

            // On the same per-commit save the commit upsert uses, so a mid-loop failure never
            // leaves a commit in the mirror with its ahead-ness lost.
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return (inserted, failures);
    }

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
