using TaskSphere.Domain.Entities;

namespace TaskSphere.Domain.Interfaces;

public interface IUnitOfWork : IReadOnlyUnitOfWork
{
    IGenericRepository<ChatMessage, int> ChatMessages { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Detaches everything still pending, abandoning the writes of an operation that failed.
    /// <para>
    /// EF Core keeps an entity tracked as Modified after a rejected SaveChangesAsync, so a
    /// loop that saves per item and swallows the exception will re-send the bad write on the
    /// NEXT item's save and take that one down too. Saving per item is not enough on its own;
    /// the failed unit has to be discarded as well.
    /// </para>
    /// </summary>
    void DiscardPendingChanges();
}