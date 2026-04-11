using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.Task;

namespace TaskSphere.Application.Interfaces;

public interface ITaskValidationService
{
    Task<Result<ValidatedTaskInput>> ValidateTaskCreateAsync(Guid companyId, CreateTaskDto dto, CancellationToken ct = default);
    Task<Result<ValidatedTaskInput>> ValidateTaskUpdateAsync(Guid companyId, int projectId, UpdateTaskDto dto, CancellationToken ct = default);
    Task<Result<string>> ValidateTaskStatusAsync(string? status);
    Task<Result<string?>> ValidateTaskAssignmentForProjectAsync(Guid companyId, string? assigneeUserId, int projectId, CancellationToken ct = default);
    Task<Result<string?>> ValidateTaskAssignmentForTaskAsync(Guid companyId, string? assigneeUserId, int taskId, CancellationToken ct = default);
    Task<Result<bool>> ValidateTaskMoveToSprintAsync(Guid companyId, int taskId, int sprintId, CancellationToken ct = default);
}

public record ValidatedTaskInput(string Status, string? Priority, string? AssigneeUserId);
