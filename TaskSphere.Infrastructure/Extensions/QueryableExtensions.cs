using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.Common;

namespace TaskSphere.Infrastructure.Extensions;

public static class QueryableExtensions
{
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> source,
        PaginationParams pagination,
        CancellationToken cancellationToken = default)
    {
        var totalCount = await source.CountAsync(cancellationToken);
        var items = await source
            .Skip((pagination.Page - 1) * pagination.PageSize)
            .Take(pagination.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<T>(items, totalCount, pagination.Page, pagination.PageSize);
    }
}