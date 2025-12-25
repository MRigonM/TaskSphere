using TaskSphere.Domain.Entities;

namespace TaskSphere.Domain.Interfaces;

public interface IMemberRepository : IGenericRepository<Member, int>
{
    Task<Member?> GetByProjectAndUserIncludingDeletedAsync(int projectId, string userId, CancellationToken ct);
}