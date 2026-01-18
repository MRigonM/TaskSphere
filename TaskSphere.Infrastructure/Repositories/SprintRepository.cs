using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.DataTransferObjects.Sprint;
using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Enums;
using TaskSphere.Domain.Interfaces;
using TaskSphere.Infrastructure.Data;
using Task = System.Threading.Tasks.Task;
using TaskEntity = TaskSphere.Domain.Entities.Task;

namespace TaskSphere.Infrastructure.Repositories;

public class SprintRepository : GenericRepository<Sprint, int>, ISprintRepository
{
    private readonly ApplicationDbContext _context;

    public SprintRepository(ApplicationDbContext context) : base(context)
    {
        _context = context;
    }

    public async Task<List<Sprint>> GetByProjectAsync(int projectId, Guid companyId, bool includeArchived, CancellationToken ct)
    {
        var q = _context.Set<Sprint>()
            .AsNoTracking()
            .Where(s => s.CompanyId == companyId && s.ProjectId == projectId);

        if (!includeArchived)
            q = q.Where(s => !s.IsArchived);

        return await q
            .OrderByDescending(s => s.IsActive)
            .ThenByDescending(s => s.StartDate)
            .ToListAsync(ct);
    }

    public async Task<Sprint?> GetWithProjectAsync(int sprintId, Guid companyId, CancellationToken ct)
    {
        return await _context.Set<Sprint>()
            .AsNoTracking()
            .Include(s => s.Project)
            .FirstOrDefaultAsync(s => s.Id == sprintId && s.CompanyId == companyId, ct);
    }

    public async Task<Sprint?> GetActiveForProjectAsync(int projectId, Guid companyId, CancellationToken ct)
    {
        return await _context.Set<Sprint>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.CompanyId == companyId && s.ProjectId == projectId && s.IsActive, ct);
    }

    public async Task SetActiveAsync(int sprintId, Guid companyId, bool isActive, CancellationToken ct)
    {
        var sprint = await _context.Set<Sprint>()
            .FirstOrDefaultAsync(s => s.Id == sprintId && s.CompanyId == companyId, ct);

        if (sprint == null) return;

        if (isActive && sprint.ProjectId.HasValue)
        {
            await _context.Set<Sprint>()
                .Where(s => s.CompanyId == companyId
                            && s.ProjectId == sprint.ProjectId
                            && s.Id != sprintId
                            && s.IsActive)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.IsActive, false), ct);
        }

        sprint.IsActive = isActive;
    }

    public async Task<Sprint> CreateAndActivateAsync(
        Sprint sprint,
        Guid companyId,
        bool deactivateOtherSprintsInProject,
        CancellationToken ct)
    {
        sprint.CompanyId = companyId;

        if (sprint.ProjectId.HasValue && deactivateOtherSprintsInProject)
        {
            await _context.Set<Sprint>()
                .Where(s => s.CompanyId == companyId
                            && s.ProjectId == sprint.ProjectId
                            && s.IsActive)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.IsActive, false), ct);
        }

        sprint.IsActive = true;

        await _context.Set<Sprint>().AddAsync(sprint, ct);
        return sprint;
    }

    public async Task ActivateExistingAndCarryOverAsync(
        int sprintId,
        Guid companyId,
        bool carryOverUnfinished,
        CancellationToken ct)
    {
        var sprint = await _context.Set<Sprint>()
            .FirstOrDefaultAsync(s => s.Id == sprintId && s.CompanyId == companyId, ct);

        if (sprint == null) return;
        if (!sprint.ProjectId.HasValue) return;

        var prevActiveSprintId = await _context.Set<Sprint>()
            .AsNoTracking()
            .Where(s => s.CompanyId == companyId
                        && s.ProjectId == sprint.ProjectId
                        && s.Id != sprintId
                        && s.IsActive)
            .Select(s => (int?)s.Id)
            .FirstOrDefaultAsync(ct);

        await _context.Set<Sprint>()
            .Where(s => s.CompanyId == companyId
                        && s.ProjectId == sprint.ProjectId
                        && s.Id != sprintId
                        && s.IsActive)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.IsActive, false), ct);

        sprint.IsActive = true;

        if (!carryOverUnfinished)
            return;

        if (prevActiveSprintId.HasValue)
        {
            await _context.Set<TaskEntity>()
                .Where(t => t.CompanyId == companyId
                            && t.ProjectId == sprint.ProjectId
                            && t.SprintId == prevActiveSprintId.Value
                            && t.Status != TaskStatuses.Done)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.SprintId, sprintId), ct);
        }
    }

    public async Task<SprintBoardDto?> GetBoardAsync(int sprintId, Guid companyId, CancellationToken ct)
    {
        var sprint = await _context.Set<Sprint>()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sprintId && s.CompanyId == companyId, ct);

        if (sprint == null) return null;

        var tasks = await _context.Set<TaskEntity>()
            .AsNoTracking()
            .Where(t => t.CompanyId == companyId && t.SprintId == sprintId)
            .OrderByDescending(t => t.CreatedAtUtc)
            .ToListAsync(ct);

        return new SprintBoardDto
        {
            SprintId = sprint.Id,
            SprintName = sprint.Name,
            ProjectId = sprint.ProjectId,
            
            Low = tasks.Where(t => t.Priority == TaskPriority.Low).ToList(),
            Medium = tasks.Where(t => t.Priority == TaskPriority.Medium).ToList(),
            High = tasks.Where(t => t.Priority == TaskPriority.High).ToList(),
            Critical = tasks.Where(t => t.Priority == TaskPriority.Critical).ToList(),

            Open = tasks.Where(t => t.Status == TaskStatuses.Open).ToList(),
            InProgress = tasks.Where(t => t.Status == TaskStatuses.InProgress).ToList(),
            Blocked = tasks.Where(t => t.Status == TaskStatuses.Blocked).ToList(),
            Done = tasks.Where(t => t.Status == TaskStatuses.Done).ToList()
        };
    }

    public async Task MoveTaskToActiveAsync(int taskId, int sprintId, Guid companyId, CancellationToken ct)
    {
        var sprintExists = await _context.Set<Sprint>()
            .AnyAsync(s => s.Id == sprintId && s.CompanyId == companyId, ct);

        if (!sprintExists) return;

        await _context.Set<TaskEntity>()
            .Where(t => t.Id == taskId && t.CompanyId == companyId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.SprintId, sprintId)
                .SetProperty(x => x.Status, TaskStatuses.InProgress), ct);
    }
    public Task<Sprint?> GetByCompanyAsync(Guid companyId, int sprintId, CancellationToken ct) =>
        _context.Set<Sprint>().FirstOrDefaultAsync(s => s.Id == sprintId && s.CompanyId == companyId, ct);

}