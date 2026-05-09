# TaskSphere

TaskSphere is a project management platform built for teams. You create a company, invite members, spin up projects, plan sprints, and track tasks — all in one place. Teams can communicate through a per-project real-time chat with image sharing, so context stays close to the work. Access is role-based: company admins manage everything, while regular members only see the projects they're part of. Built with ASP.NET Core 9 on the backend and Angular 21 on the frontend.

---

## Features

- **Projects & Sprints** — Create projects, plan sprints, manage backlog
- **Task Management** — Assign tasks to sprint members, track status
- **Real-time Team Chat** — Per-project chat with image sharing (paste or upload)
- **Multi-tenancy** — Company-scoped isolation; every resource belongs to a tenant
- **Role-based Access Control** — Company admin vs. project member roles
- **JWT Authentication** — Secure auth with ASP.NET Identity

---

## Tech Stack

| Layer | Technology |
|---|---|
| Frontend | Angular 21, TypeScript, Tailwind CSS |
| Real-time | SignalR (`@microsoft/signalr` 10.0) |
| Backend | ASP.NET Core 9, C# |
| ORM | Entity Framework Core 9 + SQL Server |
| Auth | JWT Bearer + ASP.NET Identity |
| Validation | FluentValidation 11 |
| Mapping | AutoMapper 13 |
| Testing | Vitest (frontend) |

---

## Architecture

Clean Architecture — four layers:

```
Angular SPA  →  ASP.NET Core Web API  →  Application Layer  →  Domain Layer
                                                 ↑
                                        Infrastructure Layer (EF Core / SQL Server)
```

```
TaskSphere/                  # Web API — controllers, middleware, DI, JWT config
TaskSphere.Application/      # Services, FluentValidation validators, AutoMapper profiles
TaskSphere.Domain/           # Entities, DTOs, enums, Result<T> pattern
TaskSphere.Infrastructure/   # EF Core DbContext, repositories, migrations, AccessControlService
client/                      # Angular SPA
```

Key patterns: `Result<T>` error handling, soft deletes, generic repository + Unit of Work, membership-based access control.

---

## Getting Started

### Prerequisites

- .NET 9 SDK
- SQL Server
- Node.js 20+

### Backend

```bash
# Configure JWT secret (stored in User Secrets, not appsettings.json)
dotnet user-secrets set "Jwt:Key" "your-secret-key" --project TaskSphere

# Apply database migrations
dotnet ef database update --project TaskSphere.Infrastructure --startup-project TaskSphere

# Run the API (https://localhost:5001)
dotnet run --project TaskSphere/TaskSphere.csproj
```

Swagger UI is available at `https://localhost:5001/swagger`.

### Frontend

```bash
cd client
npm install
npm start       # http://localhost:4200
```

The Angular dev environment hardcodes the API URL to `https://localhost:5001`.

---

## Database Migrations

```bash
# Add a new migration
dotnet ef migrations add <MigrationName> --project TaskSphere.Infrastructure --startup-project TaskSphere

# Apply migrations
dotnet ef database update --project TaskSphere.Infrastructure --startup-project TaskSphere
```

---

## Access Control

Two roles exist within a company tenant:

| Role | Access |
|---|---|
| `Company` | Admin — manages all projects and members within the company |
| `User` | Member — can only access projects they are explicitly added to |

Project membership is enforced server-side via `IAccessControlService` on every data operation.
