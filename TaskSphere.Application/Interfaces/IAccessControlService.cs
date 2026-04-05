namespace TaskSphere.Application.Interfaces;

public interface IAccessControlService
{
    Task<bool> CanAccessProjectAsync(Guid companyId, string userId, int projectId, CancellationToken ct = default);
    Task<bool> CanAccessSprintAsync(Guid companyId, string userId, int sprintId, CancellationToken ct = default);
    Task<bool> CanAccessTaskAsync(Guid companyId, string userId, int taskId, CancellationToken ct = default);
}
