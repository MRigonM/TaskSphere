# Audit Logging Per Company — Design Spec

**Date:** 2026-07-20
**Project:** TaskSphere
**Status:** Approved

---

## Goal

Scope audit logs to the company tenant boundary so each company only sees their own logs, and expose aggregate stats (active users, busiest endpoints, daily request counts) via a dedicated endpoint.

---

## Scope Decision

Only endpoints decorated with `[Audit]` are audited. All such endpoints sit behind `[RequireCompany]`, so a `CompanyId` is always available. Auth endpoints (login, register) are excluded — they have no company context and should not be audited.

---

## Data Layer

### AuditLog entity
Add `CompanyId Guid?` column. Nullable to stay schema-flexible, but in practice always populated for audited endpoints.

```csharp
public Guid? CompanyId { get; set; }
```

New EF Core migration required.

### AuditEntry (in-memory)
Add `CompanyId Guid?` to the Channel payload so the value flows:

```
AuditAttribute → AuditQueue (Channel) → AuditWriterService → DB
```

No service layer touched.

### AuditAttribute
Read `CompanyId` from `HttpContext.Items["CompanyId"]` (set by `[RequireCompany]`) and stamp it on the entry:

```csharp
var companyId = http.Items["CompanyId"] as Guid?;
```

If missing (upstream bug), entry is written with `CompanyId = null` — auditing never surfaces errors to the caller.

### Filter ordering guarantee
`[RequireCompany]` is a controller-level attribute; `[Audit]` is action-level. ASP.NET Core runs controller-level filters first, so `CompanyId` is always in `HttpContext.Items` before `AuditAttribute` executes.

---

## Query Layer

### AuditRepository.GetPagedAsync
Add `companyId` parameter. Always appends:

```csharp
q = q.Where(a => a.CompanyId == companyId);
```

No caller can accidentally fetch another company's logs.

### AuditRepository.GetStatsAsync(Guid companyId, int days)
Single aggregation pass returning:
- Total request count
- Unique active user count
- Top 5 busiest endpoints (by request count)
- Requests per day for the last `days` days

`days` is clamped to 1–365 to prevent oversized aggregations.

Implemented via EF Core `GroupBy` — no raw SQL.

---

## DTOs

### AuditStatsDto (new)
```csharp
public record AuditStatsDto(
    int TotalRequests,
    int ActiveUsers,
    IReadOnlyList<EndpointStatDto> TopEndpoints,
    IReadOnlyList<DailyStatDto> RequestsPerDay);

public record EndpointStatDto(string Action, int Count);
public record DailyStatDto(DateOnly Date, int Count);
```

---

## Service Layer

`IAuditService` and `AuditService` get:

```csharp
Task<AuditStatsDto> GetStatsAsync(Guid companyId, int days, CancellationToken ct = default);
```

Delegates directly to `IAuditRepository.GetStatsAsync`.

---

## API

### Existing endpoint (updated)
```
GET /api/Audit?username=&httpMethod=&action=&page=1&pageSize=50
```
Now scoped to caller's `CompanyId` — extracted from JWT via `ApiBaseController.CompanyId`.

### New endpoint
```
GET /api/Audit/stats?days=30
```
Returns `AuditStatsDto`. `days` defaults to 30, clamped server-side to 1–365. Company-admin only (`Roles.Company`).

---

## Files Changed

| File | Change |
|---|---|
| `TaskSphere.Domain/Entities/AuditLog.cs` | Add `CompanyId Guid?` |
| `TaskSphere.Domain/Audit/AuditEntry.cs` | Add `CompanyId Guid?` |
| `TaskSphere.Domain/DataTransferObjects/Audit/AuditLogDto.cs` | Add `AuditStatsDto`, `EndpointStatDto`, `DailyStatDto` |
| `TaskSphere.Domain/Interfaces/IAuditRepository.cs` | Add `GetStatsAsync` |
| `TaskSphere.Infrastructure/Data/ApplicationDbContext.cs` | Migration snapshot update |
| `TaskSphere.Infrastructure/Repositories/AuditRepository.cs` | Scope by companyId, add `GetStatsAsync` |
| `TaskSphere.Application/Interfaces/IAuditService.cs` | Add `GetStatsAsync` |
| `TaskSphere.Application/Services/AuditService.cs` | Implement `GetStatsAsync` |
| `TaskSphere/Filters/AuditAttribute.cs` | Read and stamp `CompanyId` |
| `TaskSphere/Controllers/AuditController.cs` | Pass companyId to service, add `/stats` action |
| `Migration` (new file) | `AddCompanyIdToAuditLog` |
