using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.Interfaces;

namespace TaskSphere.Infrastructure.Services;

/// <summary>
/// A company-scoped snapshot of everything needed to route a task key to a task id, and the
/// one rule that authorizes it: a key is honoured only when the record's repository is linked
/// to that key's project. Keys route, the repo link authorizes.
/// <para>
/// Extracted so the boundary exists exactly once. Two callers hand-rolling it would drift, and
/// the drift's failure mode is writing to another project's tasks.
/// </para>
/// </summary>
public sealed class TaskKeyResolutionMap
{
    private readonly Dictionary<string, int> _projectIdByKey;
    private readonly HashSet<(int ProjectId, int RepositoryId)> _authorized;
    private readonly Dictionary<(int ProjectId, int Number), int> _taskIdByProjectAndNumber;

    private TaskKeyResolutionMap(
        Dictionary<string, int> projectIdByKey,
        HashSet<(int, int)> authorized,
        Dictionary<(int, int), int> taskIdByProjectAndNumber)
    {
        _projectIdByKey = projectIdByKey;
        _authorized = authorized;
        _taskIdByProjectAndNumber = taskIdByProjectAndNumber;
    }

    public static async Task<TaskKeyResolutionMap> BuildAsync(
        IUnitOfWork unitOfWork,
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        // Everything below is company-scoped and filtered. A soft-deleted link must not
        // authorize, and a soft-deleted project must not resolve a key.
        var projectsByKey = await unitOfWork.Projects
            .GetCompanyProjects(companyId)
            .Select(p => new { p.Id, p.Key })
            .ToDictionaryAsync(p => p.Key, p => p.Id, StringComparer.Ordinal, cancellationToken);

        var authorized = (await unitOfWork.ProjectRepositoryLinks
                .GetByCompany(companyId)
                .Select(l => new { l.ProjectId, l.GitHubRepositoryId })
                .ToListAsync(cancellationToken))
            .Select(l => (l.ProjectId, l.GitHubRepositoryId))
            .ToHashSet();

        var taskIdByProjectAndNumber = (await unitOfWork.Tasks
                .GetAll()
                .Where(t => t.CompanyId == companyId && t.ProjectId != null)
                .Select(t => new { t.Id, ProjectId = t.ProjectId!.Value, t.Number })
                .ToListAsync(cancellationToken))
            .ToDictionary(t => (t.ProjectId, t.Number), t => t.Id);

        return new TaskKeyResolutionMap(projectsByKey, authorized, taskIdByProjectAndNumber);
    }

    /// <summary>
    /// Steps 1-3 of the spec's resolution order, in order. Step 2 is the authorization
    /// boundary. Returns null when the key routes nowhere — normal traffic, never an error.
    /// </summary>
    public int? Resolve(TaskKey key, int gitHubRepositoryId)
    {
        if (!_projectIdByKey.TryGetValue(key.ProjectKey, out var projectId))
            return null;

        if (!_authorized.Contains((projectId, gitHubRepositoryId)))
            return null;

        return _taskIdByProjectAndNumber.TryGetValue((projectId, key.Number), out var taskId)
            ? taskId
            : null;
    }
}
