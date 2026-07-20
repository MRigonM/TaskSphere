using TaskSphere.Domain.Audit;
using TaskSphere.Domain.Entities;

namespace TaskSphere.Extensions;

public static class AuditMappingExtensions
{
    public static AuditLog ToAuditLog(this AuditEntry e) => new()
    {
        Timestamp   = e.Timestamp,
        Username    = e.Username,
        HttpMethod  = e.HttpMethod,
        Path        = e.Path,
        Ip          = e.Ip,
        UserAgent   = e.UserAgent,
        Action      = e.Action,
        RequestData = e.RequestData,
        StatusCode  = e.StatusCode,
        DurationMs  = e.DurationMs,
    };
}
