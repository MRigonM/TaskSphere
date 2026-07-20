# Audit Logging Per Company — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Scope audit logs to the company tenant boundary and expose a `/stats` endpoint with aggregate analytics (total requests, active users, top endpoints, daily counts).

**Architecture:** `CompanyId` flows from `HttpContext.Items` (set by `[RequireCompany]`) → stamped by `AuditAttribute` → written to DB via `AuditEntry → AuditLog`. All read queries are scoped by `companyId` at the repository level. Stats are computed via EF Core aggregations on the same scoped set.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core 10, SQL Server, AutoMapper

---

## File Map

| File | Change |
|---|---|
| `TaskSphere.Domain/Entities/AuditLog.cs` | Add `CompanyId Guid?` |
| `TaskSphere.Domain/Audit/AuditEntry.cs` | Add `CompanyId Guid?` |
| `TaskSphere.Domain/DataTransferObjects/Audit/AuditLogDto.cs` | Add `AuditStatsDto`, `EndpointStatDto`, `DailyStatDto` |
| `TaskSphere/Extensions/AuditMappingExtensions.cs` | Map `CompanyId` in `ToAuditLog()` |
| `TaskSphere.Domain/Interfaces/IAuditRepository.cs` | Add `companyId` param to `GetPagedAsync`, add `GetStatsAsync` |
| `TaskSphere.Infrastructure/Repositories/AuditRepository.cs` | Implement both updated methods |
| `TaskSphere.Application/Interfaces/IAuditService.cs` | Add `companyId` param to `GetPagedAsync`, add `GetStatsAsync` |
| `TaskSphere.Application/Services/AuditService.cs` | Implement both updated methods |
| `TaskSphere/Filters/AuditAttribute.cs` | Read `CompanyId` from `HttpContext.Items`, stamp on entry |
| `TaskSphere/Controllers/AuditController.cs` | Pass `CompanyId` to service, add `GET /stats` action |
| Migration (new) | `AddCompanyIdToAuditLog` |

---

## Task 1: Domain Layer — Entity, Entry, DTOs, Mapping

**Files:**
- Modify: `TaskSphere.Domain/Entities/AuditLog.cs`
- Modify: `TaskSphere.Domain/Audit/AuditEntry.cs`
- Modify: `TaskSphere.Domain/DataTransferObjects/Audit/AuditLogDto.cs`
- Modify: `TaskSphere/Extensions/AuditMappingExtensions.cs`

- [ ] **Step 1: Add `CompanyId` to `AuditLog`**

Replace `TaskSphere.Domain/Entities/AuditLog.cs` with:

```csharp
using System.ComponentModel.DataAnnotations;

namespace TaskSphere.Domain.Entities;

public class AuditLog
{
    public int Id { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public Guid? CompanyId { get; set; }

    [MaxLength(256)]
    public string? Username { get; set; }

    [MaxLength(20)]
    public string HttpMethod { get; set; } = "";

    [MaxLength(2000)]
    public string Path { get; set; } = "";

    [MaxLength(45)]
    public string? Ip { get; set; }

    [MaxLength(500)]
    public string? UserAgent { get; set; }

    [MaxLength(500)]
    public string Action { get; set; } = "";

    public string? RequestData { get; set; }

    public int StatusCode { get; set; }
    public long DurationMs { get; set; }
}
```

- [ ] **Step 2: Add `CompanyId` to `AuditEntry`**

Replace `TaskSphere.Domain/Audit/AuditEntry.cs` with:

```csharp
namespace TaskSphere.Domain.Audit;

public sealed record AuditEntry
{
    public DateTimeOffset Timestamp { get; init; }
    public Guid? CompanyId { get; init; }
    public string? Username { get; init; }
    public string HttpMethod { get; init; } = "";
    public string Path { get; init; } = "";
    public string? Ip { get; init; }
    public string? UserAgent { get; init; }
    public string Action { get; init; } = "";
    public string? RequestData { get; init; }
    public int StatusCode { get; init; }
    public long DurationMs { get; init; }
}
```

