using Microsoft.EntityFrameworkCore;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Audit;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.Enums;
using TaskSphere.Domain.Interfaces;

using TaskEntity = TaskSphere.Domain.Entities.Task;

namespace TaskSphere.Infrastructure.Services;

public class MergeTransitionService : IMergeTransitionService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly AuditQueue _auditQueue;

    public MergeTransitionService(IUnitOfWork unitOfWork, AuditQueue auditQueue)
    {
        _unitOfWork = unitOfWork;
        _auditQueue = auditQueue;
    }

    public async Task<Result<MergeTransitionResult>> ApplyAsync(
        Guid companyId,
        string? actorUsername,
        CancellationToken cancellationToken = default)
    {
        var pending = await _unitOfWork.GitHubPullRequests
            .GetByCompany(companyId)
            .Where(p => p.State == PullRequestState.Merged && p.MergeTransitionAppliedAtUtc == null)
            .Select(p => new { p.Id, p.GitHubRepositoryId, p.Number, p.HeadBranch })
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
            return Result<MergeTransitionResult>.Success(new MergeTransitionResult(0, 0, 0));

        var map = await TaskKeyResolutionMap.BuildAsync(_unitOfWork, companyId, cancellationToken);

        var autoDoneByProjectId = await _unitOfWork.Projects
            .GetCompanyProjects(companyId)
            .Select(p => new { p.Id, p.AutoDoneOnMerge })
            .ToDictionaryAsync(p => p.Id, p => p.AutoDoneOnMerge, cancellationToken);

        var transitioned = 0;
        var skipped = 0;
        var failed = 0;

        foreach (var pull in pending)
        {
            try
            {
                var movedHere = 0;

                foreach (var key in TaskKeyScanner.Scan(pull.HeadBranch))
                {
                    var taskId = map.Resolve(key, pull.GitHubRepositoryId);
                    if (taskId is null)
                        continue;

                    var task = await _unitOfWork.Tasks.GetByIdAsync(taskId.Value, cancellationToken);
                    if (task is null)
                        continue;

                    if (task.ProjectId is null ||
                        !autoDoneByProjectId.TryGetValue(task.ProjectId.Value, out var autoDone) ||
                        !autoDone)
                        continue;

                    if (task.Status != TaskStatuses.Open && task.Status != TaskStatuses.InProgress)
                        continue;

                    task.Status = TaskStatuses.Done;
                    await _unitOfWork.Tasks.Update(task, cancellationToken);

                    Enqueue(companyId, actorUsername, key.ToString(), pull.Number);
                    movedHere++;
                }

                var stored = await _unitOfWork.GitHubPullRequests.GetByIdAsync(pull.Id, cancellationToken);
                if (stored is not null)
                {
                    // Stamped unconditionally: a pull request considered once is never
                    // reconsidered, whether it moved a task, was opted out, or named nothing.
                    stored.MergeTransitionAppliedAtUtc = DateTime.UtcNow;
                    await _unitOfWork.GitHubPullRequests.Update(stored, cancellationToken);
                }

                // Per pull request, so a later failure cannot discard earlier work. The unit
                // reported must equal the unit persisted.
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                transitioned += movedHere;
                if (movedHere == 0)
                    skipped++;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                failed++;
            }
        }

        return Result<MergeTransitionResult>.Success(
            new MergeTransitionResult(transitioned, skipped, failed));
    }

    private void Enqueue(Guid companyId, string? actorUsername, string taskKey, int pullNumber)
    {
        // AuditEntry is HTTP-shaped; a sync-driven transition has no request, so those fields
        // stay empty. The audit dashboard must render such a row.
        _auditQueue.TryWrite(new AuditEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            CompanyId = companyId,
            Username = actorUsername,
            Action = $"Moved {taskKey} to Done — pull request #{pullNumber} was merged",
        });
    }
}
