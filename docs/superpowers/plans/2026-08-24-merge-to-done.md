# Merge → Done Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When a pull request whose head branch names a task key is merged, move that task to `Done` — once, accountably, and only where the project has opted in.

**Architecture:** A new `IMergeTransitionService` runs inside the existing manual activity sync, after the pull-request upsert. It detects "this merge is new to us" through an applied-once marker column on `GitHubPullRequest` rather than by comparing states, because the sync overwrites `State` on every pass. Task resolution goes through a shared unit extracted from `GitHubTaskLinkResolver`, so the repository↔project authorization boundary exists exactly once.

**Tech Stack:** .NET 10 (`net10.0`), EF Core 9.0.10, SQL Server / LocalDB, xUnit integration tests against a real migrated database, Angular 21 + Vitest on the client.

**Spec:** `docs/superpowers/specs/2026-08-24-merge-to-done-design.md`

## Global Constraints

- **Target framework is `net10.0`.** The repo `CLAUDE.md` says .NET 9; it is wrong. Do not "fix" code to match it.
- **Status values come from `TaskSphere.Domain.Enums.TaskStatuses`** — `Open`, `InProgress`, `Blocked`, `Done`. Never write the literal string.
- **The entity `Task` collides with `System.Threading.Tasks.Task`.** Every file touching both needs a `using TaskEntity = TaskSphere.Domain.Entities.Task;` alias (and often `using SystemTask = System.Threading.Tasks;`). Follow the existing files exactly.
- **The marker is stamped unconditionally** — on skip, on toggle-off, on no-keys-found, on success. The only case that does not stamp is an exception.
- **Partial-failure unit is one pull request.** `SaveChangesAsync` per pull request, never once at the end.
- **`Project.Key` must never become editable.** The settings DTO carries `AutoDoneOnMerge` and nothing else.
- **Infrastructure must not acquire an HTTP dependency.** The actor username is passed as a parameter, never via `IHttpContextAccessor`.
- **Test fixtures must seed different identity values per table.** A freshly migrated database gives every table the same identity seeds, so a lookup passing the wrong entity's id resolves correctly by accident. Use decoy rows.
- **Every integration test class gets its own LocalDB database name**, following the existing pattern.
- **Run the backend suite with the app stopped.** `dotnet test` fails against a running app holding the database.

---

## File Structure

| File | Responsibility |
|---|---|
| `TaskSphere.Infrastructure/Services/TaskKeyResolutionMap.cs` | **New.** The company's key→task resolution snapshot and the authorization rule. Extracted from `GitHubTaskLinkResolver`. |
| `TaskSphere.Infrastructure/Services/GitHubTaskLinkResolver.cs` | **Modify.** Delegates resolution to the map; behaviour unchanged. |
| `TaskSphere.Domain/Entities/GitHubPullRequest.cs` | **Modify.** Gains `MergeTransitionAppliedAtUtc`. |
| `TaskSphere.Domain/Entities/Project.cs` | **Modify.** Gains `AutoDoneOnMerge`. |
| `TaskSphere.Infrastructure/Migrations/*` | **New.** One migration adding both columns. |
| `TaskSphere.Application/Interfaces/IMergeTransitionService.cs` | **New.** Interface + `MergeTransitionResult`. |
| `TaskSphere.Infrastructure/Services/MergeTransitionService.cs` | **New.** The algorithm. |
| `TaskSphere.Infrastructure/Services/GitHubActivitySyncService.cs` | **Modify.** Calls the transition service; preserves the marker on upsert; carries the actor. |
| `TaskSphere.Domain/DataTransferObjects/GitHub/GitHubActivityDtos.cs` | **Modify.** `SyncActivityResultDto` gains `TasksTransitioned`. |
| `TaskSphere.Domain/DataTransferObjects/Project/ProjectDto.cs` | **Modify.** `ProjectDto` gains `AutoDoneOnMerge`; new `UpdateProjectSettingsDto`. |
| `TaskSphere/Controllers/ProjectsController.cs` | **Modify.** New `PATCH {projectId}/settings`. |
| `TaskSphere/Controllers/GitHubController.cs` | **Modify.** Passes the actor username. |
| `TaskSphere/Extensions/ApplicationServices.cs` | **Modify.** DI registration. |
| `client/src/app/core/models/github-activity.models.ts` | **Modify.** `tasksTransitioned`. |
| `client/src/app/core/models/projects.models.ts` | **Modify.** `autoDoneOnMerge`. |
| `client/src/app/company-dashboard/projects/*` | **Modify.** The toggle control. |

---

### Task 1: Extract the shared task resolution

**Files:**
- Create: `TaskSphere.Infrastructure/Services/TaskKeyResolutionMap.cs`
- Modify: `TaskSphere.Infrastructure/Services/GitHubTaskLinkResolver.cs`
- Test: `TaskSphere.Tests/Integration/TaskKeyResolutionMapTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `TaskSphere.Infrastructure.Services.TaskKeyResolutionMap` with
  `static Task<TaskKeyResolutionMap> BuildAsync(IUnitOfWork unitOfWork, Guid companyId, CancellationToken cancellationToken)`
  and `int? Resolve(TaskKey key, int gitHubRepositoryId)`.

This is a **behaviour-preserving refactor**. `GitHubTaskLinkResolverTests` must pass unchanged at the end of it. Step 2 of the resolution is the authorization boundary — without it, push access to any repository under the installation is enough to change any project's task status.

- [ ] **Step 1: Write the failing test**

Create `TaskSphere.Tests/Integration/TaskKeyResolutionMapTests.cs`. Fixture mirrors `GitHubTaskLinkResolverTests` but with **deliberately divergent identity seeds** (see Global Constraints).

```csharp
using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.Enums;
using TaskSphere.Infrastructure.Data;
using TaskSphere.Infrastructure.Repositories;
using TaskSphere.Infrastructure.Services;

using Company = TaskSphere.Domain.Entities.Company;
using GitHubInstallation = TaskSphere.Domain.Entities.GitHubInstallation;
using GitHubRepository = TaskSphere.Domain.Entities.GitHubRepository;
using Project = TaskSphere.Domain.Entities.Project;
using ProjectRepositoryLink = TaskSphere.Domain.Entities.ProjectRepositoryLink;
using TaskEntity = TaskSphere.Domain.Entities.Task;
using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

public class TaskKeyResolutionMapTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereResolutionMapTests;Trusted_Connection=True;TrustServerCertificate=True";

    private Guid _companyId;
    private int _tsProjectId;
    private int _bsProjectId;
    private int _apiRepositoryId;
    private int _webRepositoryId;
    private int _ts42TaskId;

    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }

    private static UnitOfWork NewUnitOfWork(ApplicationDbContext db) => new(db);

    public async SystemTask.Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();

        var company = new Company { Name = "Map Co" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        _companyId = company.Id;

        // DECOY ROWS: burn identity values so Projects, Repositories and Tasks do not share
        // seeds. Without these, a lookup passing a repository id where a project id belongs
        // resolves correctly by accident.
        var decoyProjects = new[]
        {
            new Project { Name = "Decoy A", Key = "DECA", CompanyId = _companyId },
            new Project { Name = "Decoy B", Key = "DECB", CompanyId = _companyId },
            new Project { Name = "Decoy C", Key = "DECC", CompanyId = _companyId },
        };
        db.Projects.AddRange(decoyProjects);
        await db.SaveChangesAsync();

        var ts = new Project { Name = "TaskSphere", Key = "TS", CompanyId = _companyId };
        var bs = new Project { Name = "BaseClean", Key = "BS", CompanyId = _companyId };
        db.Projects.AddRange(ts, bs);
        await db.SaveChangesAsync();
        _tsProjectId = ts.Id;
        _bsProjectId = bs.Id;

        var installation = new GitHubInstallation
        {
            InstallationId = 9301,
            CompanyId = _companyId,
            AccountLogin = "rigon-org",
            AccountType = "Organization",
            RepositorySelection = RepositorySelection.All,
        };
        db.GitHubInstallations.Add(installation);
        await db.SaveChangesAsync();

        var api = new GitHubRepository
        {
            RepositoryId = 9401,
            GitHubInstallationId = installation.Id,
            CompanyId = _companyId,
            FullName = "rigon-org/api",
            DefaultBranch = "main",
        };
        var web = new GitHubRepository
        {
            RepositoryId = 9402,
            GitHubInstallationId = installation.Id,
            CompanyId = _companyId,
            FullName = "rigon-org/web",
            DefaultBranch = "main",
        };
        db.GitHubRepositories.AddRange(api, web);
        await db.SaveChangesAsync();
        _apiRepositoryId = api.Id;
        _webRepositoryId = web.Id;

        db.ProjectRepositoryLinks.Add(new ProjectRepositoryLink
        {
            ProjectId = _tsProjectId,
            GitHubRepositoryId = _apiRepositoryId,
            CompanyId = _companyId,
            LinkedByUserId = "rigon",
        });
        await db.SaveChangesAsync();

        var ts42 = new TaskEntity { Title = "Panel", Number = 42, ProjectId = _tsProjectId, CompanyId = _companyId };
        // Same Number, different project: routing by number alone lands here.
        var bs42 = new TaskEntity { Title = "Purge", Number = 42, ProjectId = _bsProjectId, CompanyId = _companyId };
        db.Set<TaskEntity>().AddRange(ts42, bs42);
        await db.SaveChangesAsync();
        _ts42TaskId = ts42.Id;
    }

    public SystemTask.Task DisposeAsync() => SystemTask.Task.CompletedTask;

    [Fact]
    public async SystemTask.Task Resolves_a_key_whose_repository_is_linked_to_its_project()
    {
        await using var db = NewContext();
        var map = await TaskKeyResolutionMap.BuildAsync(NewUnitOfWork(db), _companyId, default);

        Assert.True(TaskKey.TryParse("TS-42", out var key));

        Assert.Equal(_ts42TaskId, map.Resolve(key, _apiRepositoryId));
    }

    [Fact]
    public async SystemTask.Task Refuses_a_key_whose_repository_is_not_linked_to_its_project()
    {
        await using var db = NewContext();
        var map = await TaskKeyResolutionMap.BuildAsync(NewUnitOfWork(db), _companyId, default);

        Assert.True(TaskKey.TryParse("TS-42", out var key));

        // The authorization boundary: web is linked to nothing.
        Assert.Null(map.Resolve(key, _webRepositoryId));
    }

    [Fact]
    public async SystemTask.Task Refuses_a_key_for_a_project_the_repository_is_not_linked_to()
    {
        await using var db = NewContext();
        var map = await TaskKeyResolutionMap.BuildAsync(NewUnitOfWork(db), _companyId, default);

        Assert.True(TaskKey.TryParse("BS-42", out var key));

        // api is linked to TS, not BS — and BS-42 exists, so this can only fail on authorization.
        Assert.Null(map.Resolve(key, _apiRepositoryId));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~TaskKeyResolutionMapTests`
Expected: FAIL — `TaskKeyResolutionMap` does not exist (compile error).

- [ ] **Step 3: Write the implementation**

Create `TaskSphere.Infrastructure/Services/TaskKeyResolutionMap.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.Interfaces;

namespace TaskSphere.Infrastructure.Services;

/// <summary>
/// A company-scoped snapshot of everything needed to route a task key to a task id, and the
/// one rule that authorizes it: a key is honoured only when the record's repository is linked
/// to that key's project. Keys route, the repo link authorizes.
/// <para>
/// Extracted so the boundary exists exactly once. Two callers hand-rolling it would drift, and
/// the drift's failure mode is writing to another project's tasks.
/// </para>
/// </summary>
public sealed class TaskKeyResolutionMap
{
    private readonly Dictionary<string, int> _projectIdByKey;
    private readonly HashSet<(int ProjectId, int RepositoryId)> _authorized;
    private readonly Dictionary<(int ProjectId, int Number), int> _taskIdByProjectAndNumber;

    private TaskKeyResolutionMap(
        Dictionary<string, int> projectIdByKey,
        HashSet<(int, int)> authorized,
        Dictionary<(int, int), int> taskIdByProjectAndNumber)
    {
        _projectIdByKey = projectIdByKey;
        _authorized = authorized;
        _taskIdByProjectAndNumber = taskIdByProjectAndNumber;
    }

    public static async Task<TaskKeyResolutionMap> BuildAsync(
        IUnitOfWork unitOfWork,
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        // Everything below is company-scoped and filtered. A soft-deleted link must not
        // authorize, and a soft-deleted project must not resolve a key.
        var projectsByKey = await unitOfWork.Projects
            .GetCompanyProjects(companyId)
            .Select(p => new { p.Id, p.Key })
            .ToDictionaryAsync(p => p.Key, p => p.Id, StringComparer.Ordinal, cancellationToken);

        var authorized = (await unitOfWork.ProjectRepositoryLinks
                .GetByCompany(companyId)
                .Select(l => new { l.ProjectId, l.GitHubRepositoryId })
                .ToListAsync(cancellationToken))
            .Select(l => (l.ProjectId, l.GitHubRepositoryId))
            .ToHashSet();

        var taskIdByProjectAndNumber = (await unitOfWork.Tasks
                .GetAll()
                .Where(t => t.CompanyId == companyId && t.ProjectId != null)
                .Select(t => new { t.Id, ProjectId = t.ProjectId!.Value, t.Number })
                .ToListAsync(cancellationToken))
            .ToDictionary(t => (t.ProjectId, t.Number), t => t.Id);

        return new TaskKeyResolutionMap(projectsByKey, authorized, taskIdByProjectAndNumber);
    }

    /// <summary>
    /// Steps 1-3 of the spec's resolution order, in order. Step 2 is the authorization
    /// boundary. Returns null when the key routes nowhere — normal traffic, never an error.
    /// </summary>
    public int? Resolve(TaskKey key, int gitHubRepositoryId)
    {
        if (!_projectIdByKey.TryGetValue(key.ProjectKey, out var projectId))
            return null;

        if (!_authorized.Contains((projectId, gitHubRepositoryId)))
            return null;

        return _taskIdByProjectAndNumber.TryGetValue((projectId, key.Number), out var taskId)
            ? taskId
            : null;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~TaskKeyResolutionMapTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Rewire `GitHubTaskLinkResolver` to use the map**

In `GitHubTaskLinkResolver.ResolveAsync`, delete the three local snapshot queries (`projectsByKey`, `authorized`, `taskIdByProjectAndNumber`) and the local `ResolveTask` function. Replace with:

```csharp
var map = await TaskKeyResolutionMap.BuildAsync(_unitOfWork, companyId, cancellationToken);
```

and change the one call site inside `LinkAll` from `ResolveTask(key, gitHubRepositoryId)` to `map.Resolve(key, gitHubRepositoryId)`.

Everything else in the file — the `existing` set, the commit/branch/pull loops, the counts, the save — is untouched.

- [ ] **Step 6: Run the resolver's existing tests to prove behaviour is unchanged**

Run: `dotnet test --filter FullyQualifiedName~GitHubTaskLinkResolverTests`
Expected: PASS, **same count as before the refactor**. If any test changed behaviour, the extraction is wrong — revert and redo. Do not edit these tests.

- [ ] **Step 7: Run the full backend suite**

Run: `dotnet test`
Expected: PASS at the pre-existing baseline (406 at the time of writing).

- [ ] **Step 8: Commit**

```bash
git add TaskSphere.Infrastructure/Services/TaskKeyResolutionMap.cs TaskSphere.Infrastructure/Services/GitHubTaskLinkResolver.cs TaskSphere.Tests/Integration/TaskKeyResolutionMapTests.cs
git commit -m "Extract the key resolution so the authorization boundary exists once, not twice"
```

---

### Task 2: The two columns and their migration

**Files:**
- Modify: `TaskSphere.Domain/Entities/GitHubPullRequest.cs`
- Modify: `TaskSphere.Domain/Entities/Project.cs`
- Create: `TaskSphere.Infrastructure/Migrations/<timestamp>_AddMergeTransitionMarkerAndAutoDoneOnMerge.cs`
- Test: `TaskSphere.Tests/Integration/MergeTransitionModelTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `GitHubPullRequest.MergeTransitionAppliedAtUtc` (`DateTime?`), `Project.AutoDoneOnMerge` (`bool`, default `false`).

- [ ] **Step 1: Write the failing test**

Create `TaskSphere.Tests/Integration/MergeTransitionModelTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.Enums;
using TaskSphere.Infrastructure.Data;

using Company = TaskSphere.Domain.Entities.Company;
using GitHubInstallation = TaskSphere.Domain.Entities.GitHubInstallation;
using GitHubPullRequest = TaskSphere.Domain.Entities.GitHubPullRequest;
using GitHubRepository = TaskSphere.Domain.Entities.GitHubRepository;
using Project = TaskSphere.Domain.Entities.Project;
using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

public class MergeTransitionModelTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereMergeTransitionModelTests;Trusted_Connection=True;TrustServerCertificate=True";

    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }

    public async SystemTask.Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public SystemTask.Task DisposeAsync() => SystemTask.Task.CompletedTask;

    [Fact]
    public async SystemTask.Task A_new_project_does_not_auto_done_on_merge()
    {
        await using var db = NewContext();
        var company = new Company { Name = "Defaults Co" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var project = new Project { Name = "P", Key = "PP", CompanyId = company.Id };
        db.Projects.Add(project);
        await db.SaveChangesAsync();

        var reloaded = await db.Projects.SingleAsync(p => p.Id == project.Id);

        Assert.False(reloaded.AutoDoneOnMerge);
    }

    [Fact]
    public async SystemTask.Task A_new_pull_request_has_no_merge_transition_marker()
    {
        await using var db = NewContext();
        var company = new Company { Name = "Marker Co" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var installation = new GitHubInstallation
        {
            InstallationId = 9501,
            CompanyId = company.Id,
            AccountLogin = "rigon-org",
            AccountType = "Organization",
            RepositorySelection = RepositorySelection.All,
        };
        db.GitHubInstallations.Add(installation);
        await db.SaveChangesAsync();

        var repository = new GitHubRepository
        {
            RepositoryId = 9601,
            GitHubInstallationId = installation.Id,
            CompanyId = company.Id,
            FullName = "rigon-org/api",
            DefaultBranch = "main",
        };
        db.GitHubRepositories.Add(repository);
        await db.SaveChangesAsync();

        var pull = new GitHubPullRequest
        {
            GitHubRepositoryId = repository.Id,
            CompanyId = company.Id,
            Number = 1,
            Title = "Add the panel",
            State = PullRequestState.Merged,
            AuthorLogin = "rigon",
            HeadBranch = "TS-42/add-the-panel",
            OpenedAtUtc = DateTime.UtcNow,
            GitHubUpdatedAtUtc = DateTime.UtcNow,
            MergedAtUtc = DateTime.UtcNow,
            HtmlUrl = "https://github.com/rigon-org/api/pull/1",
        };
        db.GitHubPullRequests.Add(pull);
        await db.SaveChangesAsync();

        var reloaded = await db.GitHubPullRequests.SingleAsync(p => p.Id == pull.Id);

        Assert.Null(reloaded.MergeTransitionAppliedAtUtc);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~MergeTransitionModelTests`
Expected: FAIL — the properties do not exist (compile error).

- [ ] **Step 3: Add the properties**

In `TaskSphere.Domain/Entities/GitHubPullRequest.cs`, after `HtmlUrl`:

```csharp
    /// <summary>
    /// TaskSphere's own column, not GitHub's — non-null means "this pull request has already
    /// been considered for a merge → Done transition", whether or not anything moved.
    /// It is how the transition is made idempotent without observing a state edge: the sync
    /// overwrites <see cref="State"/> on every pass, so "just became merged" is not
    /// observable after the write.
    /// The sync's upsert must leave this field alone.
    /// </summary>
    public DateTime? MergeTransitionAppliedAtUtc { get; set; }
```

In `TaskSphere.Domain/Entities/Project.cs`, after `NextTaskNumber`:

```csharp
    /// <summary>
    /// Opt-in, per project. When false the merge → Done transition resolves and marks pull
    /// requests as considered but never writes a status.
    /// </summary>
    public bool AutoDoneOnMerge { get; set; } = false;
```

- [ ] **Step 4: Generate the migration**

```bash
dotnet ef migrations add AddMergeTransitionMarkerAndAutoDoneOnMerge --project TaskSphere.Infrastructure --startup-project TaskSphere
```

Open the generated file and confirm it contains exactly two `AddColumn` calls — `MergeTransitionAppliedAtUtc` (nullable `datetime2`) on `GitHubPullRequests`, and `AutoDoneOnMerge` (`bit`, `defaultValue: false`, not nullable) on `Projects`. If it contains anything else, the model snapshot has drifted; stop and report rather than editing the migration by hand.

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~MergeTransitionModelTests`
Expected: PASS (2 tests).

- [ ] **Step 6: Apply the migration to the dev database**

```bash
dotnet ef database update --project TaskSphere.Infrastructure --startup-project TaskSphere
```

- [ ] **Step 7: Commit**

```bash
git add TaskSphere.Domain/Entities/GitHubPullRequest.cs TaskSphere.Domain/Entities/Project.cs TaskSphere.Infrastructure/Migrations TaskSphere.Tests/Integration/MergeTransitionModelTests.cs
git commit -m "Add the applied-once marker and the per-project opt-in"
```

---

### Task 3: The sync upsert must not clear the marker

**Files:**
- Modify: `TaskSphere.Infrastructure/Services/GitHubActivitySyncService.cs:409-426`
- Test: `TaskSphere.Tests/Integration/MergeTransitionModelTests.cs` (append)

**Interfaces:**
- Consumes: `GitHubPullRequest.MergeTransitionAppliedAtUtc` from Task 2.
- Produces: nothing new.

The upsert block comments itself "a pull request is a state machine: everything mutable is overwritten." That is true of GitHub-sourced fields and false of this one. The risk is a future edit adding it to the overwrite list; this test pins it.

- [ ] **Step 1: Write the failing test**

Append to `MergeTransitionModelTests`:

```csharp
    [Fact]
    public async SystemTask.Task The_upsert_block_does_not_list_the_marker_among_overwritten_fields()
    {
        // A source-level guard. The upsert overwrites every GitHub-sourced field by design;
        // this column is TaskSphere's own and must survive a re-sync. A behavioural test would
        // need the whole HTTP fake stack, so this pins the one line that would break it.
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "TaskSphere.Infrastructure", "Services", "GitHubActivitySyncService.cs");

        var source = await File.ReadAllTextAsync(Path.GetFullPath(path));

        Assert.DoesNotContain("existing.MergeTransitionAppliedAtUtc", source);
    }
```

- [ ] **Step 2: Run test to verify it passes for the right reason**

Run: `dotnet test --filter FullyQualifiedName~The_upsert_block_does_not_list_the_marker`
Expected: PASS.

Then **prove it can fail**: temporarily add the line `existing.MergeTransitionAppliedAtUtc = null;` to the upsert block, re-run, confirm FAIL, then remove it. A test that cannot fail is worse than no test — this project has shipped three of them.

- [ ] **Step 3: Add the explaining comment to the upsert block**

In `GitHubActivitySyncService.cs`, inside the `else` branch of the pull-request upsert, after `existing.HtmlUrl = pull.HtmlUrl ?? "";`:

```csharp
                // MergeTransitionAppliedAtUtc is deliberately NOT overwritten: it is
                // TaskSphere's own marker, not a GitHub-sourced field, and clearing it here
                // would re-apply every merge transition on the next sync.
```

- [ ] **Step 4: Run the suite**

Run: `dotnet test`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add TaskSphere.Infrastructure/Services/GitHubActivitySyncService.cs TaskSphere.Tests/Integration/MergeTransitionModelTests.cs
git commit -m "Pin the marker against the upsert that overwrites everything else"
```

---

### Task 4: The transition service — the happy path

**Files:**
- Create: `TaskSphere.Application/Interfaces/IMergeTransitionService.cs`
- Create: `TaskSphere.Infrastructure/Services/MergeTransitionService.cs`
- Test: `TaskSphere.Tests/Integration/MergeTransitionTests.cs`

**Interfaces:**
- Consumes: `TaskKeyResolutionMap.BuildAsync/Resolve` (Task 1); both columns (Task 2).
- Produces:
  - `sealed record MergeTransitionResult(int Transitioned, int Skipped, int Failed)`
  - `IMergeTransitionService.ApplyAsync(Guid companyId, string? actorUsername, CancellationToken cancellationToken = default)` returning `Task<Result<MergeTransitionResult>>`
  - `MergeTransitionService(IUnitOfWork unitOfWork, AuditQueue auditQueue)`

- [ ] **Step 1: Write the interface**

Create `TaskSphere.Application/Interfaces/IMergeTransitionService.cs`:

```csharp
using TaskSphere.Domain.Common;

namespace TaskSphere.Application.Interfaces;

/// <summary>
/// Counts for the sync summary. <c>Transitioned</c> is tasks actually moved to Done;
/// <c>Skipped</c> is pull requests considered that moved nothing — no key, an unresolvable
/// key, the project opted out, or the task was Blocked or already Done; <c>Failed</c> is pull
/// requests that threw. A failure is a count, not an error: it does not abort the pass.
/// </summary>
public sealed record MergeTransitionResult(int Transitioned, int Skipped, int Failed);

/// <summary>
/// Moves a task to Done when a pull request whose HEAD BRANCH names its key is merged.
/// <para>
/// It does not observe a state change. The sync overwrites PullRequest.State on every pass, so
/// "just became merged" is unobservable after the write; instead a pull request is eligible
/// when <c>State == Merged</c> and its <c>MergeTransitionAppliedAtUtc</c> marker is null, and
/// the marker is stamped unconditionally afterwards.
/// </para>
/// <para>
/// The key comes from the head branch only — never the title or body. A pull request that
/// merely mentions TS-42 does not move TS-42.
/// </para>
/// </summary>
public interface IMergeTransitionService
{
    Task<Result<MergeTransitionResult>> ApplyAsync(
        Guid companyId,
        string? actorUsername,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Write the failing test**

Create `TaskSphere.Tests/Integration/MergeTransitionTests.cs`. This fixture is reused by Tasks 5-7, so build it fully now.

```csharp
using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.Audit;
using TaskSphere.Domain.Enums;
using TaskSphere.Infrastructure.Data;
using TaskSphere.Infrastructure.Repositories;
using TaskSphere.Infrastructure.Services;

using Company = TaskSphere.Domain.Entities.Company;
using GitHubInstallation = TaskSphere.Domain.Entities.GitHubInstallation;
using GitHubPullRequest = TaskSphere.Domain.Entities.GitHubPullRequest;
using GitHubRepository = TaskSphere.Domain.Entities.GitHubRepository;
using Project = TaskSphere.Domain.Entities.Project;
using ProjectRepositoryLink = TaskSphere.Domain.Entities.ProjectRepositoryLink;
using TaskEntity = TaskSphere.Domain.Entities.Task;
using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

public class MergeTransitionTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereMergeTransitionTests;Trusted_Connection=True;TrustServerCertificate=True";

    private Guid _companyId;
    private int _tsProjectId;      // key "TS", linked to api, AutoDoneOnMerge = true
    private int _bsProjectId;      // key "BS", linked to nothing, AutoDoneOnMerge = true
    private int _optOutProjectId;  // key "OO", linked to api, AutoDoneOnMerge = false
    private int _apiRepositoryId;
    private int _webRepositoryId;  // linked to no project

    private int _ts42TaskId;
    private int _ts51TaskId;
    private int _ts60TaskId;
    private int _bs42TaskId;
    private int _oo9TaskId;

    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }

    private static MergeTransitionService NewService(ApplicationDbContext db, AuditQueue queue)
        => new(new UnitOfWork(db), queue);

    public async SystemTask.Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();

        var company = new Company { Name = "Merge Co" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        _companyId = company.Id;

        // DECOY ROWS — see Global Constraints. Projects, Repositories, Tasks and PullRequests
        // must not share identity values, or a wrong-id lookup passes by accident.
        db.Projects.AddRange(
            new Project { Name = "Decoy A", Key = "DECA", CompanyId = _companyId },
            new Project { Name = "Decoy B", Key = "DECB", CompanyId = _companyId },
            new Project { Name = "Decoy C", Key = "DECC", CompanyId = _companyId },
            new Project { Name = "Decoy D", Key = "DECD", CompanyId = _companyId });
        await db.SaveChangesAsync();

        var ts = new Project { Name = "TaskSphere", Key = "TS", CompanyId = _companyId, AutoDoneOnMerge = true };
        var bs = new Project { Name = "BaseClean", Key = "BS", CompanyId = _companyId, AutoDoneOnMerge = true };
        var oo = new Project { Name = "Opted Out", Key = "OO", CompanyId = _companyId, AutoDoneOnMerge = false };
        db.Projects.AddRange(ts, bs, oo);
        await db.SaveChangesAsync();
        _tsProjectId = ts.Id;
        _bsProjectId = bs.Id;
        _optOutProjectId = oo.Id;

        var installation = new GitHubInstallation
        {
            InstallationId = 9701,
            CompanyId = _companyId,
            AccountLogin = "rigon-org",
            AccountType = "Organization",
            RepositorySelection = RepositorySelection.All,
        };
        db.GitHubInstallations.Add(installation);
        await db.SaveChangesAsync();

        var api = new GitHubRepository
        {
            RepositoryId = 9801,
            GitHubInstallationId = installation.Id,
            CompanyId = _companyId,
            FullName = "rigon-org/api",
            DefaultBranch = "main",
        };
        var web = new GitHubRepository
        {
            RepositoryId = 9802,
            GitHubInstallationId = installation.Id,
            CompanyId = _companyId,
            FullName = "rigon-org/web",
            DefaultBranch = "main",
        };
        db.GitHubRepositories.AddRange(api, web);
        await db.SaveChangesAsync();
        _apiRepositoryId = api.Id;
        _webRepositoryId = web.Id;

        db.ProjectRepositoryLinks.AddRange(
            new ProjectRepositoryLink
            {
                ProjectId = _tsProjectId,
                GitHubRepositoryId = _apiRepositoryId,
                CompanyId = _companyId,
                LinkedByUserId = "rigon",
            },
            new ProjectRepositoryLink
            {
                ProjectId = _optOutProjectId,
                GitHubRepositoryId = _apiRepositoryId,
                CompanyId = _companyId,
                LinkedByUserId = "rigon",
            });
        await db.SaveChangesAsync();

        var ts42 = new TaskEntity { Title = "Panel", Number = 42, ProjectId = _tsProjectId, CompanyId = _companyId, Status = TaskStatuses.InProgress };
        var ts51 = new TaskEntity { Title = "Sync", Number = 51, ProjectId = _tsProjectId, CompanyId = _companyId, Status = TaskStatuses.Blocked };
        var ts60 = new TaskEntity { Title = "Tab", Number = 60, ProjectId = _tsProjectId, CompanyId = _companyId, Status = TaskStatuses.Open };
        var bs42 = new TaskEntity { Title = "Purge", Number = 42, ProjectId = _bsProjectId, CompanyId = _companyId, Status = TaskStatuses.Open };
        var oo9 = new TaskEntity { Title = "Ignore", Number = 9, ProjectId = _optOutProjectId, CompanyId = _companyId, Status = TaskStatuses.Open };
        db.Set<TaskEntity>().AddRange(ts42, ts51, ts60, bs42, oo9);
        await db.SaveChangesAsync();

        _ts42TaskId = ts42.Id;
        _ts51TaskId = ts51.Id;
        _ts60TaskId = ts60.Id;
        _bs42TaskId = bs42.Id;
        _oo9TaskId = oo9.Id;
    }

    public SystemTask.Task DisposeAsync() => SystemTask.Task.CompletedTask;

    private async SystemTask.Task<int> AddPullRequest(
        int repositoryId,
        int number,
        string headBranch,
        PullRequestState state = PullRequestState.Merged,
        DateTime? markerAppliedAt = null)
    {
        await using var db = NewContext();
        var pull = new GitHubPullRequest
        {
            GitHubRepositoryId = repositoryId,
            CompanyId = _companyId,
            Number = number,
            Title = "A pull request",
            State = state,
            AuthorLogin = "rigon",
            HeadBranch = headBranch,
            OpenedAtUtc = DateTime.UtcNow.AddDays(-1),
            GitHubUpdatedAtUtc = DateTime.UtcNow,
            MergedAtUtc = state == PullRequestState.Merged ? DateTime.UtcNow : null,
            HtmlUrl = $"https://github.com/rigon-org/api/pull/{number}",
            MergeTransitionAppliedAtUtc = markerAppliedAt,
        };
        db.GitHubPullRequests.Add(pull);
        await db.SaveChangesAsync();
        return pull.Id;
    }

    private async SystemTask.Task<string> StatusOf(int taskId)
    {
        await using var db = NewContext();
        var task = await db.Set<TaskEntity>().SingleAsync(t => t.Id == taskId);
        return task.Status;
    }

    private async SystemTask.Task<DateTime?> MarkerOf(int pullRequestId)
    {
        await using var db = NewContext();
        var pull = await db.GitHubPullRequests.SingleAsync(p => p.Id == pullRequestId);
        return pull.MergeTransitionAppliedAtUtc;
    }

    [Fact]
    public async SystemTask.Task Moves_an_in_progress_task_to_done_when_its_branch_pull_request_merges()
    {
        var pullId = await AddPullRequest(_apiRepositoryId, 1, "TS-42/add-the-panel");

        await using var db = NewContext();
        var result = await NewService(db, new AuditQueue()).ApplyAsync(_companyId, "rigon", default);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.Transitioned);
        Assert.Equal(TaskStatuses.Done, await StatusOf(_ts42TaskId));
        Assert.NotNull(await MarkerOf(pullId));
    }

    [Fact]
    public async SystemTask.Task Moves_an_open_task_to_done()
    {
        await AddPullRequest(_apiRepositoryId, 2, "TS-60/the-tab");

        await using var db = NewContext();
        var result = await NewService(db, new AuditQueue()).ApplyAsync(_companyId, "rigon", default);

        Assert.Equal(1, result.Value!.Transitioned);
        Assert.Equal(TaskStatuses.Done, await StatusOf(_ts60TaskId));
    }

    [Fact]
    public async SystemTask.Task Ignores_a_pull_request_that_is_open_rather_than_merged()
    {
        var pullId = await AddPullRequest(_apiRepositoryId, 3, "TS-42/add-the-panel", PullRequestState.Open);

        await using var db = NewContext();
        var result = await NewService(db, new AuditQueue()).ApplyAsync(_companyId, "rigon", default);

        Assert.Equal(0, result.Value!.Transitioned);
        Assert.Equal(TaskStatuses.InProgress, await StatusOf(_ts42TaskId));
        // Not considered at all: an open pull request may still merge later.
        Assert.Null(await MarkerOf(pullId));
    }

    [Fact]
    public async SystemTask.Task Stamps_the_marker_for_a_branch_that_names_no_key()
    {
        var pullId = await AddPullRequest(_apiRepositoryId, 4, "hotfix/login");

        await using var db = NewContext();
        var result = await NewService(db, new AuditQueue()).ApplyAsync(_companyId, "rigon", default);

        Assert.Equal(0, result.Value!.Transitioned);
        Assert.Equal(1, result.Value!.Skipped);
        // Its branch name cannot change retroactively, so it is never worth reconsidering.
        Assert.NotNull(await MarkerOf(pullId));
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~MergeTransitionTests`
Expected: FAIL — `MergeTransitionService` does not exist (compile error).

- [ ] **Step 4: Write the implementation**

Create `TaskSphere.Infrastructure/Services/MergeTransitionService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Audit;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.Enums;
using TaskSphere.Domain.Interfaces;

using TaskEntity = TaskSphere.Domain.Entities.Task;

namespace TaskSphere.Infrastructure.Services;

public class MergeTransitionService : IMergeTransitionService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly AuditQueue _auditQueue;

    public MergeTransitionService(IUnitOfWork unitOfWork, AuditQueue auditQueue)
    {
        _unitOfWork = unitOfWork;
        _auditQueue = auditQueue;
    }

    public async Task<Result<MergeTransitionResult>> ApplyAsync(
        Guid companyId,
        string? actorUsername,
        CancellationToken cancellationToken = default)
    {
        var pending = await _unitOfWork.GitHubPullRequests
            .GetByCompany(companyId)
            .Where(p => p.State == PullRequestState.Merged && p.MergeTransitionAppliedAtUtc == null)
            .Select(p => new { p.Id, p.GitHubRepositoryId, p.Number, p.HeadBranch })
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
            return Result<MergeTransitionResult>.Success(new MergeTransitionResult(0, 0, 0));

        var map = await TaskKeyResolutionMap.BuildAsync(_unitOfWork, companyId, cancellationToken);

        var autoDoneByProjectId = await _unitOfWork.Projects
            .GetCompanyProjects(companyId)
            .Select(p => new { p.Id, p.AutoDoneOnMerge })
            .ToDictionaryAsync(p => p.Id, p => p.AutoDoneOnMerge, cancellationToken);

        var transitioned = 0;
        var skipped = 0;
        var failed = 0;

        foreach (var pull in pending)
        {
            try
            {
                var movedHere = 0;

                foreach (var key in TaskKeyScanner.Scan(pull.HeadBranch))
                {
                    var taskId = map.Resolve(key, pull.GitHubRepositoryId);
                    if (taskId is null)
                        continue;

                    var task = await _unitOfWork.Tasks.GetByIdAsync(taskId.Value, cancellationToken);
                    if (task is null)
                        continue;

                    if (task.ProjectId is null ||
                        !autoDoneByProjectId.TryGetValue(task.ProjectId.Value, out var autoDone) ||
                        !autoDone)
                        continue;

                    if (task.Status != TaskStatuses.Open && task.Status != TaskStatuses.InProgress)
                        continue;

                    task.Status = TaskStatuses.Done;
                    await _unitOfWork.Tasks.Update(task, cancellationToken);

                    Enqueue(companyId, actorUsername, key.ToString(), pull.Number);
                    movedHere++;
                }

                var stored = await _unitOfWork.GitHubPullRequests.GetByIdAsync(pull.Id, cancellationToken);
                if (stored is not null)
                {
                    // Stamped unconditionally: a pull request considered once is never
                    // reconsidered, whether it moved a task, was opted out, or named nothing.
                    stored.MergeTransitionAppliedAtUtc = DateTime.UtcNow;
                    await _unitOfWork.GitHubPullRequests.Update(stored, cancellationToken);
                }

                // Per pull request, so a later failure cannot discard earlier work. The unit
                // reported must equal the unit persisted.
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                transitioned += movedHere;
                if (movedHere == 0)
                    skipped++;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                failed++;
            }
        }

        return Result<MergeTransitionResult>.Success(
            new MergeTransitionResult(transitioned, skipped, failed));
    }

    private void Enqueue(Guid companyId, string? actorUsername, string taskKey, int pullNumber)
    {
        // AuditEntry is HTTP-shaped; a sync-driven transition has no request, so those fields
        // stay empty. The audit dashboard must render such a row.
        _auditQueue.TryWrite(new AuditEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            CompanyId = companyId,
            Username = actorUsername,
            Action = $"Moved {taskKey} to Done — pull request #{pullNumber} was merged",
        });
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~MergeTransitionTests`
Expected: PASS (4 tests).

- [ ] **Step 6: Commit**

```bash
git add TaskSphere.Application/Interfaces/IMergeTransitionService.cs TaskSphere.Infrastructure/Services/MergeTransitionService.cs TaskSphere.Tests/Integration/MergeTransitionTests.cs
git commit -m "Move a task to Done when the pull request on its branch merges"
```

---

### Task 5: The guard, the toggle, and the marker they share

**Files:**
- Test: `TaskSphere.Tests/Integration/MergeTransitionTests.cs` (append)

**Interfaces:**
- Consumes: everything from Task 4.
- Produces: nothing new — this task proves behaviour already implemented, and fixes it if the tests fail.

- [ ] **Step 1: Write the failing tests**

Append to `MergeTransitionTests`:

```csharp
    [Fact]
    public async SystemTask.Task Leaves_a_blocked_task_alone_but_still_stamps_the_marker()
    {
        var pullId = await AddPullRequest(_apiRepositoryId, 10, "TS-51/the-sync");

        await using var db = NewContext();
        var result = await NewService(db, new AuditQueue()).ApplyAsync(_companyId, "rigon", default);

        // Someone deliberately flagged a problem; a merge does not clear it.
        Assert.Equal(0, result.Value!.Transitioned);
        Assert.Equal(TaskStatuses.Blocked, await StatusOf(_ts51TaskId));
        Assert.NotNull(await MarkerOf(pullId));
    }

    [Fact]
    public async SystemTask.Task Does_not_revisit_a_blocked_task_after_it_is_unblocked()
    {
        await AddPullRequest(_apiRepositoryId, 11, "TS-51/the-sync");

        await using (var first = NewContext())
            await NewService(first, new AuditQueue()).ApplyAsync(_companyId, "rigon", default);

        await using (var edit = NewContext())
        {
            var task = await edit.Set<TaskEntity>().SingleAsync(t => t.Id == _ts51TaskId);
            task.Status = TaskStatuses.InProgress;
            await edit.SaveChangesAsync();
        }

        await using var second = NewContext();
        var result = await NewService(second, new AuditQueue()).ApplyAsync(_companyId, "rigon", default);

        Assert.Equal(0, result.Value!.Transitioned);
        Assert.Equal(TaskStatuses.InProgress, await StatusOf(_ts51TaskId));
    }

    [Fact]
    public async SystemTask.Task Writes_no_status_when_the_project_has_opted_out_but_stamps_the_marker()
    {
        var pullId = await AddPullRequest(_apiRepositoryId, 12, "OO-9/ignore-me");

        await using var db = NewContext();
        var result = await NewService(db, new AuditQueue()).ApplyAsync(_companyId, "rigon", default);

        Assert.Equal(0, result.Value!.Transitioned);
        Assert.Equal(TaskStatuses.Open, await StatusOf(_oo9TaskId));
        Assert.NotNull(await MarkerOf(pullId));
    }

    [Fact]
    public async SystemTask.Task Enabling_the_toggle_later_does_not_retroactively_move_the_task()
    {
        await AddPullRequest(_apiRepositoryId, 13, "OO-9/ignore-me");

        await using (var first = NewContext())
            await NewService(first, new AuditQueue()).ApplyAsync(_companyId, "rigon", default);

        await using (var edit = NewContext())
        {
            var project = await edit.Projects.SingleAsync(p => p.Id == _optOutProjectId);
            project.AutoDoneOnMerge = true;
            await edit.SaveChangesAsync();
        }

        await using var second = NewContext();
        var result = await NewService(second, new AuditQueue()).ApplyAsync(_companyId, "rigon", default);

        // Deliberate: ticking a checkbox must not mass-move a month of merged work.
        Assert.Equal(0, result.Value!.Transitioned);
        Assert.Equal(TaskStatuses.Open, await StatusOf(_oo9TaskId));
    }

    [Fact]
    public async SystemTask.Task Leaves_a_task_that_is_already_done_alone()
    {
        await using (var edit = NewContext())
        {
            var task = await edit.Set<TaskEntity>().SingleAsync(t => t.Id == _ts60TaskId);
            task.Status = TaskStatuses.Done;
            await edit.SaveChangesAsync();
        }

        await AddPullRequest(_apiRepositoryId, 14, "TS-60/the-tab");

        await using var db = NewContext();
        var result = await NewService(db, new AuditQueue()).ApplyAsync(_companyId, "rigon", default);

        Assert.Equal(0, result.Value!.Transitioned);
        Assert.Equal(TaskStatuses.Done, await StatusOf(_ts60TaskId));
    }
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test --filter FullyQualifiedName~MergeTransitionTests`
Expected: PASS (9 tests). If any fail, fix `MergeTransitionService` — do not weaken the test.

- [ ] **Step 3: Prove the guard test can fail**

Temporarily change the guard in `MergeTransitionService` from

```csharp
if (task.Status != TaskStatuses.Open && task.Status != TaskStatuses.InProgress)
```

to

```csharp
if (task.Status == TaskStatuses.Done)
```

Re-run. Expected: `Leaves_a_blocked_task_alone_but_still_stamps_the_marker` FAILS. Revert the change and re-run to green. A guard with no failing test is not a guard.

- [ ] **Step 4: Commit**

```bash
git add TaskSphere.Tests/Integration/MergeTransitionTests.cs
git commit -m "Pin the Blocked guard and the opt-out, including that neither is ever revisited"
```

---

### Task 6: Idempotency, the human override, and the authorization boundary

**Files:**
- Test: `TaskSphere.Tests/Integration/MergeTransitionTests.cs` (append)

**Interfaces:**
- Consumes: everything from Task 4.
- Produces: nothing new.

The human-override test is the one that justifies the marker's existence. The authorization test covers the worst failure this feature can produce.

- [ ] **Step 1: Write the failing tests**

Append to `MergeTransitionTests`:

```csharp
    [Fact]
    public async SystemTask.Task Applies_exactly_once_across_repeated_passes()
    {
        await AddPullRequest(_apiRepositoryId, 20, "TS-42/add-the-panel");

        var queue = new AuditQueue();

        await using (var first = NewContext())
        {
            var result = await NewService(first, queue).ApplyAsync(_companyId, "rigon", default);
            Assert.Equal(1, result.Value!.Transitioned);
        }

        await using var second = NewContext();
        var again = await NewService(second, queue).ApplyAsync(_companyId, "rigon", default);

        Assert.Equal(0, again.Value!.Transitioned);
        Assert.Equal(0, again.Value!.Skipped);
    }

    [Fact]
    public async SystemTask.Task Does_not_re_apply_after_a_human_moves_the_task_back()
    {
        await AddPullRequest(_apiRepositoryId, 21, "TS-42/add-the-panel");

        await using (var first = NewContext())
            await NewService(first, new AuditQueue()).ApplyAsync(_companyId, "rigon", default);

        Assert.Equal(TaskStatuses.Done, await StatusOf(_ts42TaskId));

        // A lead decides the work is not finished after all.
        await using (var edit = NewContext())
        {
            var task = await edit.Set<TaskEntity>().SingleAsync(t => t.Id == _ts42TaskId);
            task.Status = TaskStatuses.InProgress;
            await edit.SaveChangesAsync();
        }

        await using var second = NewContext();
        var result = await NewService(second, new AuditQueue()).ApplyAsync(_companyId, "rigon", default);

        // The whole reason the marker exists: a status change is an action, not a fact, and
        // re-applying it every sync would overrule the human.
        Assert.Equal(0, result.Value!.Transitioned);
        Assert.Equal(TaskStatuses.InProgress, await StatusOf(_ts42TaskId));
    }

    [Fact]
    public async SystemTask.Task Refuses_a_key_whose_project_is_not_linked_to_the_pull_requests_repository()
    {
        // BS-42 exists and is Open, and BS has AutoDoneOnMerge = true — the ONLY thing that
        // may stop this is the repository↔project link. api is linked to TS and OO, not BS.
        var pullId = await AddPullRequest(_apiRepositoryId, 22, "BS-42/purge-it");

        await using var db = NewContext();
        var result = await NewService(db, new AuditQueue()).ApplyAsync(_companyId, "rigon", default);

        Assert.Equal(0, result.Value!.Transitioned);
        Assert.Equal(TaskStatuses.Open, await StatusOf(_bs42TaskId));
        Assert.NotNull(await MarkerOf(pullId));
    }

    [Fact]
    public async SystemTask.Task Refuses_every_key_from_a_repository_linked_to_no_project()
    {
        var pullId = await AddPullRequest(_webRepositoryId, 23, "TS-42/add-the-panel");

        await using var db = NewContext();
        var result = await NewService(db, new AuditQueue()).ApplyAsync(_companyId, "rigon", default);

        Assert.Equal(0, result.Value!.Transitioned);
        Assert.Equal(TaskStatuses.InProgress, await StatusOf(_ts42TaskId));
        Assert.NotNull(await MarkerOf(pullId));
    }

    [Fact]
    public async SystemTask.Task Transitions_every_task_a_multi_key_branch_names_then_stamps_once()
    {
        var pullId = await AddPullRequest(_apiRepositoryId, 24, "TS-42-and-TS-60/two-at-once");

        await using var db = NewContext();
        var result = await NewService(db, new AuditQueue()).ApplyAsync(_companyId, "rigon", default);

        Assert.Equal(2, result.Value!.Transitioned);
        Assert.Equal(TaskStatuses.Done, await StatusOf(_ts42TaskId));
        Assert.Equal(TaskStatuses.Done, await StatusOf(_ts60TaskId));
        Assert.NotNull(await MarkerOf(pullId));
    }
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test --filter FullyQualifiedName~MergeTransitionTests`
Expected: PASS (14 tests).

If `Transitions_every_task_a_multi_key_branch_names_then_stamps_once` fails, check `TaskKeyScanner.Scan("TS-42-and-TS-60/two-at-once")` in isolation before changing the service — the scanner's lookbehind and trailing `(?![0-9])` rules decide what that branch name yields, and the test must match the scanner's real behaviour rather than an assumption about it.

- [ ] **Step 3: Prove the authorization test can fail**

Temporarily delete the authorization check from `TaskKeyResolutionMap.Resolve`:

```csharp
// if (!_authorized.Contains((projectId, gitHubRepositoryId))) return null;
```

Re-run. Expected: **both** `Refuses_a_key_whose_project_is_not_linked...` and `Refuses_every_key_from_a_repository_linked_to_no_project` FAIL. Restore the line and re-run to green.

This is the single most important mutation in the feature: if either test still passes without the check, the authorization boundary is untested and the task is not done.

- [ ] **Step 4: Commit**

```bash
git add TaskSphere.Tests/Integration/MergeTransitionTests.cs
git commit -m "Pin the once-only guarantee, the human override, and the repo link that authorizes"
```

---

### Task 7: Partial failure is a count, not an abort

**Files:**
- Test: `TaskSphere.Tests/Integration/MergeTransitionTests.cs` (append)

**Interfaces:**
- Consumes: everything from Task 4.
- Produces: nothing new.

The 2026-08-19 session reversed exactly this mistake in the sync loop — the reported unit must equal the persisted unit.

- [ ] **Step 1: Write the failing test**

Append to `MergeTransitionTests`:

```csharp
    [Fact]
    public async SystemTask.Task A_pull_request_pointing_at_a_deleted_task_does_not_discard_earlier_work()
    {
        // Two pull requests in one pass. The first moves a task; the second names a task that
        // was soft-deleted after the map was built, so it resolves to nothing.
        await AddPullRequest(_apiRepositoryId, 30, "TS-42/add-the-panel");
        await AddPullRequest(_apiRepositoryId, 31, "TS-60/the-tab");

        await using (var edit = NewContext())
        {
            var task = await edit.Set<TaskEntity>().SingleAsync(t => t.Id == _ts60TaskId);
            task.IsDeleted = true;
            task.DeletedAt = DateTime.UtcNow;
            await edit.SaveChangesAsync();
        }

        await using var db = NewContext();
        var result = await NewService(db, new AuditQueue()).ApplyAsync(_companyId, "rigon", default);

        // The first pull request's work is persisted regardless of what the second one does.
        Assert.Equal(TaskStatuses.Done, await StatusOf(_ts42TaskId));
        Assert.Equal(1, result.Value!.Transitioned);
    }

    [Fact]
    public async SystemTask.Task Reports_success_with_counts_rather_than_an_error_when_nothing_is_pending()
    {
        await using var db = NewContext();
        var result = await NewService(db, new AuditQueue()).ApplyAsync(_companyId, "rigon", default);

        Assert.True(result.IsSuccess);
        Assert.Equal(new MergeTransitionResult(0, 0, 0), result.Value);
    }
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test --filter FullyQualifiedName~MergeTransitionTests`
Expected: PASS (16 tests).

- [ ] **Step 3: Prove the persistence unit is really per pull request**

Temporarily move `await _unitOfWork.SaveChangesAsync(cancellationToken);` out of the `foreach` loop to after it. Re-run `A_pull_request_pointing_at_a_deleted_task_does_not_discard_earlier_work`.

If it still passes, the test does not actually witness the per-pull-request unit — strengthen it (add a pull request that throws) before restoring. Restore the save inside the loop and re-run to green.

- [ ] **Step 4: Commit**

```bash
git add TaskSphere.Tests/Integration/MergeTransitionTests.cs
git commit -m "Pin the persistence unit to the pull request, the unit the counts report"
```

---

### Task 8: Wire the transition into the sync, with the actor

**Files:**
- Modify: `TaskSphere.Application/Interfaces/IGitHubActivitySyncService.cs`
- Modify: `TaskSphere.Infrastructure/Services/GitHubActivitySyncService.cs`
- Modify: `TaskSphere.Domain/DataTransferObjects/GitHub/GitHubActivityDtos.cs`
- Modify: `TaskSphere/Controllers/GitHubController.cs:123-129`
- Modify: `TaskSphere/Extensions/ApplicationServices.cs`
- Test: `TaskSphere.Tests/Integration/GitHubDependencyInjectionTests.cs` (append)

**Interfaces:**
- Consumes: `IMergeTransitionService.ApplyAsync` (Task 4).
- Produces:
  - `IGitHubActivitySyncService.SyncCompanyAsync(Guid companyId, string? actorUsername, CancellationToken cancellationToken = default)`
  - `SyncActivityResultDto` gains `int TasksTransitioned` as its sixth member, before `Failures`.

- [ ] **Step 1: Write the failing DI test**

Append to `GitHubDependencyInjectionTests`. **Use the provider-building helper that file already defines** — copy the exact name and call shape from the tests beside it; `BuildProvider()` below is a stand-in for whatever it is actually called.

```csharp
    [Fact]
    public void MergeTransitionService_resolves()
    {
        using var scope = BuildProvider().CreateScope();

        var service = scope.ServiceProvider.GetService<IMergeTransitionService>();

        Assert.NotNull(service);
        Assert.IsType<MergeTransitionService>(service);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~GitHubDependencyInjectionTests`
Expected: FAIL — `IMergeTransitionService` is not registered.

- [ ] **Step 3: Register the service**

In `TaskSphere/Extensions/ApplicationServices.cs`, beside the other GitHub service registrations:

```csharp
services.AddScoped<IMergeTransitionService, MergeTransitionService>();
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~GitHubDependencyInjectionTests`
Expected: PASS.

- [ ] **Step 5: Widen the DTO**

In `GitHubActivityDtos.cs`:

```csharp
public record SyncActivityResultDto(
    int RepositoriesSynced,
    int Commits,
    int Branches,
    int PullRequests,
    int LinksCreated,
    int TasksTransitioned,
    IReadOnlyList<SyncFailureDto> Failures);
```

- [ ] **Step 6: Thread the actor and call the transition**

In `IGitHubActivitySyncService.cs`:

```csharp
    Task<Result<SyncActivityResultDto>> SyncCompanyAsync(
        Guid companyId,
        string? actorUsername,
        CancellationToken cancellationToken = default);
```

In `GitHubActivitySyncService.cs`: add the constructor dependency

```csharp
    private readonly IMergeTransitionService _mergeTransitions;
```

assigned in the constructor alongside `_resolver`, change the method signature to match the interface, and after the resolver call:

```csharp
        var resolution = await _resolver.ResolveAsync(companyId, cancellationToken);

        // After the pull-request upsert, so State is current. Independent of the resolver: the
        // transition reads head branches, not TaskLink rows.
        var transitions = await _mergeTransitions.ApplyAsync(companyId, actorUsername, cancellationToken);
```

and widen the returned DTO:

```csharp
        return Result<SyncActivityResultDto>.Success(new SyncActivityResultDto(
            RepositoriesSynced: synced,
            Commits: commitCount,
            Branches: branchCount,
            PullRequests: pullCount,
            LinksCreated: resolution.LinksCreated,
            TasksTransitioned: transitions.Value?.Transitioned ?? 0,
            Failures: failures));
```

In `GitHubController.SyncActivity`:

```csharp
    [Audit("Synced GitHub activity")]
    [HttpPost("activity/sync")]
    public async Task<IActionResult> SyncActivity(CancellationToken ct)
    {
        var actor = User.FindFirst(ClaimTypes.Name)?.Value;
        var result = await _activitySyncService.SyncCompanyAsync(CompanyId, actor, ct);
        return FromResult(result);
    }
```

Add `using System.Security.Claims;` to the controller if it is not already present.

- [ ] **Step 7: Run the full backend suite**

Run: `dotnet test`
Expected: PASS. Existing `GitHubActivitySyncTests` call sites need the new parameter — update them by passing `"rigon"`, and do not change any assertion.

Note: `GitHubActivitySyncTests` has a recorded cold-run flake (5 failures on a cold full-suite run, passing warm, mechanism unknown). If it fails on the first run, re-run before investigating.

- [ ] **Step 8: Commit**

```bash
git add TaskSphere.Application/Interfaces/IGitHubActivitySyncService.cs TaskSphere.Infrastructure/Services/GitHubActivitySyncService.cs TaskSphere.Domain/DataTransferObjects/GitHub/GitHubActivityDtos.cs TaskSphere/Controllers/GitHubController.cs TaskSphere/Extensions/ApplicationServices.cs TaskSphere.Tests
git commit -m "Run the transition inside the sync, carrying the syncing user as the actor"
```

---

### Task 9: The project settings endpoint

**Files:**
- Modify: `TaskSphere.Domain/DataTransferObjects/Project/ProjectDto.cs`
- Modify: `TaskSphere.Application/Interfaces/IProjectService.cs`
- Modify: `TaskSphere.Application/Services/ProjectService.cs`
- Modify: `TaskSphere/Controllers/ProjectsController.cs`
- Test: `TaskSphere.Tests/Integration/ProjectSettingsTests.cs`

**Interfaces:**
- Consumes: `Project.AutoDoneOnMerge` (Task 2).
- Produces:
  - `record UpdateProjectSettingsDto(bool AutoDoneOnMerge)`
  - `ProjectDto(int Id, string Name, string Key, bool AutoDoneOnMerge)`
  - `IProjectService.UpdateSettingsAsync(Guid companyId, int projectId, UpdateProjectSettingsDto dto, CancellationToken ct)` returning `Task<Result<ProjectDto>>`

Projects are currently immutable after creation — this endpoint is new. It is `Company`-gated, matching `Create`.

- [ ] **Step 1: Write the failing test**

Create `TaskSphere.Tests/Integration/ProjectSettingsTests.cs`. This is the plan's only HTTP-level test class: **read `GitHubActivityEndpointTests.cs` first and copy its host setup and auth helpers verbatim**, including their real names. `AuthenticatedCompanyClient()` / `AuthenticatedMemberClient()` below are stand-ins for whatever that file calls them — do not invent a second harness.

The fixture needs `_projectId` (key `TS`, in the caller's company) and `_foreignProjectId` (any project in a different company).

```csharp
    [Fact]
    public async SystemTask.Task An_admin_can_enable_auto_done_on_merge()
    {
        var client = await AuthenticatedCompanyClient();

        var response = await client.PatchAsJsonAsync(
            $"/api/Projects/{_projectId}/settings",
            new { autoDoneOnMerge = true });

        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<ProjectDto>();
        Assert.True(dto!.AutoDoneOnMerge);
    }

    [Fact]
    public async SystemTask.Task A_member_cannot_change_project_settings()
    {
        var client = await AuthenticatedMemberClient();

        var response = await client.PatchAsJsonAsync(
            $"/api/Projects/{_projectId}/settings",
            new { autoDoneOnMerge = true });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async SystemTask.Task The_settings_endpoint_cannot_change_the_project_key()
    {
        var client = await AuthenticatedCompanyClient();

        // The DTO has no Key member; an extra JSON property must be ignored, never applied.
        var response = await client.PatchAsJsonAsync(
            $"/api/Projects/{_projectId}/settings",
            new { autoDoneOnMerge = true, key = "HACKED" });

        response.EnsureSuccessStatusCode();

        var dto = await response.Content.ReadFromJsonAsync<ProjectDto>();
        // Changing Key would orphan every existing task key and break TaskKeyScanner.
        Assert.Equal("TS", dto!.Key);
    }

    [Fact]
    public async SystemTask.Task A_project_in_another_company_is_not_reachable()
    {
        var client = await AuthenticatedCompanyClient();

        var response = await client.PatchAsJsonAsync(
            $"/api/Projects/{_foreignProjectId}/settings",
            new { autoDoneOnMerge = true });

        // 400, not 404: ProjectService reports a missing project with a plain-string failure,
        // which ApiBaseController maps to BadRequest. GetById already behaves this way.
        // Matching it deliberately — do not introduce a second error contract in one endpoint.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~ProjectSettingsTests`
Expected: FAIL — 404, the route does not exist.

- [ ] **Step 3: Widen the DTOs**

In `ProjectDto.cs`:

```csharp
public record ProjectDto(int Id, string Name, string Key, bool AutoDoneOnMerge);

/// <summary>
/// Deliberately NOT a general project-update shape. Project.Key is uppercase-and-load-bearing:
/// changing it orphans every existing task key and silently breaks TaskKeyScanner.
/// </summary>
public record UpdateProjectSettingsDto(bool AutoDoneOnMerge);
```

- [ ] **Step 4: Add the service method**

In `IProjectService`:

```csharp
    Task<Result<ProjectDto>> UpdateSettingsAsync(
        Guid companyId, int projectId, UpdateProjectSettingsDto dto, CancellationToken ct = default);
```

In `ProjectService`, following the file's existing company-scoping and `Result` conventions. Note it uses **no AutoMapper** — `ProjectDto` is hand-projected everywhere — and reports a missing project with a plain-string failure:

```csharp
    public async Task<Result<ProjectDto>> UpdateSettingsAsync(
        Guid companyId, int projectId, UpdateProjectSettingsDto dto, CancellationToken ct = default)
    {
        var project = await _unitOfWork.Projects
            .GetCompanyProjects(companyId)
            .FirstOrDefaultAsync(p => p.Id == projectId, ct);

        if (project is null)
            return Result<ProjectDto>.Failure("Project not found.");

        project.AutoDoneOnMerge = dto.AutoDoneOnMerge;
        await _unitOfWork.Projects.Update(project, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<ProjectDto>.Success(
            new ProjectDto(project.Id, project.Name, project.Key, project.AutoDoneOnMerge));
    }
```

- [ ] **Step 5: Add the endpoint**

In `ProjectsController`:

```csharp
    [Audit("Changed project settings")]
    [Authorize(Roles = Roles.Company)]
    [HttpPatch("{projectId:int}/settings")]
    public async Task<IActionResult> UpdateSettings(
        int projectId, [FromBody] UpdateProjectSettingsDto dto, CancellationToken ct)
    {
        var result = await _projectService.UpdateSettingsAsync(CompanyId, projectId, dto, ct);
        return FromResult(result);
    }
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~ProjectSettingsTests`
Expected: PASS (4 tests).

- [ ] **Step 7: Update the other three construction sites**

`ProjectDto` is a positional record with **no AutoMapper profile** — it is hand-projected in four places, and widening it breaks all of them at compile time. Update each to pass the new member:

- `TaskSphere.Application/Services/ProjectService.cs:69` — `new ProjectDto(project.Id, project.Name, project.Key, project.AutoDoneOnMerge)`
- `TaskSphere.Application/Services/ProjectService.cs:79` — inside the `.Select(p => …)` projection, add `p.AutoDoneOnMerge`
- `TaskSphere.Application/Services/ProjectService.cs:92` — same
- `TaskSphere.Infrastructure/Services/AccessControlService.cs:64` — same

All four are EF `Select` projections or direct constructions, so the new column is fetched by the same query. No mapping configuration exists to update, and none should be added.

- [ ] **Step 8: Run the full backend suite**

Run: `dotnet test`
Expected: PASS. Every existing construction of `ProjectDto` needs the new positional member — the compiler will list them.

- [ ] **Step 9: Commit**

```bash
git add TaskSphere.Domain/DataTransferObjects/Project/ProjectDto.cs TaskSphere.Application TaskSphere/Controllers/ProjectsController.cs TaskSphere.Tests
git commit -m "Let an admin opt a project in, without opening a door onto the project key"
```

---

### Task 10: The client — the toggle and the count

**Files:**
- Modify: `client/src/app/core/models/projects.models.ts`
- Modify: `client/src/app/core/models/github-activity.models.ts`
- Modify: `client/src/app/company-dashboard/projects/projects.service.ts`
- Modify: `client/src/app/company-dashboard/projects/project-page.component.ts` / `.html`
- Test: `client/src/app/company-dashboard/projects/project-page.component.spec.ts`

**Interfaces:**
- Consumes: `PATCH /api/Projects/{projectId}/settings` and the widened DTOs (Task 9); `tasksTransitioned` (Task 8).
- Produces: `ProjectsService.updateSettings(projectId, autoDoneOnMerge): Observable<Project>`.

Run client tests with the Angular builder, not the Vitest CLI: `npm test`.

- [ ] **Step 1: Widen the models**

In `github-activity.models.ts`:

```typescript
export interface SyncActivityResultDto {
  repositoriesSynced: number;
  commits: number;
  branches: number;
  pullRequests: number;
  linksCreated: number;
  tasksTransitioned: number;
  failures: SyncFailureDto[];
}
```

In `projects.models.ts`, add `autoDoneOnMerge: boolean;` to the project interface.

- [ ] **Step 2: Write the failing service test**

```typescript
it('sends the toggle to the settings endpoint', () => {
  service.updateSettings(7, true).subscribe();

  const req = httpMock.expectOne(`${environment.apiUrl}/Projects/7/settings`);
  expect(req.request.method).toBe('PATCH');
  expect(req.request.body).toEqual({ autoDoneOnMerge: true });
  req.flush({ id: 7, name: 'TaskSphere', key: 'TS', autoDoneOnMerge: true });
});
```

- [ ] **Step 3: Run it and watch it fail**

Run: `npm test`
Expected: FAIL — `updateSettings` is not a function.

- [ ] **Step 4: Implement the service method**

```typescript
  updateSettings(projectId: number, autoDoneOnMerge: boolean): Observable<Project> {
    return this.http.patch<Project>(
      `${environment.apiUrl}/Projects/${projectId}/settings`,
      { autoDoneOnMerge },
    );
  }
```

- [ ] **Step 5: Run it and watch it pass**

Run: `npm test`
Expected: PASS.

- [ ] **Step 6: Write the failing component test**

Drive the checkbox **through the template**, never by calling the handler on the component instance. A test that calls the handler directly cannot witness the binding — the exact defect the 2026-08-23 sweep found as M05.

```typescript
it('calls the service when the checkbox is toggled in the template', () => {
  const spy = vi.spyOn(service, 'updateSettings').mockReturnValue(of(project));

  const checkbox: HTMLInputElement =
    fixture.nativeElement.querySelector('[data-testid="auto-done-on-merge"]');
  checkbox.click();
  fixture.detectChanges();

  expect(spy).toHaveBeenCalledWith(project.id, true);
});
```

- [ ] **Step 7: Run it and watch it fail**

Run: `npm test`
Expected: FAIL — no element matches the selector.

- [ ] **Step 8: Add the control**

In `project-page.component.html`, in the admin section:

```html
<label class="setting">
  <input
    type="checkbox"
    data-testid="auto-done-on-merge"
    [checked]="project().autoDoneOnMerge"
    (change)="onAutoDoneOnMergeChanged($event)"
  />
  Move a task to Done when a pull request on its branch is merged
</label>
```

In the component:

```typescript
  onAutoDoneOnMergeChanged(event: Event): void {
    const enabled = (event.target as HTMLInputElement).checked;
    this.projectsService
      .updateSettings(this.project().id, enabled)
      .subscribe((updated) => this.project.set(updated));
  }
```

- [ ] **Step 9: Run it and watch it pass**

Run: `npm test`
Expected: PASS.

- [ ] **Step 10: Show the count on the sync result**

Wherever the sync button renders its summary, add the transitioned count, and a test that drives it:

```typescript
it('reports how many tasks the sync moved to Done', () => {
  // ... flush a sync response with tasksTransitioned: 3
  expect(fixture.nativeElement.textContent).toContain('3 tasks moved to Done');
});
```

Assert the **pairing** — the number next to its label — not merely that "3" appears somewhere; a fixture where two numbers are both 3 makes a presence assertion vacuous.

- [ ] **Step 11: Run the client suite and build**

Run: `npm test` then `npm run build`
Expected: tests PASS at the new baseline (the 2 pre-existing `app.spec.ts` `NG0201` failures remain), build green.

- [ ] **Step 12: Commit**

```bash
git add client/src
git commit -m "Let an admin flip the toggle, and say how many tasks a sync moved"
```

---

### Task 11: The audit dashboard's first non-HTTP entry

**Files:**
- Test: `TaskSphere.Tests/Integration/MergeTransitionTests.cs` (append)
- Modify: whichever audit dashboard files the check proves are needed

**Interfaces:**
- Consumes: the audit enqueue from Task 4.
- Produces: nothing new.

Every audit entry until now came from `AuditAttribute` and carried a method, a path and a status code. This one carries none of them. The audit dashboard has one live defect on record already.

- [ ] **Step 1: Write the failing test**

```csharp
    [Fact]
    public async SystemTask.Task Writes_one_audit_entry_naming_the_task_the_pull_request_and_the_actor()
    {
        await AddPullRequest(_apiRepositoryId, 40, "TS-42/add-the-panel");

        var queue = new AuditQueue();
        await using var db = NewContext();
        await NewService(db, queue).ApplyAsync(_companyId, "rigon", default);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var entries = new List<AuditEntry>();
        await foreach (var entry in queue.ReadAllAsync(cts.Token))
        {
            entries.Add(entry);
            break;
        }

        var written = Assert.Single(entries);
        Assert.Equal("rigon", written.Username);
        Assert.Equal(_companyId, written.CompanyId);
        Assert.Contains("TS-42", written.Action);
        Assert.Contains("#40", written.Action);
        // HTTP-shaped fields are empty by design — the dashboard must tolerate this.
        Assert.Equal("", written.HttpMethod);
        Assert.Equal(0, written.StatusCode);
    }

    [Fact]
    public async SystemTask.Task Writes_no_audit_entry_when_nothing_moved()
    {
        await AddPullRequest(_apiRepositoryId, 41, "OO-9/ignore-me");

        var queue = new AuditQueue();
        await using var db = NewContext();
        await NewService(db, queue).ApplyAsync(_companyId, "rigon", default);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var count = 0;
        try
        {
            await foreach (var _ in queue.ReadAllAsync(cts.Token))
                count++;
        }
        catch (OperationCanceledException)
        {
        }

        Assert.Equal(0, count);
    }
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test --filter FullyQualifiedName~MergeTransitionTests`
Expected: PASS (20 tests).

- [ ] **Step 3: Check the dashboard renders the entry**

Start the app (`dotnet run --project TaskSphere/TaskSphere.csproj` and `npm start`), trigger a sync that transitions a task, and open the audit dashboard.

Confirm the row renders with blank method, path, IP, user agent and a zero status code, **without throwing and without breaking the table layout**. If it throws or renders unusably, fix the dashboard here — a null-safe render or an explicit "—" placeholder — and add a client test covering an entry with those fields empty.

- [ ] **Step 4: Commit**

```bash
git add TaskSphere.Tests/Integration/MergeTransitionTests.cs
git commit -m "Audit the transition, and make the dashboard survive an entry with no request"
```

---

### Task 12: Independent mutation sweep

**Files:** none — measurement only.

- [ ] **Step 1: Verify the baseline yourself**

Run `dotnet test` and `npm test` and record the exact counts. Do not quote a number from any agent's summary.

- [ ] **Step 2: Dispatch the sweep**

Dispatch a measurer distinct from whoever implemented the feature, per established practice on this branch. Targets: `MergeTransitionService.cs`, `TaskKeyResolutionMap.cs`, the settings endpoint path, and the client toggle.

Require a **checkpoint file** written as each verdict is reached, and require every mutant to be recorded there with its verdict and note.

- [ ] **Step 3: Audit the report against the checkpoint file**

Count the checkpoint file's rows yourself and compare them to the summary's claimed totals. Four agent reports on this branch have misstated their own work — false counts, false scope twice, and a false summary over a correct file. **Read the artifact, not the narrative.**

- [ ] **Step 4: Judge the survivors**

For each survivor decide: equivalent, test gap, or production defect. A survivor is evidence about the **tests**, not about the app — any claim that a survivor would break live behaviour is a hypothesis until the live run confirms it.

- [ ] **Step 5: Close the real gaps, re-mutating each new test to prove it kills**

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Close the gaps the sweep found, each new test re-mutated to prove it kills"
```

---

### Task 13: Live verification

**Files:** none — this is Rigon's, against real GitHub.

Nothing below can be confirmed by tests. Steps 1-2 of the previous feature's live task were GitHub UI actions only Rigon could take; this one needs no new App permission, because it writes nothing to GitHub.

- [ ] **Step 1: Enable the toggle** on the test project. The feature is inert until this is done — a merge before this point stamps its marker and is never revisited.
- [ ] **Step 2:** Create a branch from a task using the existing button (`TS-42/...`).
- [ ] **Step 3:** Open a pull request from that branch. Confirm the task does **not** move.
- [ ] **Step 4:** Merge the pull request.
- [ ] **Step 5:** Press **Sync**. Confirm the task moves to `Done` and the count reports it.
- [ ] **Step 6:** Press **Sync** again. Confirm nothing changes and no second audit entry appears.
- [ ] **Step 7:** Move the task back to `InProgress` by hand, sync, and confirm it **stays** `InProgress`.
- [ ] **Step 8:** Open the audit dashboard and confirm the transition entry renders.
- [ ] **Step 9:** Repeat 2-5 against a `Blocked` task; confirm it is not moved, then unblock it, sync, and confirm it is still not moved.
- [ ] **Step 10:** Confirm a project with the toggle **off** is untouched throughout.

Record the result in the vault: which of these were actually exercised and which were not. The previous feature's live run confirmed the happy path only, and the log says so explicitly rather than implying the rest passed.

---

## Notes for the executor

- **`docs/` is gitignored** (`.gitignore:487`) while four design docs are tracked from before the rule. This plan and its spec were force-added. A new doc under `docs/` needs `git add -f` or it is silently invisible.
- **Backend baseline at plan time:** 406 tests. **Client:** 109 tests, 107 passing — the 2 failures are the pre-existing `app.spec.ts` `NG0201: No provider found for ActivatedRoute` and are not yours.
- **`GitHubActivitySyncTests` has an open cold-run flake** — 5 failures on a cold full-suite run, passing warm, mechanism unknown. Re-run before investigating a failure there.
- **Stop the app before `dotnet test`.** A running app holds the database and the suite fails for reasons unrelated to your change.