- [ ] **Step 3: Add stats DTOs to `AuditLogDto.cs`**

Append to `TaskSphere.Domain/DataTransferObjects/Audit/AuditLogDto.cs` (keep existing records, add below `PagedResult<T>`):

```csharp
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

public record AuditStatsDto(
    int TotalRequests,
    int ActiveUsers,
    IReadOnlyList<EndpointStatDto> TopEndpoints,
    IReadOnlyList<DailyStatDto> RequestsPerDay);

public record EndpointStatDto(string Action, int Count);

public record DailyStatDto(DateOnly Date, int Count);
```

- [ ] **Step 4: Map `CompanyId` in `AuditMappingExtensions`**

Replace `TaskSphere/Extensions/AuditMappingExtensions.cs` with:

```csharp
using TaskSphere.Domain.Audit;
using TaskSphere.Domain.Entities;

namespace TaskSphere.Extensions;

public static class AuditMappingExtensions
{
    public static AuditLog ToAuditLog(this AuditEntry e) => new()
    {
        Timestamp   = e.Timestamp,
        CompanyId   = e.CompanyId,
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
```

- [ ] **Step 5: Verify build**

```bash
dotnet build TaskSphere.sln
```

Expected: Build succeeded, 0 errors.

---

## Task 2: Repository Layer

**Files:**
- Modify: `TaskSphere.Domain/Interfaces/IAuditRepository.cs`
- Modify: `TaskSphere.Infrastructure/Repositories/AuditRepository.cs`

- [ ] **Step 1: Update `IAuditRepository`**

Replace `TaskSphere.Domain/Interfaces/IAuditRepository.cs` with:

```csharp
using TaskSphere.Domain.DataTransferObjects.Audit;
using TaskSphere.Domain.Entities;

namespace TaskSphere.Domain.Interfaces;

public interface IAuditRepository
{
    Task<PagedResult<AuditLog>> GetPagedAsync(Guid companyId, AuditQueryDto query, CancellationToken ct = default);
    Task<AuditStatsDto> GetStatsAsync(Guid companyId, int days, CancellationToken ct = default);
}
```

- [ ] **Step 2: Implement updated `AuditRepository`**

Replace `TaskSphere.Infrastructure/Repositories/AuditRepository.cs` with:

```csharp
using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.DataTransferObjects.Audit;
using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Interfaces;
using TaskSphere.Infrastructure.Data;

namespace TaskSphere.Infrastructure.Repositories;

public class AuditRepository : IAuditRepository
{
    private readonly ApplicationDbContext _context;

    public AuditRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<AuditLog>> GetPagedAsync(Guid companyId, AuditQueryDto query, CancellationToken ct = default)
    {
        var q = _context.AuditLogs.AsNoTracking()
            .Where(a => a.CompanyId == companyId);

        if (!string.IsNullOrWhiteSpace(query.Username))
            q = q.Where(a => a.Username != null && a.Username.Contains(query.Username));

        if (!string.IsNullOrWhiteSpace(query.Action))
            q = q.Where(a => a.Action.Contains(query.Action));

        if (!string.IsNullOrWhiteSpace(query.HttpMethod))
            q = q.Where(a => a.HttpMethod == query.HttpMethod.ToUpper());

        var total = await q.CountAsync(ct);

        var items = await q
            .OrderByDescending(a => a.Timestamp)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return new PagedResult<AuditLog>(items, total, query.Page, query.PageSize);
    }

    public async Task<AuditStatsDto> GetStatsAsync(Guid companyId, int days, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 1, 365);
        var since = DateTimeOffset.UtcNow.AddDays(-days);

        var q = _context.AuditLogs.AsNoTracking()
            .Where(a => a.CompanyId == companyId);

        var total = await q.CountAsync(ct);

        var activeUsers = await q
            .Where(a => a.Username != null)
            .Select(a => a.Username)
            .Distinct()
            .CountAsync(ct);

        var topEndpoints = await q
            .GroupBy(a => a.Action)
            .Select(g => new EndpointStatDto(g.Key, g.Count()))
            .OrderByDescending(x => x.Count)
            .Take(5)
            .ToListAsync(ct);

        var dailyCounts = await q
            .Where(a => a.Timestamp >= since)
            .GroupBy(a => a.Timestamp.Date)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .OrderBy(x => x.Date)
            .ToListAsync(ct);

        var requestsPerDay = dailyCounts
            .Select(x => new DailyStatDto(DateOnly.FromDateTime(x.Date), x.Count))
            .ToList();

        return new AuditStatsDto(total, activeUsers, topEndpoints, requestsPerDay);
    }
}
```

