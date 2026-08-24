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
            .Select(p => new { p.Id, p.GitHubRepositoryId, p.Title, p.Body })
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
            // Title and body scanned as one text, so a pull request that names a task in both
            // places is one mention rather than two: TaskKeyScanner.Scan already returns each
            // distinct key once per text. The per-kind suppression below would collapse the
            // second link either way; what this keeps honest is the KeysSeen count.
            var text = string.IsNullOrEmpty(pull.Body) ? pull.Title : pull.Title + "\n" + pull.Body;
            await LinkAll(text, pull.GitHubRepositoryId, pullRequestId: pull.Id);
        }

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
    }
}
