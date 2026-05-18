using TaskSphere.Domain.Entities;

namespace TaskSphere.Domain.Interfaces;

public interface IUnitOfWork : IReadOnlyUnitOfWork
{
    IGenericRepository<ChatMessage, int> ChatMessages { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}