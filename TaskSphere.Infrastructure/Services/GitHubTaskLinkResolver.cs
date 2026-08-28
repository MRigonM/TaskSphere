using Microsoft.EntityFrameworkCore;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.Interfaces;

// The entity, not the namespace — preventively, and following GitHubProjectLinkService and
// GitHubRepositorySyncService. A wholesale `using TaskSphere.Domain.Entities;` compiles clean today:
// the only Task in this file is the generic Task<TaskLinkResolution>, and arity-1 never
// collides with the non-generic entity. It stops compiling the moment anyone writes a bare
// Task — Task.WhenAll, Task.CompletedTask — which is then CS0104, "'Task' is an ambiguous
// reference between 'TaskSphere.Domain.Entities.Task' and 'System.Threading.Tasks.Task'".
using TaskLink = TaskSphere.Domain.Entities.TaskLink;

namespace TaskSphere.Infrastructure.Services;

public class GitHubTaskLinkResolver : IGitHubTaskLinkResolver
{
    private readonly IUnitOfWork _unitOfWork;

    public GitHubTaskLinkResolver(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<TaskLinkResolution> ResolveAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var map = await TaskKeyResolutionMap.BuildAsync(_unitOfWork, companyId, cancellationToken);

        // Existing links, so a re-run inserts nothing. Keyed by the kind's FK as well as the
        // task, because the three unique indexes are per-kind.
        var existing = (await _unitOfWork.TaskLinks
                .GetByCompany(companyId)
                .Select(l => new { l.TaskId, l.GitHubCommitId, l.GitHubBranchId, l.GitHubPullRequestId })
                .ToListAsync(cancellationToken))
            .Select(l => (l.TaskId, l.GitHubCommitId, l.GitHubBranchId, l.GitHubPullRequestId))
            .ToHashSet();

        var commits = await _unitOfWork.GitHubCommits
            .GetByCompany(companyId)
            .Select(c => new { c.Id, c.GitHubRepositoryId, c.Message })
            .ToListAsync(cancellationToken);

        var branches = await _unitOfWork.GitHubBranches
            .GetByCompany(companyId)
            .Select(b => new { b.Id, b.GitHubRepositoryId, b.Name })
            .ToListAsync(cancellationToken);

        var pulls = await _unitOfWork.GitHubPullRequests
            .GetByCompany(companyId)
            .Select(p => new { p.Id, p.GitHubRepositoryId, p.Title, p.Body, p.HeadBranch })
            .ToListAsync(cancellationToken);

        var created = 0;
        var seen = 0;
        var unresolved = 0;

        foreach (var commit in commits)
            await LinkAll(commit.Message, commit.GitHubRepositoryId, commitId: commit.Id);

        foreach (var branch in branches)
            await LinkAll(branch.Name, branch.GitHubRepositoryId, branchId: branch.Id);

        foreach (var pull in pulls)
        {
            // Title, body, and head branch are scanned as one text, so a pull request that
            // names a task in multiple places is one mention rather than many: TaskKeyScanner.Scan
            // already returns each distinct key once per text. The per-kind suppression below
            // would collapse duplicate links either way; what this keeps honest is the
            // KeysSeen count. The head branch is included because the merge → Done transition
            // decides by head branch, so a pull request that can move a task by its branch must
            // also be able to link by it, or the task's history omits the very pull request that
            // closed it.
            var text = string.IsNullOrEmpty(pull.Body) ? pull.Title : pull.Title + "\n" + pull.Body;
            text = string.IsNullOrEmpty(pull.HeadBranch) ? text : text + "\n" + pull.HeadBranch;
            await LinkAll(text, pull.GitHubRepositoryId, pullRequestId: pull.Id);
        }

        // Fourth pass, and deliberately last: the three text passes must have claimed their
        // tuples first, so a commit that names the task itself reads as direct rather than
        // inherited. See InheritFromBranchesAsync.
        await InheritFromBranchesAsync();

        if (created > 0)
            await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new TaskLinkResolution(created, seen, unresolved);

        async Task LinkAll(string text, int gitHubRepositoryId, int? commitId = null, int? branchId = null, int? pullRequestId = null)
        {
            foreach (var key in TaskKeyScanner.Scan(text))
            {
                seen++;

                var taskId = map.Resolve(key, gitHubRepositoryId);

                if (taskId is null)
                {
                    unresolved++;
                    continue;
                }

                if (!existing.Add((taskId.Value, commitId, branchId, pullRequestId)))
                    continue;

                await _unitOfWork.TaskLinks.AddAsync(new TaskLink
                {
                    CompanyId = companyId,
                    TaskId = taskId.Value,
                    GitHubCommitId = commitId,
                    GitHubBranchId = branchId,
                    GitHubPullRequestId = pullRequestId,
                }, cancellationToken);

                created++;
            }
        }

        async Task InheritFromBranchesAsync()
        {
            // `existing` is the reason this works in a single run. It was seeded from the
            // database and has been added to by the branch pass, so it holds both the branch
            // links that already existed and the ones created moments ago — which are still
            // unsaved and invisible to a fresh query. Reading the database here instead would
            // make a newly linked branch inherit nothing until the NEXT sync.
            //
            // Materialized before the loop: the loop adds to `existing`, and iterating a
            // HashSet while adding to it throws.
            var branchLinks = existing
                .Where(e => e.GitHubBranchId is not null)
                .Select(e => (e.TaskId, BranchId: e.GitHubBranchId!.Value))
                .ToList();

            if (branchLinks.Count == 0)
                return;

            var branchIds = branchLinks.Select(l => l.BranchId).Distinct().ToList();

            var aheadRows = await _unitOfWork.GitHubBranchCommits
                .GetByCompany(companyId)
                .Where(bc => branchIds.Contains(bc.GitHubBranchId))
                .Select(bc => new { bc.GitHubBranchId, bc.GitHubCommitId })
                .ToListAsync(cancellationToken);

            var commitsByBranch = aheadRows
                .GroupBy(r => r.GitHubBranchId)
                .ToDictionary(g => g.Key, g => g.Select(r => r.GitHubCommitId).ToList());

            foreach (var (taskId, branchId) in branchLinks)
            {
                if (!commitsByBranch.TryGetValue(branchId, out var commitIds))
                    continue;

                foreach (var commitId in commitIds)
                {
                    // The SAME tuple shape the commit pass uses — ViaGitHubBranchId is not part
                    // of it, and must never become part of it. Widening the tuple would let a
                    // direct link and an inherited one for one commit both look new, and they
                    // collide on IX_TaskLinks_TaskId_CommitId. Task 7 pins this.
                    //
                    // When two linked branches are both ahead of the same commit for the same
                    // task, only one link can exist — IX_TaskLinks_TaskId_CommitId is unique —
                    // so the surviving ViaGitHubBranchId is whichever branch this enumeration
                    // reached first. The attribution is arbitrary; the link is not.
                    if (!existing.Add((taskId, commitId, null, null)))
                        continue;

                    await _unitOfWork.TaskLinks.AddAsync(new TaskLink
                    {
                        CompanyId = companyId,
                        TaskId = taskId,
                        GitHubCommitId = commitId,
                        ViaGitHubBranchId = branchId,
                    }, cancellationToken);

                    created++;
                }
            }
        }
    }
}
