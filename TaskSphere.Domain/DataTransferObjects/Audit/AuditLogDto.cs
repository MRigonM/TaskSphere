namespace TaskSphere.Domain.DataTransferObjects.Audit;

public record AuditLogDto(
    int Id,
    DateTimeOffset Timestamp,
    string? Username,
    string? HttpMethod,
    string Path,
    string? Ip,
    string Action,
    string? RequestData,
    int StatusCode,
    long DurationMs);

public record AuditQueryDto(
    string? Username = null,
    string? HttpMethod = null,
    string? Action = null,
    int Page = 1,
    int PageSize = 50);

public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
