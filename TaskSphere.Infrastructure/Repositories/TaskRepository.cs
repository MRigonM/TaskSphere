using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.Interfaces;
using TaskSphere.Infrastructure.Data;
using TaskEntity = TaskSphere.Domain.Entities.Task;

namespace TaskSphere.Infrastructure.Repositories;

public class TaskRepository : GenericRepository<TaskEntity, int>, ITaskRepository
{
    private readonly ApplicationDbContext _db;

    public TaskRepository(ApplicationDbContext db) : base(db)
    {
        _db = db;
    }

    public async Task<TaskEntity?> GetByIdForCompanyAsync(int taskId, Guid companyId, CancellationToken ct)
    {
        return await _db.Set<TaskEntity>()
            .FirstOrDefaultAsync(t => t.Id == taskId && t.CompanyId == companyId, ct);
    }

    public async Task<List<TaskEntity>> GetByProjectAsync(int projectId, Guid companyId, CancellationToken ct)
    {
        return await _db.Set<TaskEntity>()
            .AsNoTracking()
            .Where(t => t.CompanyId == companyId && t.ProjectId == projectId)
            .OrderByDescending(t => t.CreatedAtUtc)
            .ToListAsync(ct);
    }

    public async Task<List<TaskEntity>> GetBacklogAsync(int projectId, Guid companyId, CancellationToken ct)
    {
        return await _db.Set<TaskEntity>()
            .AsNoTracking()
            .Where(t =>
                t.CompanyId == companyId &&
                t.ProjectId == projectId &&
                t.SprintId == null)
            .OrderByDescending(t => t.CreatedAtUtc)
            .ToListAsync(ct);
    }

    public async Task<List<TaskEntity>> GetBySprintAsync(int sprintId, Guid companyId, CancellationToken ct)
    {
        return await _db.Set<TaskEntity>()
            .AsNoTracking()
            .Where(t => t.CompanyId == companyId && t.SprintId == sprintId)
            .OrderByDescending(t => t.CreatedAtUtc)
            .ToListAsync(ct);
    }

    public async Task MoveToSprintAsync(int taskId, int sprintId, Guid companyId, CancellationToken ct)
    {
        var taskEntity = await _db.Set<TaskEntity>()
            .FirstOrDefaultAsync(t => t.Id == taskId && t.CompanyId == companyId, ct);

        if (taskEntity is null) throw new KeyNotFoundException("Task not found.");

        taskEntity.SprintId = sprintId;
    }

    public async Task MoveToBacklogAsync(int taskId, Guid companyId, CancellationToken ct)
    {
        var taskEntity = await _db.Set<TaskEntity>()
            .FirstOrDefaultAsync(t => t.Id == taskId && t.CompanyId == companyId, ct);

        if (taskEntity is null) throw new KeyNotFoundException("Task not found.");

        taskEntity.SprintId = null;
    }

    public async Task SetStatusAsync(int taskId, Guid companyId, string status, CancellationToken ct)
    {
        var taskEntity = await _db.Set<TaskEntity>()
            .FirstOrDefaultAsync(t => t.Id == taskId && t.CompanyId == companyId, ct);

        if (taskEntity is null) throw new KeyNotFoundException("Task not found.");

        taskEntity.Status = status;
    }

    public async Task AssignAsync(int taskId, Guid companyId, string? assigneeUserId, CancellationToken ct)
    {
        var taskEntity = await _db.Set<TaskEntity>()
            .FirstOrDefaultAsync(t => t.Id == taskId && t.CompanyId == companyId, ct);

        if (taskEntity is null) throw new KeyNotFoundException("Task not found.");

        taskEntity.AssigneeUserId = assigneeUserId;
    }
}