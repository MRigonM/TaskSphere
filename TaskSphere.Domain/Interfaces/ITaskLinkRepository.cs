using TaskSphere.Domain.Entities;

namespace TaskSphere.Domain.Interfaces;

public interface ITaskLinkRepository : IGenericRepository<TaskLink, int>
{
    IQueryable<TaskLink> GetByCompany(Guid companyId);
    IQueryable<TaskLink> GetByTask(Guid companyId, int taskId);
}