- [ ] **Step 3: Verify build**

```bash
dotnet build TaskSphere.sln
```

Expected: Build succeeded, 0 errors.

---

## Task 3: Service Layer

**Files:**
- Modify: `TaskSphere.Application/Interfaces/IAuditService.cs`
- Modify: `TaskSphere.Application/Services/AuditService.cs`

- [ ] **Step 1: Update `IAuditService`**

Replace `TaskSphere.Application/Interfaces/IAuditService.cs` with:

```csharp
using TaskSphere.Domain.DataTransferObjects.Audit;

namespace TaskSphere.Application.Interfaces;

public interface IAuditService
{
    Task<PagedResult<AuditLogDto>> GetPagedAsync(Guid companyId, AuditQueryDto query, CancellationToken ct = default);
    Task<AuditStatsDto> GetStatsAsync(Guid companyId, int days, CancellationToken ct = default);
}
```

- [ ] **Step 2: Update `AuditService`**

Replace `TaskSphere.Application/Services/AuditService.cs` with:

```csharp
using AutoMapper;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.DataTransferObjects.Audit;
using TaskSphere.Domain.Interfaces;

namespace TaskSphere.Application.Services;

public class AuditService : IAuditService
{
    private readonly IReadOnlyUnitOfWork _unitOfWork;
    private readonly IMapper _mapper;

    public AuditService(IReadOnlyUnitOfWork unitOfWork, IMapper mapper)
    {
        _unitOfWork = unitOfWork;
        _mapper = mapper;
    }

    public async Task<PagedResult<AuditLogDto>> GetPagedAsync(Guid companyId, AuditQueryDto query, CancellationToken ct = default)
    {
        var paged = await _unitOfWork.AuditLogs.GetPagedAsync(companyId, query, ct);
        var dtos = _mapper.Map<List<AuditLogDto>>(paged.Items);
        return new PagedResult<AuditLogDto>(dtos, paged.Total, paged.Page, paged.PageSize);
    }

    public async Task<AuditStatsDto> GetStatsAsync(Guid companyId, int days, CancellationToken ct = default)
    {
        return await _unitOfWork.AuditLogs.GetStatsAsync(companyId, days, ct);
    }
}
```

- [ ] **Step 3: Verify build**

```bash
dotnet build TaskSphere.sln
```

Expected: Build succeeded, 0 errors.

---

## Task 4: Stamp CompanyId in AuditAttribute

**Files:**
- Modify: `TaskSphere/Filters/AuditAttribute.cs`

- [ ] **Step 1: Read `CompanyId` from `HttpContext.Items` and stamp on entry**

Replace `TaskSphere/Filters/AuditAttribute.cs` with:

