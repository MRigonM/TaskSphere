using TaskEntity = TaskSphere.Domain.Entities.Task;

namespace TaskSphere.Domain.Interfaces;

public interface ITaskRepository : IGenericRepository<TaskEntity, int>
{
    Task<TaskEntity?> GetByIdForCompanyAsync(int taskId, Guid companyId, CancellationToken ct);
    Task<List<TaskEntity>> GetByProjectAsync(int projectId, Guid companyId, CancellationToken ct);
    Task<List<TaskEntity>> GetBacklogAsync(int projectId, Guid companyId, CancellationToken ct);
    Task<List<TaskEntity>> GetBySprintAsync(int sprintId, Guid companyId, CancellationToken ct);
    Task MoveToSprintAsync(int taskId, int sprintId, Guid companyId, CancellationToken ct);
    Task MoveToBacklogAsync(int taskId, Guid companyId, CancellationToken ct);
    Task SetStatusAsync(int taskId, Guid companyId, string status, CancellationToken ct);
    Task AssignAsync(int taskId, Guid companyId, string? assigneeUserId, CancellationToken ct);
}