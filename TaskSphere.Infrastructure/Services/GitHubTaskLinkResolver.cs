using Microsoft.EntityFrameworkCore;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.Interfaces;

// The entity, not the namespace — preventively, and by the convention every other GitHub
// service here follows. A wholesale `using TaskSphere.Domain.Entities;` compiles clean today:
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
        // Everything below is company-scoped and filtered. A soft-deleted link must not
        // authorize, and a soft-deleted project must not resolve a key.
        var projectsByKey = await _unitOfWork.Projects
            .GetCompanyProjects(companyId)
            .Select(p => new { p.Id, p.Key })
            .ToDictionaryAsync(p => p.Key, p => p.Id, StringComparer.Ordinal, cancellationToken);

        var authorized = (await _unitOfWork.ProjectRepositoryLinks
                .GetByCompany(companyId)
                .Select(l => new { l.ProjectId, l.GitHubRepositoryId })
                .ToListAsync(cancellationToken))
            .Select(l => (l.ProjectId, l.GitHubRepositoryId))
            .ToHashSet();

        var taskIdByProjectAndNumber = (await _unitOfWork.Tasks
                .GetAll()
                .Where(t => t.CompanyId == companyId && t.ProjectId != null)
                .Select(t => new { t.Id, ProjectId = t.ProjectId!.Value, t.Number })
                .ToListAsync(cancellationToken))
            .ToDictionary(t => (t.ProjectId, t.Number), t => t.Id);

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

        var created = 0;
        var seen = 0;
        var unresolved = 0;

        foreach (var commit in commits)
        {
            foreach (var key in TaskKeyScanner.Scan(commit.Message))
            {
                seen++;

                var taskId = ResolveTask(key, commit.GitHubRepositoryId);

                if (taskId is null)
                {
                    unresolved++;
                    continue;
                }

                var identity = (taskId.Value, (int?)commit.Id, (int?)null, (int?)null);

                if (!existing.Add(identity))
                    continue;

                await _unitOfWork.TaskLinks.AddAsync(new TaskLink
                {
                    CompanyId = companyId,
                    TaskId = taskId.Value,
                    GitHubCommitId = commit.Id,
                }, cancellationToken);

                created++;
            }
        }

        if (created > 0)
            await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new TaskLinkResolution(created, seen, unresolved);

        // Steps 1-3 of the spec's resolution order, in order. Step 2 is the authorization
        // boundary: without it, push access to any repository under the installation would be
        // enough to attach activity to any project.
        int? ResolveTask(TaskKey key, int gitHubRepositoryId)
        {
            if (!projectsByKey.TryGetValue(key.ProjectKey, out var projectId))
                return null;

            if (!authorized.Contains((projectId, gitHubRepositoryId)))
                return null;

            return taskIdByProjectAndNumber.TryGetValue((projectId, key.Number), out var taskId)
                ? taskId
                : null;
        }
    }
}
