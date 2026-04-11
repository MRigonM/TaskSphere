using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.Task;
using TaskSphere.Domain.Enums;
using TaskSphere.Domain.Interfaces;

namespace TaskSphere.Application.Services;

public class TaskValidationService : ITaskValidationService
{
    private static readonly HashSet<string> AllowedStatuses =
    [
        TaskStatuses.Open,
        TaskStatuses.InProgress,
        TaskStatuses.Blocked,
        TaskStatuses.Done
    ];

    private static readonly HashSet<string> AllowedPriorities =
    [
        TaskPriority.Low,
        TaskPriority.Medium,
        TaskPriority.High,
        TaskPriority.Critical
    ];

    private readonly IAccessControlService _accessControl;
    private readonly IProjectRepository _projectRepository;
    private readonly ISprintRepository _sprintRepository;
    private readonly ITaskRepository _taskRepository;

    public TaskValidationService(
        IAccessControlService accessControl,
        IProjectRepository projectRepository,
        ISprintRepository sprintRepository,
        ITaskRepository taskRepository)
    {
        _accessControl = accessControl;
        _projectRepository = projectRepository;
        _sprintRepository = sprintRepository;
        _taskRepository = taskRepository;
    }

    public async Task<Result<ValidatedTaskInput>> ValidateTaskCreateAsync(Guid companyId, CreateTaskDto dto, CancellationToken ct = default)
    {
        var payloadValidation = await ValidateTaskPayloadCoreAsync(companyId, dto.ProjectId, dto.SprintId, dto.Status, dto.Priority, ct);
        if (!payloadValidation.IsSuccess)
            return Result<ValidatedTaskInput>.Failure(payloadValidation.Errors.ToArray());

        var assigneeValidation = await ValidateTaskAssignmentForProjectAsync(companyId, dto.AssigneeUserId, dto.ProjectId, ct);
        if (!assigneeValidation.IsSuccess)
            return Result<ValidatedTaskInput>.Failure(assigneeValidation.Errors.ToArray());

        var payload = payloadValidation.Value!;
        return Result<ValidatedTaskInput>.Success(new ValidatedTaskInput(payload.Status, payload.Priority, assigneeValidation.Value));
    }

    public async Task<Result<ValidatedTaskInput>> ValidateTaskUpdateAsync(Guid companyId, int projectId, UpdateTaskDto dto, CancellationToken ct = default)
    {
        var payloadValidation = await ValidateTaskPayloadCoreAsync(companyId, projectId, dto.SprintId, dto.Status, dto.Priority, ct);
        if (!payloadValidation.IsSuccess)
            return Result<ValidatedTaskInput>.Failure(payloadValidation.Errors.ToArray());

        var assigneeValidation = await ValidateTaskAssignmentForProjectAsync(companyId, dto.AssigneeUserId, projectId, ct);
        if (!assigneeValidation.IsSuccess)
            return Result<ValidatedTaskInput>.Failure(assigneeValidation.Errors.ToArray());

        var payload = payloadValidation.Value!;
        return Result<ValidatedTaskInput>.Success(new ValidatedTaskInput(payload.Status, payload.Priority, assigneeValidation.Value));
    }

    public Task<Result<string>> ValidateTaskStatusAsync(string? status)
    {
        var normalizedStatus = NormalizeStatus(status);
        if (normalizedStatus is null)
            return Task.FromResult(Result<string>.Failure(new Error("Validation.InvalidStatus", "Status must be Open, InProgress, Blocked, or Done.")));

        return Task.FromResult(Result<string>.Success(normalizedStatus));
    }

    public async Task<Result<string?>> ValidateTaskAssignmentForProjectAsync(Guid companyId, string? assigneeUserId, int projectId, CancellationToken ct = default)
    {
        var normalizedAssigneeUserId = NormalizeAssignee(assigneeUserId);
        if (normalizedAssigneeUserId is null)
            return Result<string?>.Success(null);

        if (!await _accessControl.CanAssignToProjectAsync(companyId, normalizedAssigneeUserId, projectId, ct))
            return Result<string?>.Failure(EntityError.InvalidAssignee);

        return Result<string?>.Success(normalizedAssigneeUserId);
    }

