using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.Task;

namespace TaskSphere.Application.Interfaces;

public interface ITaskService
{
    Task<Result<TaskDto>> GetByIdAsync(int taskId, Guid companyId, CancellationToken ct);
    Task<Result<List<TaskDto>>> GetByProjectAsync(int projectId, Guid companyId, CancellationToken ct);
    Task<Result<List<TaskDto>>> GetBacklogAsync(int projectId, Guid companyId, CancellationToken ct);
    Task<Result<List<TaskDto>>> GetBySprintAsync(int sprintId, Guid companyId, CancellationToken ct);
    Task<Result<int>> CreateAsync(CreateTaskDto dto, Guid companyId, string createdByUserId, CancellationToken ct);
    Task<Result<TaskDto>> UpdateAsync(int taskId, UpdateTaskDto dto, Guid companyId, CancellationToken ct);
    Task<Result<bool>> DeleteAsync(int taskId, Guid companyId, CancellationToken ct);
    Task<Result<bool>> MoveToSprintAsync(int taskId, int sprintId, Guid companyId, CancellationToken ct);
    Task<Result<bool>> MoveToBacklogAsync(int taskId, Guid companyId, CancellationToken ct);
    Task<Result<bool>> SetStatusAsync(int taskId, string status, Guid companyId, CancellationToken ct);
    Task<Result<bool>> AssignAsync(int taskId, string? assigneeUserId, Guid companyId, CancellationToken ct);

}