```csharp
using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Filters;
using TaskSphere.Auditing;
using TaskSphere.Domain.Audit;

namespace TaskSphere.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class AuditAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var services = context.HttpContext.RequestServices;
        var queue    = services.GetRequiredService<AuditQueue>();
        var redactor = services.GetRequiredService<SensitiveDataRedactor>();

        var http = context.HttpContext;

        string? requestData = null;
        try
        {
            requestData = redactor.SerializeAndRedact(context.ActionArguments);
        }
        catch { }

        var timestamp = DateTimeOffset.UtcNow;
        var sw = Stopwatch.StartNew();
        var executed = await next();
        sw.Stop();

        try
        {
            var companyId = http.Items.TryGetValue("CompanyId", out var cid) && cid is Guid g ? g : (Guid?)null;

            var entry = new AuditEntry
            {
                Timestamp   = timestamp,
                CompanyId   = companyId,
                Username    = http.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                HttpMethod  = http.Request.Method,
                Path        = http.Request.Path,
                Ip          = http.Connection.RemoteIpAddress?.ToString(),
                UserAgent   = http.Request.Headers.UserAgent.ToString(),
                Action      = $"{context.RouteData.Values["controller"]}/{context.RouteData.Values["action"]}",
                RequestData = requestData,
                StatusCode  = executed.HttpContext.Response.StatusCode,
                DurationMs  = sw.ElapsedMilliseconds,
            };
            queue.TryWrite(entry);
        }
        catch { /* never surface errors to the caller */ }
    }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build TaskSphere.sln
```

Expected: Build succeeded, 0 errors.

---

## Task 5: Controller — Scope Paged Query + Add /stats

**Files:**
- Modify: `TaskSphere/Controllers/AuditController.cs`

- [ ] **Step 1: Update controller**

Replace `TaskSphere/Controllers/AuditController.cs` with:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.DataTransferObjects.Audit;
using TaskSphere.Domain.Enums;
using TaskSphere.Filters;

namespace TaskSphere.Controllers;

[Authorize(Roles = Roles.Company)]
[RequireCompany]
[Route("api/[controller]")]
public class AuditController : ApiBaseController
{
    private readonly IAuditService _auditService;

    public AuditController(IAuditService auditService)
    {
        _auditService = auditService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] AuditQueryDto query, CancellationToken ct)
    {
        var result = await _auditService.GetPagedAsync(CompanyId, query, ct);
        return Ok(result);
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats([FromQuery] int days = 30, CancellationToken ct = default)
    {
        var result = await _auditService.GetStatsAsync(CompanyId, days, ct);
        return Ok(result);
    }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build TaskSphere.sln
```

Expected: Build succeeded, 0 errors.

---

## Task 6: Migration

- [ ] **Step 1: Generate migration**

```bash
dotnet ef migrations add AddCompanyIdToAuditLog --project TaskSphere.Infrastructure --startup-project TaskSphere
```

Expected: Migration file `<timestamp>_AddCompanyIdToAuditLog.cs` created in `TaskSphere.Infrastructure/Migrations/`.

Verify the generated `Up()` method contains:
```csharp
migrationBuilder.AddColumn<Guid>(
    name: "CompanyId",
    table: "AuditLogs",
    type: "uniqueidentifier",
    nullable: true);
```

- [ ] **Step 2: Apply migration**

```bash
dotnet ef database update --project TaskSphere.Infrastructure --startup-project TaskSphere
```

Expected: `Done.`

- [ ] **Step 3: Manual verification via Swagger**

Start the API (`dotnet run --project TaskSphere/TaskSphere.csproj`), open `https://localhost:5001/swagger`:

1. Login as a Company-role user → copy JWT
2. Authorize in Swagger
3. Trigger `POST /api/Projects/` (decorated with `[Audit]`) to generate a log row
4. `GET /api/Audit` → verify response includes items scoped to your company, each with a non-null `companyId` field in the DB (check via SSMS/Rider: `SELECT CompanyId, Action FROM AuditLogs`)
5. `GET /api/Audit/stats?days=30` → verify response shape:
```json
{
  "totalRequests": 1,
  "activeUsers": 1,
  "topEndpoints": [{ "action": "Projects/Create", "count": 1 }],
  "requestsPerDay": [{ "date": "2026-07-21", "count": 1 }]
}
```