    public async Task<Result<string?>> ValidateTaskAssignmentForTaskAsync(Guid companyId, string? assigneeUserId, int taskId, CancellationToken ct = default)
    {
        var normalizedAssigneeUserId = NormalizeAssignee(assigneeUserId);
        if (normalizedAssigneeUserId is null)
            return Result<string?>.Success(null);

        if (!await _accessControl.CanAssignToTaskAsync(companyId, normalizedAssigneeUserId, taskId, ct))
            return Result<string?>.Failure(EntityError.InvalidAssignee);

        return Result<string?>.Success(normalizedAssigneeUserId);
    }

    public async Task<Result<bool>> ValidateTaskMoveToSprintAsync(Guid companyId, int taskId, int sprintId, CancellationToken ct = default)
    {
        var taskEntity = await _taskRepository.GetByIdForCompanyAsync(taskId, companyId, ct);
        if (taskEntity is null)
            return Result<bool>.Failure(EntityError.NotFound(taskId));

        if (!taskEntity.ProjectId.HasValue)
            return Result<bool>.Failure("Task project is invalid.");

        var sprint = await _sprintRepository.GetByCompanyAsync(companyId, sprintId, ct);
        if (sprint is null)
            return Result<bool>.Failure("Sprint not found.");

        if (sprint.ProjectId != taskEntity.ProjectId)
            return Result<bool>.Failure("Task can only be moved to a sprint in the same project.");

        return Result<bool>.Success(true);
    }

    private async Task<Result<ValidatedTaskInput>> ValidateTaskPayloadCoreAsync(
        Guid companyId,
        int projectId,
        int? sprintId,
        string? status,
        string? priority,
        CancellationToken ct)
    {
        if (!await _projectRepository.CompanyOwnsProjectAsync(companyId, projectId, ct))
            return Result<ValidatedTaskInput>.Failure(new Error("Validation.InvalidProject", "Project not found."));

        var normalizedStatus = NormalizeStatus(status);
        if (normalizedStatus is null)
            return Result<ValidatedTaskInput>.Failure(new Error("Validation.InvalidStatus", "Status must be Open, InProgress, Blocked, or Done."));

        var normalizedPriority = NormalizePriority(priority);
        if (normalizedPriority is null && !string.IsNullOrWhiteSpace(priority))
            return Result<ValidatedTaskInput>.Failure(new Error("Validation.InvalidPriority", "Priority must be Low, Medium, High, or Critical."));

        if (sprintId.HasValue)
        {
            var sprint = await _sprintRepository.GetByCompanyAsync(companyId, sprintId.Value, ct);
            if (sprint is null)
                return Result<ValidatedTaskInput>.Failure(new Error("Validation.InvalidSprint", "Sprint not found."));

            if (sprint.ProjectId != projectId)
                return Result<ValidatedTaskInput>.Failure(new Error("Validation.InvalidSprintProject", "Sprint must belong to the same project as the task."));
        }

        return Result<ValidatedTaskInput>.Success(new ValidatedTaskInput(normalizedStatus, normalizedPriority, null));
    }

    private static string? NormalizeAssignee(string? assigneeUserId)
    {
        if (string.IsNullOrWhiteSpace(assigneeUserId))
            return null;

        return assigneeUserId.Trim();
    }

    private static string? NormalizeStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return null;

        var normalized = status.Trim();
        return AllowedStatuses.Contains(normalized) ? normalized : null;
    }

    private static string? NormalizePriority(string? priority)
    {
        if (string.IsNullOrWhiteSpace(priority))
            return null;

        var normalized = priority.Trim();
        return AllowedPriorities.Contains(normalized) ? normalized : null;
    }
}
