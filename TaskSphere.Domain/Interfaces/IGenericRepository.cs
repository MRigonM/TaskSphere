namespace TaskSphere.Domain.Interfaces;

public interface IGenericRepository<TEntity,TKey> where TEntity : BaseEntity<TKey>
{
    Task<TEntity?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default);
    IQueryable<TEntity> GetAll();
    Task<List<TEntity>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(TKey id, CancellationToken cancellationToken = default);
    Task<TKey> AddAsync(TEntity entity, CancellationToken cancellationToken = default);
    Task Delete(TEntity entity, CancellationToken cancellationToken = default);
    Task Update(TEntity entity, CancellationToken cancellationToken = default);
    Task<int> CountAsync(CancellationToken cancellationToken = default);
}