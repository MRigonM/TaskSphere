# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

### Backend (.NET 9)
```bash
dotnet build
dotnet run --project TaskSphere/TaskSphere.csproj

# EF Core migrations
dotnet ef migrations add <Name> --project TaskSphere.Infrastructure --startup-project TaskSphere
dotnet ef database update --project TaskSphere.Infrastructure --startup-project TaskSphere
```

### Frontend (Angular 21)
```bash
cd client
npm install
npm start        # dev server → http://localhost:4200
npm run build
npm test         # Vitest
```

The API runs at `https://localhost:5001`. The Angular env hardcodes this in `client/src/environments/environment.ts`.

JWT secrets are stored via .NET User Secrets (not in `appsettings.json`).

## Architecture

Clean Architecture with four layers:

```
Angular SPA  →  ASP.NET Core Web API  →  Application Layer  →  Domain Layer
                                                 ↑
                                        Infrastructure Layer (EF Core / SQL Server)
```

- **TaskSphere/** — Web API: controllers, filters, DI registrations, JWT config
- **TaskSphere.Application/** — services, interfaces, FluentValidation validators, AutoMapper profiles
- **TaskSphere.Domain/** — entities, DTOs, enums, `Result<T>` pattern
- **TaskSphere.Infrastructure/** — EF Core `ApplicationDbContext`, repositories, migrations, `AccessControlService`



## Key Patterns

### Multi-Tenancy
`Company` is the top-level tenant boundary. The `[RequireCompany]` action filter extracts `companyId` from the JWT and puts it in `HttpContext.Items["CompanyId"]`. `ApiBaseController.CompanyId` exposes it. Every query is scoped to `CompanyId`.

### Two Roles
- `"Company"` — org admin; can manage all resources within the company
- `"User"` — member; can only access projects they are explicitly a `Member` of

### Access Control
`IAccessControlService` (injected into Application services) enforces project membership for `User`-role callers before any data operation. Company admins bypass it.

### Result Pattern
All Application services return `Result<T>` (see `TaskSphere.Domain/Common/Result.cs`). `ApiBaseController.FromResult<T>()` maps it to HTTP: `200`, `403` (code `"Auth.Forbidden"`), or `400`.

### Business Validation
A second in-process validation tier (`ITaskValidationService`, `ISprintValidationService`) enforces business rules (e.g., assignee must be a project member, only one active sprint per project) and returns `Result<T>`.

### Soft Deletes
`BaseEntity<T>` provides `IsDeleted`/`DeletedAt`. EF Core global query filters exclude soft-deleted records automatically.

### Angular Auth
`AuthStoreService` stores auth state as Angular signals, backed by `localStorage` key `tasksphere_auth` (contains token, name, role, companyId, userId). `authInterceptor` attaches the Bearer token to every HTTP request. Three route guards: `guestGuard`, `companyGuard`, `companyMemberGuard`.

## Important Files

| File | Purpose |
|---|---|
| `TaskSphere/Program.cs` | App entry point and middleware pipeline |
| `TaskSphere/Extensions/ApplicationServices.cs` | All DI registrations |
| `TaskSphere/Extensions/AuthenticationExtensions.cs` | JWT configuration |
| `TaskSphere/Filters/RequireCompanyAttribute.cs` | Multi-tenancy action filter |
| `TaskSphere.Infrastructure/Services/AccessControlService.cs` | Membership-based access enforcement |
| `TaskSphere.Infrastructure/Data/ApplicationDbContext.cs` | EF model, relationships, and global query filters |
| `TaskSphere.Domain/Common/Result.cs` | Result/error discriminated union |
| `client/src/app/core/services/auth-store.service.ts` | Client-side auth state (signals + localStorage) |
| `client/src/app/core/interceptors/auth.interceptor.ts` | JWT attachment interceptor |
| `client/src/app/app.routes.ts` | All Angular routes with guards |
