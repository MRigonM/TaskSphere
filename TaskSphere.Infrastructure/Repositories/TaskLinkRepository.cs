using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Interfaces;
using TaskSphere.Infrastructure.Data;

namespace TaskSphere.Infrastructure.Repositories;

public class TaskLinkRepository : GenericRepository<TaskLink, int>, ITaskLinkRepository
{
    private readonly ApplicationDbContext _context;

    public TaskLinkRepository(ApplicationDbContext context) : base(context)
    {
        _context = context;
    }

    public IQueryable<TaskLink> GetByCompany(Guid companyId)
    {
        return _context.TaskLinks.Where(l => l.CompanyId == companyId);
    }

    public IQueryable<TaskLink> GetByTask(Guid companyId, int taskId)
    {
        return _context.TaskLinks.Where(l => l.CompanyId == companyId && l.TaskId == taskId);
    }
}
