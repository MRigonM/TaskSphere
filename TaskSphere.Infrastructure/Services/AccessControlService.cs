using Microsoft.EntityFrameworkCore;
using TaskSphere.Application.Interfaces;
using TaskSphere.Infrastructure.Data;
using TaskEntity = TaskSphere.Domain.Entities.Task;

namespace TaskSphere.Infrastructure.Services;

public class AccessControlService : IAccessControlService
{
    private readonly ApplicationDbContext _db;

    public AccessControlService(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<bool> CanAccessProjectAsync(Guid companyId, string userId, int projectId, CancellationToken ct = default)
    {
        return _db.Members.AnyAsync(
            m => m.Project.CompanyId == companyId
                 && m.ProjectId == projectId
                 && m.UserId == userId,
            ct);
    }

    public Task<bool> CanAccessSprintAsync(Guid companyId, string userId, int sprintId, CancellationToken ct = default)
    {
        return _db.Sprints
            .Where(s => s.Id == sprintId && s.CompanyId == companyId && s.ProjectId.HasValue)
            .AnyAsync(s => s.Project!.Members.Any(m => m.UserId == userId), ct);
    }

    public Task<bool> CanAccessTaskAsync(Guid companyId, string userId, int taskId, CancellationToken ct = default)
    {
        return _db.Set<TaskEntity>()
            .Where(t => t.Id == taskId && t.CompanyId == companyId && t.ProjectId.HasValue)
            .AnyAsync(t => t.Project!.Members.Any(m => m.UserId == userId), ct);
    }
}
