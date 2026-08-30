# Project Activity Refresh Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A task whose pull request was merged reaches `Done` because someone opened the board, not because someone pressed **Sync all repositories**.

**Architecture:** The pull-request listing is extracted out of `GitHubActivitySyncService` into a unit two services share. A new `IProjectActivityRefreshService` refreshes only the repositories linked to one project, guarded by a per-repository 60-second cooldown, then runs the existing merge → Done transition with a **repository-id filter**. A new member-reachable endpoint on `ProjectsController` exposes it, and the board and backlog call it once per project load, re-reading only when it reports a transition.

**Tech Stack:** .NET 10 (`net10.0`), EF Core 9.0.10, SQL Server / LocalDB, xUnit integration tests against a real migrated database, Angular 21 + Vitest on the client.

**Spec:** `docs/superpowers/specs/2026-08-25-project-activity-refresh-design.md`

## Global Constraints

- **Target framework is `net10.0`.** The repo `CLAUDE.md` says .NET 9; it is wrong. Do not "fix" code to match it.
- **The entity `Task` collides with `System.Threading.Tasks.Task`.** Every file touching both needs `using TaskEntity = TaskSphere.Domain.Entities.Task;` and often `using SystemTask = System.Threading.Tasks;`. Follow the existing files exactly.
- **Status values come from `TaskSphere.Domain.Enums.TaskStatuses`.** Never write the literal string.
- **Partial-failure unit is one repository.** `SaveChangesAsync` per repository, never once at the end, and a failed unit must be discarded via `IUnitOfWork.DiscardPendingChanges()` — a rejected save leaves its entity tracked and poisons the next one.
- **The cooldown column is TaskSphere's own.** `GitHubRepositorySyncService` must never overwrite `PullRequestsRefreshedAtUtc`.
- **The transition filter is on repositories, never on projects.** Filtering by project either strands tasks forever or re-applies a transition a human reversed. See the spec's reasoning before changing this.
- **Test fixtures must seed different identity values per table.** Use decoy rows.
- **Every integration test class gets its own LocalDB database name.**
- **Run the backend suite with the app stopped.** `dotnet test` fails against a running app holding the database.
- **Client tests run with the Angular builder:** `npm test` from `client/`. The 2 failing `app.spec.ts` tests (`NG0201`) are pre-existing and not yours.

---

## Baseline at plan time

- Backend: **439 tests, all passing.**
- Client: **129 tests, 127 passing** (the 2 pre-existing `app.spec.ts` failures).

---

## File Structure

| File | Responsibility |
|---|---|
| `TaskSphere.Infrastructure/Services/GitHubPullRequestMirror.cs` | **New.** One GitHub call per repository; upserts pull requests into the mirror. Extracted verbatim from `GitHubActivitySyncService`. |
| `TaskSphere.Infrastructure/Services/GitHubActivitySyncService.cs` | **Modify.** Delegates the pull-request pass to the mirror; behaviour unchanged. |
| `TaskSphere.Domain/Entities/GitHubRepository.cs` | **Modify.** Gains `PullRequestsRefreshedAtUtc`. |
| `TaskSphere.Infrastructure/Migrations/*` | **New.** One migration, one column. |
| `TaskSphere.Infrastructure/Services/GitHubRepositorySyncService.cs` | **Modify.** Comment pinning the cooldown column out of the upsert. |
| `TaskSphere.Application/Interfaces/IMergeTransitionService.cs` | **Modify.** `ApplyAsync` gains an optional repository-id filter. |
| `TaskSphere.Infrastructure/Services/MergeTransitionService.cs` | **Modify.** Applies the filter. |
| `TaskSphere.Application/Interfaces/IProjectActivityRefreshService.cs` | **New.** Interface + `ProjectActivityRefreshDto`. |
| `TaskSphere.Infrastructure/Services/ProjectActivityRefreshService.cs` | **New.** The algorithm. |
| `TaskSphere/Controllers/ProjectsController.cs` | **Modify.** New `POST {projectId}/github-refresh`. |
| `TaskSphere/Extensions/ApplicationServices.cs` | **Modify.** Two DI registrations. |
| `client/src/app/core/models/projects.models.ts` | **Modify.** `ProjectActivityRefreshDto`. |
| `client/src/app/company-dashboard/projects/projects.service.ts` | **Modify.** `refreshGitHub`. |
| `client/src/app/sprints/sprints-page.component.ts` | **Modify.** Refresh on project load. |
| `client/src/app/tasks/tasks-page.component.ts` | **Modify.** Refresh on project load. |

---

### Task 1: Extract the pull-request mirror

**Files:**
- Create: `TaskSphere.Infrastructure/Services/GitHubPullRequestMirror.cs`
- Modify: `TaskSphere.Infrastructure/Services/GitHubActivitySyncService.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `GitHubPullRequestMirror` with
  `Task<Result<int>> RefreshAsync(GitHubInstallation installation, int repositoryRowId, string fullName, DateTime since, CancellationToken cancellationToken)`

This is a **behaviour-preserving refactor**. `GitHubActivitySyncTests` must pass unchanged at the end of it — do not edit that file.

- [ ] **Step 1: Create the mirror by moving the method verbatim**

Create `TaskSphere.Infrastructure/Services/GitHubPullRequestMirror.cs`. The body below is `SyncPullRequestsAsync` moved unchanged, with `_apiClient`/`_unitOfWork` becoming constructor dependencies and the three payload records moved with it.

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Enums;
using TaskSphere.Domain.Interfaces;
using TaskSphere.Application.Interfaces;

namespace TaskSphere.Infrastructure.Services;

/// <summary>
/// One GitHub call per repository, upserted into the mirror. Extracted from
/// <see cref="GitHubActivitySyncService"/> because two callers need it: the full company sync,
/// and the project-scoped refresh a board triggers.
/// <para>
/// This is the cheap half of a sync. The commits pass costs 1 + B calls for B branches; this
/// costs one, and it is the only input the merge → Done transition reads.
/// </para>
/// </summary>
public class GitHubPullRequestMirror
{
    private readonly IGitHubApiClient _apiClient;
    private readonly IUnitOfWork _unitOfWork;

    public GitHubPullRequestMirror(IGitHubApiClient apiClient, IUnitOfWork unitOfWork)
    {
        _apiClient = apiClient;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<int>> RefreshAsync(
        GitHubInstallation installation,
        int repositoryRowId,
        string fullName,
        DateTime since,
        CancellationToken cancellationToken)
    {
        var url = $"https://api.github.com/repos/{fullName}/pulls" +
                  "?state=all&sort=updated&direction=desc&per_page=100";

        var response = await _apiClient.GetAsync(installation.InstallationId, url, cancellationToken);

        if (!response.IsSuccess)
            return Result<int>.Failure(response.Errors[0]);

        List<PullRequestPayload>? payload;

        try
        {
            payload = JsonSerializer.Deserialize<List<PullRequestPayload>>(response.Value!.Body);
        }
        catch (JsonException)
        {
            return Result<int>.Failure(new Error("GitHub.SyncFailed", $"GitHub returned an unreadable pull requests response for {fullName}."));
        }

        if (payload is null)
            return Result<int>.Failure(new Error("GitHub.SyncFailed", $"GitHub returned no pull requests list for {fullName}."));

        var touched = 0;

        foreach (var pull in payload)
        {
            var updatedAt = pull.UpdatedAt ?? DateTime.UtcNow;

            if (updatedAt < since)
                break;

            var existing = await _unitOfWork.GitHubPullRequests
                .GetByNumberIncludingDeletedAsync(repositoryRowId, pull.Number, cancellationToken);

            // Merged is derived, not reported: GitHub sends state "closed" with a merged_at
            // timestamp, so branching on state alone calls every merged PR closed.
            var state = pull.MergedAt is not null
                ? PullRequestState.Merged
                : string.Equals(pull.State, "closed", StringComparison.OrdinalIgnoreCase)
                    ? PullRequestState.Closed
                    : PullRequestState.Open;

            if (existing is null)
            {
                await _unitOfWork.GitHubPullRequests.AddAsync(new GitHubPullRequest
                {
                    GitHubRepositoryId = repositoryRowId,
                    CompanyId = installation.CompanyId,
                    Number = pull.Number,
                    Title = pull.Title ?? "",
                    Body = pull.Body,
                    State = state,
                    AuthorLogin = pull.User?.Login ?? "",
                    HeadBranch = pull.Head?.Ref ?? "",
                    OpenedAtUtc = pull.CreatedAt ?? updatedAt,
                    GitHubUpdatedAtUtc = updatedAt,
                    MergedAtUtc = pull.MergedAt,
                    HtmlUrl = pull.HtmlUrl ?? "",
                }, cancellationToken);
            }
            else
            {
                // A pull request is a state machine: everything mutable is overwritten.
                existing.CompanyId = installation.CompanyId;
                existing.Title = pull.Title ?? "";
                existing.Body = pull.Body;
                existing.State = state;
                existing.AuthorLogin = pull.User?.Login ?? "";
                existing.HeadBranch = pull.Head?.Ref ?? "";
                existing.GitHubUpdatedAtUtc = updatedAt;
                existing.MergedAtUtc = pull.MergedAt;
                existing.HtmlUrl = pull.HtmlUrl ?? "";
                // MergeTransitionAppliedAtUtc is deliberately NOT overwritten: it is
                // TaskSphere's own marker, not a GitHub-sourced field, and clearing it here
                // would re-apply every merge transition on the next sync.
                existing.IsDeleted = false;
                existing.DeletedAt = null;

                await _unitOfWork.GitHubPullRequests.Update(existing, cancellationToken);
            }

            touched++;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result<int>.Success(touched);
    }

    private sealed record PullRequestPayload(
        [property: JsonPropertyName("number")] int Number,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("state")] string? State,
        [property: JsonPropertyName("user")] PullRequestUserPayload? User,
        [property: JsonPropertyName("head")] PullRequestHeadPayload? Head,
        [property: JsonPropertyName("created_at")] DateTime? CreatedAt,
        [property: JsonPropertyName("updated_at")] DateTime? UpdatedAt,
        [property: JsonPropertyName("merged_at")] DateTime? MergedAt,
        [property: JsonPropertyName("html_url")] string? HtmlUrl);

    private sealed record PullRequestUserPayload(
        [property: JsonPropertyName("login")] string? Login);

    private sealed record PullRequestHeadPayload(
        [property: JsonPropertyName("ref")] string? Ref);
}
```

- [ ] **Step 2: Delete the originals from the sync service**

In `GitHubActivitySyncService.cs`, delete the whole `private async Task<Result<int>> SyncPullRequestsAsync(...)` method and the three records `PullRequestPayload`, `PullRequestUserPayload`, `PullRequestHeadPayload`. Leave `BranchPayload`, `BranchCommitPayload` and every commit/branch record alone.

- [ ] **Step 3: Take the mirror as a dependency and delegate**

Add the field beside `_mergeTransitions`:

```csharp
    private readonly GitHubPullRequestMirror _pullRequests;
```

Add the constructor parameter after `IMergeTransitionService mergeTransitions`:

```csharp
        GitHubPullRequestMirror pullRequests)
```

and assign it: `_pullRequests = pullRequests;`

Then change the one call site inside the repository loop from

```csharp
            var pullResult = await SyncPullRequestsAsync(
                installation, repository.Id, repository.FullName, since, cancellationToken);
```

to

```csharp
            var pullResult = await _pullRequests.RefreshAsync(
                installation, repository.Id, repository.FullName, since, cancellationToken);
```

- [ ] **Step 4: Update the one test construction site**

`TaskSphere.Tests/Integration/GitHubActivitySyncTests.cs` constructs the service in its private `Sync` helper. Add the mirror — this is the only change to that file, and no assertion may change:

```csharp
        var transitions = new MergeTransitionService(uow, new AuditQueue());
        var mirror = new GitHubPullRequestMirror(api, uow);

        return await new GitHubActivitySyncService(api, uow, resolver, transitions, mirror)
            .SyncCompanyAsync(_companyId, "rigon");
```

- [ ] **Step 5: Register the mirror**

In `TaskSphere/Extensions/ApplicationServices.cs`, beside the other GitHub registrations:

```csharp
        services.AddScoped<GitHubPullRequestMirror>();
```

- [ ] **Step 6: Prove the extraction changed nothing**

Run: `dotnet test --filter FullyQualifiedName~GitHubActivitySyncTests`
Expected: PASS, **same count as before the refactor**. If any assertion had to change, the extraction is wrong — revert and redo.

Then run: `dotnet test`
Expected: PASS at 439.

- [ ] **Step 7: Commit**

```bash
git add TaskSphere.Infrastructure/Services/GitHubPullRequestMirror.cs TaskSphere.Infrastructure/Services/GitHubActivitySyncService.cs TaskSphere/Extensions/ApplicationServices.cs TaskSphere.Tests/Integration/GitHubActivitySyncTests.cs
git commit -m "Extract the pull-request mirror, the cheap half two callers need"
```

---

### Task 2: The cooldown column and its guard

**Files:**
- Modify: `TaskSphere.Domain/Entities/GitHubRepository.cs`
- Modify: `TaskSphere.Infrastructure/Services/GitHubRepositorySyncService.cs`
- Create: `TaskSphere.Infrastructure/Migrations/<timestamp>_AddPullRequestsRefreshedAt.cs`
- Test: `TaskSphere.Tests/Integration/ProjectActivityRefreshModelTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `GitHubRepository.PullRequestsRefreshedAtUtc` (`DateTime?`).

- [ ] **Step 1: Write the failing tests**

Create `TaskSphere.Tests/Integration/ProjectActivityRefreshModelTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.Enums;
using TaskSphere.Infrastructure.Data;

using Company = TaskSphere.Domain.Entities.Company;
using GitHubInstallation = TaskSphere.Domain.Entities.GitHubInstallation;
using GitHubRepository = TaskSphere.Domain.Entities.GitHubRepository;
using SystemTask = System.Threading.Tasks;

namespace TaskSphere.Tests.Integration;

public class ProjectActivityRefreshModelTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereRefreshModelTests;Trusted_Connection=True;TrustServerCertificate=True";

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
    public async SystemTask.Task A_new_repository_has_never_been_refreshed()
    {
        await using var db = NewContext();

        var company = new Company { Name = "Cooldown Co" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();

        var installation = new GitHubInstallation
        {
            InstallationId = 11001,
            CompanyId = company.Id,
            AccountLogin = "rigon-org",
            AccountType = "Organization",
            RepositorySelection = RepositorySelection.All,
        };
        db.GitHubInstallations.Add(installation);
        await db.SaveChangesAsync();

        var repository = new GitHubRepository
        {
            RepositoryId = 11101,
            GitHubInstallationId = installation.Id,
            CompanyId = company.Id,
            FullName = "rigon-org/api",
            DefaultBranch = "main",
        };
        db.GitHubRepositories.Add(repository);
        await db.SaveChangesAsync();

        var reloaded = await db.GitHubRepositories.SingleAsync(r => r.Id == repository.Id);

        Assert.Null(reloaded.PullRequestsRefreshedAtUtc);
    }

    [Fact]
    public async SystemTask.Task The_repository_upsert_does_not_list_the_cooldown_among_overwritten_fields()
    {
        // A source-level guard. That upsert overwrites every GitHub-sourced field by design;
        // this column is TaskSphere's own, and clearing it there would silently reset every
        // cooldown on every repository sync.
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "TaskSphere.Infrastructure", "Services", "GitHubRepositorySyncService.cs");

        var source = await File.ReadAllTextAsync(Path.GetFullPath(path));

        Assert.DoesNotContain("existing.PullRequestsRefreshedAtUtc", source);
    }
}
```

- [ ] **Step 2: Run to verify the first test fails**

Run: `dotnet test --filter FullyQualifiedName~ProjectActivityRefreshModelTests`
Expected: FAIL — `PullRequestsRefreshedAtUtc` does not exist (compile error).

- [ ] **Step 3: Add the property**

In `TaskSphere.Domain/Entities/GitHubRepository.cs`, after the last existing property:

```csharp
    /// <summary>
    /// TaskSphere's own column, not GitHub's — when the project-scoped refresh last pulled this
    /// repository's pull requests. Drives a per-repository cooldown, so several boards opening
    /// at once cost one GitHub call rather than one each.
    /// The repository upsert must leave this field alone.
    /// </summary>
    public DateTime? PullRequestsRefreshedAtUtc { get; set; }
```

- [ ] **Step 4: Add the explaining comment to the repository upsert**

In `GitHubRepositorySyncService.cs`, immediately after `existing.IsPrivate = payload.Private;`:

```csharp
                // PullRequestsRefreshedAtUtc is deliberately NOT overwritten: it is
                // TaskSphere's own cooldown stamp, not a GitHub-sourced field, and clearing it
                // here would make every repository sync reset every cooldown.
```

- [ ] **Step 5: Generate the migration**

```bash
dotnet ef migrations add AddPullRequestsRefreshedAt --project TaskSphere.Infrastructure --startup-project TaskSphere
```

Open the generated file and confirm it contains **exactly one** `AddColumn` — `PullRequestsRefreshedAtUtc`, nullable `datetime2`, on `GitHubRepositories`. Anything else means the model snapshot has drifted; stop and report rather than hand-editing.

- [ ] **Step 6: Run the tests**

Run: `dotnet test --filter FullyQualifiedName~ProjectActivityRefreshModelTests`
Expected: PASS (2 tests).

- [ ] **Step 7: Prove the source guard can fail**

Temporarily add `existing.PullRequestsRefreshedAtUtc = null;` to the repository upsert block, re-run, confirm `The_repository_upsert_does_not_list_the_cooldown...` FAILS **on the assertion** (not on a compile error — if the build breaks, the mutant is invalid; place it where it compiles). Remove it and re-run to green.

- [ ] **Step 8: Apply the migration and run the suite**

```bash
dotnet ef database update --project TaskSphere.Infrastructure --startup-project TaskSphere
dotnet test
```
Expected: PASS at 441.

- [ ] **Step 9: Commit**

```bash
git add TaskSphere.Domain/Entities/GitHubRepository.cs TaskSphere.Infrastructure/Services/GitHubRepositorySyncService.cs TaskSphere.Infrastructure/Migrations TaskSphere.Tests/Integration/ProjectActivityRefreshModelTests.cs
git commit -m "Add the per-repository cooldown stamp, pinned against the upsert"
```

---

### Task 3: The transition takes a repository filter

**Files:**
- Modify: `TaskSphere.Application/Interfaces/IMergeTransitionService.cs`
- Modify: `TaskSphere.Infrastructure/Services/MergeTransitionService.cs`
- Modify: `TaskSphere.Infrastructure/Services/GitHubActivitySyncService.cs` (call site)
- Test: `TaskSphere.Tests/Integration/MergeTransitionTests.cs` (append)

**Interfaces:**
- Consumes: `IMergeTransitionService.ApplyAsync` as it stands.
- Produces:
  `Task<Result<MergeTransitionResult>> ApplyAsync(Guid companyId, string? actorUsername, IReadOnlyCollection<int>? repositoryIds = null, CancellationToken cancellationToken = default)`

**Read the spec's reasoning before starting.** The filter is on repositories and not on projects because a project filter either strands tasks forever or re-applies a transition a human reversed.

- [ ] **Step 1: Write the failing tests**

Append to `MergeTransitionTests` (the class already has `_apiRepositoryId`, `_webRepositoryId`, `_ts42TaskId`, `_ts60TaskId`, `AddPullRequest`, `StatusOf`, `MarkerOf`):

```csharp
    [Fact]
    public async SystemTask.Task Ignores_a_pull_request_in_a_repository_outside_the_filter()
    {
        var pullId = await AddPullRequest(_apiRepositoryId, 50, "TS-42/add-the-panel");

        await using var db = NewContext();
        var result = await NewService(db, new AuditQueue())
            .ApplyAsync(_companyId, "rigon", new[] { _webRepositoryId }, default);

        Assert.Equal(0, result.Value!.Transitioned);
        Assert.Equal(TaskStatuses.InProgress, await StatusOf(_ts42TaskId));

        // NOT stamped: a pull request the filter skipped has not been considered, and must
        // still be eligible when a pass that covers its repository runs.
        Assert.Null(await MarkerOf(pullId));
    }

    [Fact]
    public async SystemTask.Task Transitions_a_pull_request_in_a_repository_inside_the_filter()
    {
        var pullId = await AddPullRequest(_apiRepositoryId, 51, "TS-42/add-the-panel");

        await using var db = NewContext();
        var result = await NewService(db, new AuditQueue())
            .ApplyAsync(_companyId, "rigon", new[] { _apiRepositoryId }, default);

        Assert.Equal(1, result.Value!.Transitioned);
        Assert.Equal(TaskStatuses.Done, await StatusOf(_ts42TaskId));
        Assert.NotNull(await MarkerOf(pullId));
    }

    [Fact]
    public async SystemTask.Task A_filtered_pass_still_considers_every_key_on_the_branch_it_processes()
    {
        // The invariant that made a project filter unworkable: a considered pull request is
        // considered FULLY. Both keys move, and the marker is stamped once — so no key is ever
        // left stranded behind a marker, and no pull request stays eligible to be re-applied
        // over a human's decision.
        var pullId = await AddPullRequest(_apiRepositoryId, 52, "TS-42-and-TS-60/two-at-once");

        await using var db = NewContext();
        var result = await NewService(db, new AuditQueue())
            .ApplyAsync(_companyId, "rigon", new[] { _apiRepositoryId }, default);

        Assert.Equal(2, result.Value!.Transitioned);
        Assert.Equal(TaskStatuses.Done, await StatusOf(_ts42TaskId));
        Assert.Equal(TaskStatuses.Done, await StatusOf(_ts60TaskId));
        Assert.NotNull(await MarkerOf(pullId));
    }

    [Fact]
    public async SystemTask.Task A_null_filter_still_means_the_whole_company()
    {
        await AddPullRequest(_apiRepositoryId, 53, "TS-42/add-the-panel");

        await using var db = NewContext();
        var result = await NewService(db, new AuditQueue())
            .ApplyAsync(_companyId, "rigon", null, default);

        Assert.Equal(1, result.Value!.Transitioned);
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~MergeTransitionTests`
Expected: FAIL — no overload takes a repository-id collection.

- [ ] **Step 3: Widen the interface**

In `IMergeTransitionService`:

```csharp
    /// <param name="repositoryIds">
    /// Restricts the pass to pull requests in these repositories. Null means the whole company,
    /// which is what the admin-triggered sync passes.
    /// <para>
    /// Deliberately repositories and not projects: a repository can be linked to several
    /// projects, so a head branch can name keys outside a project filter. Skipping those keys
    /// while stamping the marker strands them forever; skipping them without stamping leaves
    /// the pull request eligible, and a later pass would re-apply the transition over a human
    /// who moved the task back. Filtering on repositories keeps every considered pull request
    /// considered in full.
    /// </para>
    /// </param>
    Task<Result<MergeTransitionResult>> ApplyAsync(
        Guid companyId,
        string? actorUsername,
        IReadOnlyCollection<int>? repositoryIds = null,
        CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Apply the filter**

In `MergeTransitionService.ApplyAsync`, change the signature to match, then filter the pending query. Replace:

```csharp
        var pending = await _unitOfWork.GitHubPullRequests
            .GetByCompany(companyId)
            .Where(p => p.State == PullRequestState.Merged && p.MergeTransitionAppliedAtUtc == null)
            .Select(p => new { p.Id, p.GitHubRepositoryId, p.Number, p.HeadBranch })
            .ToListAsync(cancellationToken);
```

with:

```csharp
        var query = _unitOfWork.GitHubPullRequests
            .GetByCompany(companyId)
            .Where(p => p.State == PullRequestState.Merged && p.MergeTransitionAppliedAtUtc == null);

        if (repositoryIds is not null)
            query = query.Where(p => repositoryIds.Contains(p.GitHubRepositoryId));

        var pending = await query
            .Select(p => new { p.Id, p.GitHubRepositoryId, p.Number, p.HeadBranch })
            .ToListAsync(cancellationToken);
```

Everything below is untouched: a pull request that reaches the loop is still processed for **every** key its head branch yields, and stamped once.

- [ ] **Step 5: Update the existing call site and the existing test calls**

In `GitHubActivitySyncService`, the admin sync passes no filter:

```csharp
        var transitions = await _mergeTransitions.ApplyAsync(companyId, actorUsername, null, cancellationToken);
```

In `MergeTransitionTests`, the existing calls read `.ApplyAsync(_companyId, "rigon", default)`. That still compiles — `default` now binds to `repositoryIds` as null and the token defaults — but it reads as if the token were passed. Make every pre-existing call explicit so nobody has to work that out:

```csharp
.ApplyAsync(_companyId, "rigon", cancellationToken: default)
```

Do **not** change any assertion in those tests.

- [ ] **Step 6: Run the tests**

Run: `dotnet test --filter FullyQualifiedName~MergeTransitionTests`
Expected: PASS (24 tests).

- [ ] **Step 7: Prove the filter can fail**

Temporarily delete the two `if (repositoryIds is not null)` lines. Re-run. Expected: `Ignores_a_pull_request_in_a_repository_outside_the_filter` FAILS. Restore and re-run to green.

- [ ] **Step 8: Run the full suite and commit**

```bash
dotnet test
git add TaskSphere.Application/Interfaces/IMergeTransitionService.cs TaskSphere.Infrastructure/Services/MergeTransitionService.cs TaskSphere.Infrastructure/Services/GitHubActivitySyncService.cs TaskSphere.Tests/Integration/MergeTransitionTests.cs
git commit -m "Let a pass be scoped to repositories, so a board cannot move another project's tasks"
```

---

### Task 4: The refresh service — the happy path

**Files:**
- Create: `TaskSphere.Application/Interfaces/IProjectActivityRefreshService.cs`
- Create: `TaskSphere.Infrastructure/Services/ProjectActivityRefreshService.cs`
- Test: `TaskSphere.Tests/Integration/ProjectActivityRefreshTests.cs`

**Interfaces:**
- Consumes: `GitHubPullRequestMirror.RefreshAsync` (Task 1); `PullRequestsRefreshedAtUtc` (Task 2); `IMergeTransitionService.ApplyAsync` with the filter (Task 3).
- Produces:
  - `sealed record ProjectActivityRefreshDto(bool Refreshed, int RepositoriesRefreshed, int TasksTransitioned)`
  - `IProjectActivityRefreshService.RefreshAsync(Guid companyId, int projectId, string userId, bool isCompanyAdmin, string? actorUsername, CancellationToken cancellationToken = default)`
  - `ProjectActivityRefreshService(IUnitOfWork unitOfWork, IAccessControlService accessControl, GitHubPullRequestMirror pullRequests, IMergeTransitionService mergeTransitions)`

- [ ] **Step 1: Write the interface**

Create `TaskSphere.Application/Interfaces/IProjectActivityRefreshService.cs`:

```csharp
using TaskSphere.Domain.Common;

namespace TaskSphere.Application.Interfaces;

/// <summary>
/// <c>Refreshed</c> is false when nothing was fetched — the project opted out, or every
/// repository was inside its cooldown. Both are ordinary outcomes, not errors.
/// </summary>
public sealed record ProjectActivityRefreshDto(
    bool Refreshed,
    int RepositoriesRefreshed,
    int TasksTransitioned);

/// <summary>
/// Refreshes pull requests for one project's linked repositories, then runs the merge → Done
/// transition scoped to those repositories. Triggered by opening a board or a backlog, so it is
/// reachable by project members and not only company admins — the repository↔project link is
/// what authorizes it, the same fact create-branch-from-task relies on.
/// <para>
/// Pull requests only. Commits and branches cost 1 + B calls per repository and the transition
/// reads neither, so this stays affordable enough to run on a page load.
/// </para>
/// </summary>
public interface IProjectActivityRefreshService
{
    Task<Result<ProjectActivityRefreshDto>> RefreshAsync(
        Guid companyId,
        int projectId,
        string userId,
        bool isCompanyAdmin,
        string? actorUsername,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Write the failing test**

Create `TaskSphere.Tests/Integration/ProjectActivityRefreshTests.cs`. This fixture is reused by Tasks 5-7, so build it fully now. `FakeGitHubApiClient` below is a real fake that records its calls — it is not a mock library.

```csharp
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Audit;
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

public class ProjectActivityRefreshTests : IAsyncLifetime
{
    private const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=TaskSphereProjectRefreshTests;Trusted_Connection=True;TrustServerCertificate=True";

    private Guid _companyId;
    private int _tsProjectId;       // key "TS", AutoDoneOnMerge = true, linked to api
    private int _optOutProjectId;   // key "OO", AutoDoneOnMerge = false, linked to web
    private int _apiRepositoryId;
    private int _webRepositoryId;
    private int _ts42TaskId;

    private const string MemberUserId = "member-1";
    private const string StrangerUserId = "stranger-1";

    private static ApplicationDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new ApplicationDbContext(options);
    }

    /// <summary>
    /// Answers every pull-request listing with one merged pull request whose head branch names
    /// TS-42, and records the urls it was asked for so a test can assert that NO call was made.
    /// </summary>
    private sealed class FakeGitHubApiClient : IGitHubApiClient
    {
        public List<string> Calls { get; } = new();
        public bool Fail { get; set; }

        public SystemTask.Task<Result<GitHubResponse>> GetAsync(
            long installationId, string url, CancellationToken cancellationToken = default)
        {
            Calls.Add(url);

            if (Fail)
                return SystemTask.Task.FromResult(
                    Result<GitHubResponse>.Failure(new Error("GitHub.Failed", "GitHub returned 500.")));

            var body = JsonSerializer.Serialize(new[]
            {
                new
                {
                    number = 7,
                    title = "Add the panel",
                    body = (string?)null,
                    state = "closed",
                    user = new { login = "rigon" },
                    head = new { @ref = "TS-42/add-the-panel" },
                    created_at = DateTime.UtcNow.AddDays(-1),
                    updated_at = DateTime.UtcNow,
                    merged_at = (DateTime?)DateTime.UtcNow,
                    html_url = "https://github.com/rigon-org/api/pull/7",
                },
            });

            return SystemTask.Task.FromResult(
                Result<GitHubResponse>.Success(new GitHubResponse(body, null)));
        }
    }

    private static ProjectActivityRefreshService NewService(
        ApplicationDbContext db, FakeGitHubApiClient api)
    {
        var uow = new UnitOfWork(db);

        return new ProjectActivityRefreshService(
            uow,
            new AccessControlService(db),
            new GitHubPullRequestMirror(api, uow),
            new MergeTransitionService(uow, new AuditQueue()));
    }

    public async SystemTask.Task InitializeAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();

        var company = new Company { Name = "Refresh Co" };
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        _companyId = company.Id;

        // DECOY ROWS — Projects, Repositories, Tasks and links must not share identity values,
        // or a lookup passing the wrong entity's id resolves correctly by accident.
        db.Projects.AddRange(
            new Project { Name = "Decoy A", Key = "DECA", CompanyId = _companyId },
            new Project { Name = "Decoy B", Key = "DECB", CompanyId = _companyId },
            new Project { Name = "Decoy C", Key = "DECC", CompanyId = _companyId });
        await db.SaveChangesAsync();

        var ts = new Project { Name = "TaskSphere", Key = "TS", CompanyId = _companyId, AutoDoneOnMerge = true };
        var oo = new Project { Name = "Opted Out", Key = "OO", CompanyId = _companyId, AutoDoneOnMerge = false };
        db.Projects.AddRange(ts, oo);
        await db.SaveChangesAsync();
        _tsProjectId = ts.Id;
        _optOutProjectId = oo.Id;

        var installation = new GitHubInstallation
        {
            InstallationId = 12001,
            CompanyId = _companyId,
            AccountLogin = "rigon-org",
            AccountType = "Organization",
            RepositorySelection = RepositorySelection.All,
        };
        db.GitHubInstallations.Add(installation);
        await db.SaveChangesAsync();

        var api = new GitHubRepository
        {
            RepositoryId = 12101,
            GitHubInstallationId = installation.Id,
            CompanyId = _companyId,
            FullName = "rigon-org/api",
            DefaultBranch = "main",
        };
        var web = new GitHubRepository
        {
            RepositoryId = 12102,
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
                GitHubRepositoryId = _webRepositoryId,
                CompanyId = _companyId,
                LinkedByUserId = "rigon",
            });
        await db.SaveChangesAsync();

        db.Members.Add(new TaskSphere.Domain.Entities.Member
        {
            ProjectId = _tsProjectId,
            UserId = MemberUserId,
        });
        await db.SaveChangesAsync();

        var ts42 = new TaskEntity
        {
            Title = "Panel",
            Number = 42,
            ProjectId = _tsProjectId,
            CompanyId = _companyId,
            Status = TaskStatuses.InProgress,
        };
        db.Set<TaskEntity>().AddRange(ts42);
        await db.SaveChangesAsync();
        _ts42TaskId = ts42.Id;
    }

    public SystemTask.Task DisposeAsync() => SystemTask.Task.CompletedTask;

    private async SystemTask.Task<string> StatusOf(int taskId)
    {
        await using var db = NewContext();
        var task = await db.Set<TaskEntity>().SingleAsync(t => t.Id == taskId);
        return task.Status;
    }

    [Fact]
    public async SystemTask.Task Refreshes_the_projects_repositories_and_moves_the_merged_task()
    {
        var api = new FakeGitHubApiClient();

        await using var db = NewContext();
        var result = await NewService(db, api).RefreshAsync(
            _companyId, _tsProjectId, "rigon", isCompanyAdmin: true, "rigon", default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Refreshed);
        Assert.Equal(1, result.Value.RepositoriesRefreshed);
        Assert.Equal(1, result.Value.TasksTransitioned);
        Assert.Equal(TaskStatuses.Done, await StatusOf(_ts42TaskId));

        // Only this project's repository, and only the pull-request listing: the commits pass
        // is what makes a full sync expensive, and this feature never pays for it.
        Assert.Single(api.Calls);
        Assert.Contains("/repos/rigon-org/api/pulls", api.Calls[0]);
    }

    [Fact]
    public async SystemTask.Task Stamps_the_cooldown_on_the_repositories_it_refreshed()
    {
        var api = new FakeGitHubApiClient();

        await using (var db = NewContext())
            await NewService(db, api).RefreshAsync(
                _companyId, _tsProjectId, "rigon", isCompanyAdmin: true, "rigon", default);

        await using var check = NewContext();
        var repository = await check.GitHubRepositories.SingleAsync(r => r.Id == _apiRepositoryId);

        Assert.NotNull(repository.PullRequestsRefreshedAtUtc);
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~ProjectActivityRefreshTests`
Expected: FAIL — `ProjectActivityRefreshService` does not exist (compile error).

If any of these signatures differ from the fake above, **read the real signatures and match them** — do not change the assertions to fit a guess.

- [ ] **Step 4: Write the implementation**

Create `TaskSphere.Infrastructure/Services/ProjectActivityRefreshService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.Interfaces;

namespace TaskSphere.Infrastructure.Services;

public class ProjectActivityRefreshService : IProjectActivityRefreshService
{
    /// <summary>
    /// Per repository, not per project: one repository can be linked to several projects, and
    /// refreshing it once serves every board that shows it. Sized against merge → alt-tab →
    /// look at the board, which is often under a minute — a longer window would leave people
    /// reaching for the Sync button, which is the behaviour this exists to remove.
    /// </summary>
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The same window the full sync uses, so a pull request is visible to both paths or
    /// neither.
    /// </summary>
    private const int SyncWindowDays = 30;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAccessControlService _accessControl;
    private readonly GitHubPullRequestMirror _pullRequests;
    private readonly IMergeTransitionService _mergeTransitions;

    public ProjectActivityRefreshService(
        IUnitOfWork unitOfWork,
        IAccessControlService accessControl,
        GitHubPullRequestMirror pullRequests,
        IMergeTransitionService mergeTransitions)
    {
        _unitOfWork = unitOfWork;
        _accessControl = accessControl;
        _pullRequests = pullRequests;
        _mergeTransitions = mergeTransitions;
    }

    public async Task<Result<ProjectActivityRefreshDto>> RefreshAsync(
        Guid companyId,
        int projectId,
        string userId,
        bool isCompanyAdmin,
        string? actorUsername,
        CancellationToken cancellationToken = default)
    {
        var project = await _unitOfWork.Projects
            .GetCompanyProjects(companyId)
            .FirstOrDefaultAsync(p => p.Id == projectId, cancellationToken);

        if (project is null)
            return Result<ProjectActivityRefreshDto>.Failure("Project not found.");

        if (!isCompanyAdmin &&
            !await _accessControl.CanAccessProjectAsync(companyId, userId, projectId, cancellationToken))
        {
            return Result<ProjectActivityRefreshDto>.Failure(
                new Error("Auth.Forbidden", "You are not a member of this project."));
        }

        // Before the installation lookup on purpose: a project that cannot transition anything
        // costs nothing, and a company with no GitHub connection at all stays quiet.
        if (!project.AutoDoneOnMerge)
            return Result<ProjectActivityRefreshDto>.Success(new ProjectActivityRefreshDto(false, 0, 0));

        var installation = await _unitOfWork.GitHubInstallations
            .GetByCompanyAsync(companyId, cancellationToken);

        if (installation is null)
        {
            return Result<ProjectActivityRefreshDto>.Failure(new Error(
                "GitHub.NotConnected",
                "This company is not connected to GitHub."));
        }

        var linkedRepositoryIds = await _unitOfWork.ProjectRepositoryLinks
            .GetByProject(companyId, projectId)
            .Select(l => l.GitHubRepositoryId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (linkedRepositoryIds.Count == 0)
            return Result<ProjectActivityRefreshDto>.Success(new ProjectActivityRefreshDto(false, 0, 0));

        var cutoff = DateTime.UtcNow - Cooldown;

        var repositories = await _unitOfWork.GitHubRepositories
            .GetByCompany(companyId)
            .Where(r => linkedRepositoryIds.Contains(r.Id))
            .Where(r => r.PullRequestsRefreshedAtUtc == null || r.PullRequestsRefreshedAtUtc < cutoff)
            .OrderBy(r => r.FullName)
            .ToListAsync(cancellationToken);

        if (repositories.Count == 0)
            return Result<ProjectActivityRefreshDto>.Success(new ProjectActivityRefreshDto(false, 0, 0));

        var since = DateTime.UtcNow.AddDays(-SyncWindowDays);
        var refreshed = 0;

        foreach (var repository in repositories)
        {
            try
            {
                var result = await _pullRequests.RefreshAsync(
                    installation, repository.Id, repository.FullName, since, cancellationToken);

                // A repository that failed keeps its old stamp, so the next board load retries
                // it rather than waiting out a cooldown it never earned.
                if (!result.IsSuccess)
                    continue;

                repository.PullRequestsRefreshedAtUtc = DateTime.UtcNow;
                await _unitOfWork.GitHubRepositories.Update(repository, cancellationToken);

                // Per repository, so a later failure cannot discard earlier work.
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                refreshed++;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // A rejected save leaves its entity tracked as Modified; without this the next
                // repository's save re-sends the bad write and fails too.
                _unitOfWork.DiscardPendingChanges();
            }
        }

        var transitions = await _mergeTransitions.ApplyAsync(
            companyId, actorUsername, linkedRepositoryIds, cancellationToken);

        return Result<ProjectActivityRefreshDto>.Success(new ProjectActivityRefreshDto(
            Refreshed: refreshed > 0,
            RepositoriesRefreshed: refreshed,
            TasksTransitioned: transitions.Value?.Transitioned ?? 0));
    }
}
```

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~ProjectActivityRefreshTests`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add TaskSphere.Application/Interfaces/IProjectActivityRefreshService.cs TaskSphere.Infrastructure/Services/ProjectActivityRefreshService.cs TaskSphere.Tests/Integration/ProjectActivityRefreshTests.cs
git commit -m "Refresh one project's pull requests and move what merged"
```

---

### Task 5: The cooldown and the opt-out

**Files:**
- Test: `TaskSphere.Tests/Integration/ProjectActivityRefreshTests.cs` (append)

**Interfaces:**
- Consumes: everything from Task 4.
- Produces: nothing new — this proves behaviour already written, and fixes it if the tests fail.

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public async SystemTask.Task A_second_refresh_inside_the_window_makes_no_call()
    {
        var api = new FakeGitHubApiClient();

        await using (var first = NewContext())
            await NewService(first, api).RefreshAsync(
                _companyId, _tsProjectId, "rigon", isCompanyAdmin: true, "rigon", default);

        Assert.Single(api.Calls);

        await using var second = NewContext();
        var result = await NewService(second, api).RefreshAsync(
            _companyId, _tsProjectId, "rigon", isCompanyAdmin: true, "rigon", default);

        // The whole point of the cooldown: five people opening boards in the same minute cost
        // one call, not five.
        Assert.Single(api.Calls);
        Assert.False(result.Value!.Refreshed);
        Assert.Equal(0, result.Value.RepositoriesRefreshed);
    }

    [Fact]
    public async SystemTask.Task A_refresh_past_the_window_calls_again()
    {
        var api = new FakeGitHubApiClient();

        await using (var first = NewContext())
            await NewService(first, api).RefreshAsync(
                _companyId, _tsProjectId, "rigon", isCompanyAdmin: true, "rigon", default);

        // Age the stamp rather than waiting a minute in a test.
        await using (var age = NewContext())
        {
            var repository = await age.GitHubRepositories.SingleAsync(r => r.Id == _apiRepositoryId);
            repository.PullRequestsRefreshedAtUtc = DateTime.UtcNow.AddMinutes(-5);
            await age.SaveChangesAsync();
        }

        await using var second = NewContext();
        var result = await NewService(second, api).RefreshAsync(
            _companyId, _tsProjectId, "rigon", isCompanyAdmin: true, "rigon", default);

        Assert.Equal(2, api.Calls.Count);
        Assert.True(result.Value!.Refreshed);
    }

    [Fact]
    public async SystemTask.Task An_opted_out_project_costs_no_github_call_at_all()
    {
        var api = new FakeGitHubApiClient();

        await using var db = NewContext();
        var result = await NewService(db, api).RefreshAsync(
            _companyId, _optOutProjectId, "rigon", isCompanyAdmin: true, "rigon", default);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Refreshed);
        // Asserted on the client's call list, not just the counts: the point of skipping early
        // is that the rate limit is never spent where it cannot buy anything.
        Assert.Empty(api.Calls);
    }

    [Fact]
    public async SystemTask.Task A_project_with_no_linked_repository_costs_no_github_call()
    {
        var api = new FakeGitHubApiClient();

        int unlinkedProjectId;
        await using (var seed = NewContext())
        {
            var unlinked = new Project
            {
                Name = "Unlinked",
                Key = "UL",
                CompanyId = _companyId,
                AutoDoneOnMerge = true,
            };
            seed.Projects.Add(unlinked);
            await seed.SaveChangesAsync();
            unlinkedProjectId = unlinked.Id;
        }

        await using var db = NewContext();
        var result = await NewService(db, api).RefreshAsync(
            _companyId, unlinkedProjectId, "rigon", isCompanyAdmin: true, "rigon", default);

        // The toggle being on is not enough: with nothing linked there is nothing to fetch.
        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Refreshed);
        Assert.Empty(api.Calls);
    }
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test --filter FullyQualifiedName~ProjectActivityRefreshTests`
Expected: PASS (6 tests). If any fail, fix `ProjectActivityRefreshService` — do not weaken the test.

- [ ] **Step 3: Prove the cooldown test can fail**

Temporarily delete this line from the repository query:

```csharp
            .Where(r => r.PullRequestsRefreshedAtUtc == null || r.PullRequestsRefreshedAtUtc < cutoff)
```

Re-run. Expected: `A_second_refresh_inside_the_window_makes_no_call` FAILS. Restore and re-run to green.

- [ ] **Step 4: Commit**

```bash
git add TaskSphere.Tests/Integration/ProjectActivityRefreshTests.cs
git commit -m "Pin the cooldown and the opt-out, both asserted on calls made rather than counts returned"
```

---

### Task 6: Authorization — the dangerous direction

**Files:**
- Test: `TaskSphere.Tests/Integration/ProjectActivityRefreshTests.cs` (append)

**Interfaces:**
- Consumes: everything from Task 4.
- Produces: nothing new.

This endpoint is the first member-triggered GitHub spend in the application. The failure it must not have is a non-member refreshing — and thereby transitioning tasks in — a project they have nothing to do with.

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public async SystemTask.Task A_member_of_the_project_may_refresh_it()
    {
        var api = new FakeGitHubApiClient();

        await using var db = NewContext();
        var result = await NewService(db, api).RefreshAsync(
            _companyId, _tsProjectId, MemberUserId, isCompanyAdmin: false, MemberUserId, default);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.Refreshed);
    }

    [Fact]
    public async SystemTask.Task A_user_who_is_not_a_member_may_not()
    {
        var api = new FakeGitHubApiClient();

        await using var db = NewContext();
        var result = await NewService(db, api).RefreshAsync(
            _companyId, _tsProjectId, StrangerUserId, isCompanyAdmin: false, StrangerUserId, default);

        Assert.False(result.IsSuccess);
        Assert.Equal("Auth.Forbidden", result.Errors[0].Code);

        // And no call was made on their behalf.
        Assert.Empty(api.Calls);
    }

    [Fact]
    public async SystemTask.Task A_project_in_another_company_is_not_reachable()
    {
        var api = new FakeGitHubApiClient();

        Guid otherCompanyId;
        int foreignProjectId;

        await using (var seed = NewContext())
        {
            var other = new Company { Name = "Other Co" };
            seed.Companies.Add(other);
            await seed.SaveChangesAsync();
            otherCompanyId = other.Id;

            var foreign = new Project
            {
                Name = "Foreign",
                Key = "FR",
                CompanyId = otherCompanyId,
                AutoDoneOnMerge = true,
            };
            seed.Projects.Add(foreign);
            await seed.SaveChangesAsync();
            foreignProjectId = foreign.Id;
        }

        await using var db = NewContext();
        var result = await NewService(db, api).RefreshAsync(
            _companyId, foreignProjectId, "rigon", isCompanyAdmin: true, "rigon", default);

        Assert.False(result.IsSuccess);
        Assert.Empty(api.Calls);
    }
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test --filter FullyQualifiedName~ProjectActivityRefreshTests`
Expected: PASS (9 tests).

- [ ] **Step 3: Prove the membership gate can fail**

Temporarily change the gate to `if (false)`. Re-run. Expected: `A_user_who_is_not_a_member_may_not` FAILS. Restore and re-run to green.

If it still passes, the gate is untested and the task is not done — check that `MemberUserId` and `StrangerUserId` really differ in the fixture and that a `Member` row exists for the first.

- [ ] **Step 4: Commit**

```bash
git add TaskSphere.Tests/Integration/ProjectActivityRefreshTests.cs
git commit -m "Pin the membership gate on the first member-triggered GitHub spend"
```

---

### Task 7: Partial failure

**Files:**
- Test: `TaskSphere.Tests/Integration/ProjectActivityRefreshTests.cs` (append)

**Interfaces:**
- Consumes: everything from Task 4.
- Produces: nothing new.

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public async SystemTask.Task A_repository_whose_listing_fails_does_not_stamp_its_cooldown()
    {
        var api = new FakeGitHubApiClient { Fail = true };

        await using var db = NewContext();
        var result = await NewService(db, api).RefreshAsync(
            _companyId, _tsProjectId, "rigon", isCompanyAdmin: true, "rigon", default);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Refreshed);

        await using var check = NewContext();
        var repository = await check.GitHubRepositories.SingleAsync(r => r.Id == _apiRepositoryId);

        // Otherwise a single failure buys a minute of silence it never earned, and the next
        // board load cannot retry.
        Assert.Null(repository.PullRequestsRefreshedAtUtc);
    }

    [Fact]
    public async SystemTask.Task A_failed_listing_leaves_the_board_answerable_rather_than_throwing()
    {
        var api = new FakeGitHubApiClient { Fail = true };

        await using var db = NewContext();
        var result = await NewService(db, api).RefreshAsync(
            _companyId, _tsProjectId, "rigon", isCompanyAdmin: true, "rigon", default);

        // GitHub being down is an ordinary outcome for a background refresh, not an error the
        // caller must handle: the board renders either way.
        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.TasksTransitioned);
    }
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test --filter FullyQualifiedName~ProjectActivityRefreshTests`
Expected: PASS (11 tests).

- [ ] **Step 3: Run the full backend suite**

Run: `dotnet test`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add TaskSphere.Tests/Integration/ProjectActivityRefreshTests.cs
git commit -m "Pin that a failed listing retries next time instead of buying silence"
```

---

### Task 8: The endpoint

**Files:**
- Modify: `TaskSphere/Controllers/ProjectsController.cs`
- Modify: `TaskSphere/Extensions/ApplicationServices.cs`
- Test: `TaskSphere.Tests/Integration/ProjectActivityRefreshEndpointTests.cs`
- Test: `TaskSphere.Tests/Integration/GitHubDependencyInjectionTests.cs` (append)

**Interfaces:**
- Consumes: `IProjectActivityRefreshService` (Task 4).
- Produces: `POST /api/Projects/{projectId}/github-refresh`.

- [ ] **Step 1: Write the failing tests**

Create `TaskSphere.Tests/Integration/ProjectActivityRefreshEndpointTests.cs`. This project has **no HTTP host harness**; endpoint contracts are asserted by reflection, as in `GitHubActivityEndpointTests`.

```csharp
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskSphere.Controllers;
using TaskSphere.Domain.Enums;
using TaskSphere.Filters;

namespace TaskSphere.Tests.Integration;

/// <summary>
/// The first member-reachable GitHub spend in the app. It lives on ProjectsController rather
/// than GitHubController for the same reason the activity read lives on TasksController: the
/// Company-only gate belongs to company-wide operations, and this one is project-scoped.
/// </summary>
public class ProjectActivityRefreshEndpointTests
{
    [Fact]
    public void The_refresh_action_inherits_the_controllers_company_or_user_gate()
    {
        var controller = typeof(ProjectsController);
        var action = controller.GetMethod(nameof(ProjectsController.RefreshGitHub));

        Assert.NotNull(action);

        // Not on the action: membership is enforced in the service so the response carries
        // "Auth.Forbidden" rather than being a bare framework 403.
        Assert.Null(action!.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal(Roles.CompanyOrUser, controller.GetCustomAttribute<AuthorizeAttribute>()!.Roles);

        // The client hardcodes this route string, and nothing else connects the two.
        Assert.Equal("{projectId:int}/github-refresh", action.GetCustomAttribute<HttpPostAttribute>()!.Template);
    }

    [Fact]
    public void The_refresh_action_is_deliberately_not_audited()
    {
        var action = typeof(ProjectsController).GetMethod(nameof(ProjectsController.RefreshGitHub));

        // Every audited action is a human decision. This one fires from opening a page, and
        // auditing it would bury the merge → Done entries under one row per board visit. The
        // transitions it causes are still audited individually.
        Assert.Null(action!.GetCustomAttribute<AuditAttribute>());
    }
}
```

Append to `GitHubDependencyInjectionTests`:

```csharp
    [Fact]
    public void ProjectActivityRefreshService_resolves()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var service = scope.ServiceProvider.GetService<IProjectActivityRefreshService>();

        Assert.NotNull(service);
        Assert.IsType<TaskSphere.Infrastructure.Services.ProjectActivityRefreshService>(service);
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test --filter FullyQualifiedName~ProjectActivityRefreshEndpointTests`
Expected: FAIL — `RefreshGitHub` does not exist.

- [ ] **Step 3: Register the service**

In `ApplicationServices.cs`, beside the other GitHub registrations:

```csharp
        services.AddScoped<IProjectActivityRefreshService, ProjectActivityRefreshService>();
```

- [ ] **Step 4: Add the endpoint**

`ProjectsController` currently takes only `IProjectService`. Add the second dependency:

```csharp
    private readonly IProjectActivityRefreshService _activityRefresh;

    public ProjectsController(
        IProjectService projectService,
        IProjectActivityRefreshService activityRefresh)
    {
        _projectService = projectService;
        _activityRefresh = activityRefresh;
    }
```

and the action:

```csharp
    /// <summary>
    /// Refreshes this project's pull requests and applies any merge → Done transitions. Fired
    /// by opening a board or a backlog, so it is reachable by members: the repository↔project
    /// link is what authorizes it. Not audited — see ProjectActivityRefreshEndpointTests.
    /// </summary>
    [HttpPost("{projectId:int}/github-refresh")]
    public async Task<IActionResult> RefreshGitHub(int projectId, CancellationToken ct)
    {
        var result = await _activityRefresh.RefreshAsync(
            CompanyId, projectId, UserId, IsCompanyAdmin, User.FindFirst(ClaimTypes.Name)?.Value, ct);

        return FromResult(result);
    }
```

Add `using System.Security.Claims;` if it is not already present. `UserId` and `IsCompanyAdmin` come from `ApiBaseController` — **check their exact names there** and use whatever that base class actually exposes rather than assuming these.

- [ ] **Step 5: Run to verify they pass**

Run: `dotnet test --filter FullyQualifiedName~ProjectActivityRefresh`
Then: `dotnet test --filter FullyQualifiedName~GitHubDependencyInjectionTests`
Expected: PASS.

- [ ] **Step 6: Run the full backend suite and commit**

```bash
dotnet test
git add TaskSphere/Controllers/ProjectsController.cs TaskSphere/Extensions/ApplicationServices.cs TaskSphere.Tests
git commit -m "Expose the refresh where a member can reach it, without the Company gate"
```

---

### Task 9: The client call

**Files:**
- Modify: `client/src/app/core/models/projects.models.ts`
- Modify: `client/src/app/company-dashboard/projects/projects.service.ts`
- Test: `client/src/app/company-dashboard/projects/projects.service.spec.ts` (append)

**Interfaces:**
- Consumes: `POST /api/Projects/{projectId}/github-refresh` (Task 8).
- Produces: `ProjectsApiService.refreshGitHub(projectId: number): Observable<ProjectActivityRefreshDto>`

Run client tests with `npm test` from `client/`.

- [ ] **Step 1: Write the failing test**

Append to `projects.service.spec.ts`:

```typescript
  it('posts a refresh for one project', () => {
    const { service, http } = setup();

    service.refreshGitHub(7).subscribe();

    const req = http.expectOne(`${environment.apiUrl}Projects/7/github-refresh`);
    expect(req.request.method).toBe('POST');
    // No body: the project id is the whole request.
    expect(req.request.body).toEqual({});

    req.flush({ refreshed: true, repositoriesRefreshed: 1, tasksTransitioned: 1 });
  });
```

- [ ] **Step 2: Run it and watch it fail**

Run: `npm test`
Expected: FAIL — `refreshGitHub` is not a function.

- [ ] **Step 3: Add the model**

In `client/src/app/core/models/projects.models.ts`:

```typescript
export interface ProjectActivityRefreshDto {
  refreshed: boolean;
  repositoriesRefreshed: number;
  tasksTransitioned: number;
}
```

- [ ] **Step 4: Add the service method**

In `projects.service.ts`, importing `ProjectActivityRefreshDto` alongside the existing model imports:

```typescript
  /**
   * Refreshes this project's pull requests and applies any merge → Done transitions. Fired on
   * board and backlog load; failures are swallowed by the caller on purpose.
   */
  refreshGitHub(projectId: number): Observable<ProjectActivityRefreshDto> {
    return this.http.post<ProjectActivityRefreshDto>(`${this.base}${projectId}/github-refresh`, {});
  }
```

- [ ] **Step 5: Run it and watch it pass**

Run: `npm test`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add client/src/app/core/models/projects.models.ts client/src/app/company-dashboard/projects/projects.service.ts client/src/app/company-dashboard/projects/projects.service.spec.ts
git commit -m "Add the client call for the project refresh"
```

---

### Task 10: The board and the backlog fire it

**Files:**
- Modify: `client/src/app/sprints/sprints-page.component.ts`
- Modify: `client/src/app/tasks/tasks-page.component.ts`
- Test: `client/src/app/sprints/sprints-page.component.spec.ts` (append)
- Test: `client/src/app/tasks/tasks-page.component.spec.ts` (append)

**Interfaces:**
- Consumes: `ProjectsApiService.refreshGitHub` (Task 9).
- Produces: nothing further.

Both spec files already exist and already fake `ProjectsApiService`; **add `refreshGitHub` to those existing fakes** rather than writing new ones.

- [ ] **Step 1: Write the failing tests**

In `tasks-page.component.spec.ts`, extend the existing `ProjectsApiService` fake with
`refreshGitHub: vi.fn().mockReturnValue(of({ refreshed: true, repositoriesRefreshed: 1, tasksTransitioned: 0 }))`,
return it from `setup()`, and append:

```typescript
  it('refreshes GitHub once when the project loads', async () => {
    const { fixture, projectsApi } = setup();
    await fixture.whenStable();

    expect(projectsApi.refreshGitHub).toHaveBeenCalledWith(7);
    expect(projectsApi.refreshGitHub.mock.calls.length).toBe(1);
  });

  it('re-reads the backlog when the refresh moved something', async () => {
    const { fixture, tasksApi, projectsApi } = setup();
    projectsApi.refreshGitHub.mockReturnValue(
      of({ refreshed: true, repositoriesRefreshed: 1, tasksTransitioned: 2 }),
    );

    const readsBefore = tasksApi.getBacklog.mock.calls.length;

    fixture.componentInstance.refreshGitHubActivity();
    await fixture.whenStable();

    expect(tasksApi.getBacklog.mock.calls.length).toBe(readsBefore + 1);
  });

  it('does not re-read when the refresh moved nothing', async () => {
    const { fixture, tasksApi, projectsApi } = setup();
    projectsApi.refreshGitHub.mockReturnValue(
      of({ refreshed: true, repositoriesRefreshed: 1, tasksTransitioned: 0 }),
    );

    const readsBefore = tasksApi.getBacklog.mock.calls.length;

    fixture.componentInstance.refreshGitHubActivity();
    await fixture.whenStable();

    expect(tasksApi.getBacklog.mock.calls.length).toBe(readsBefore);
  });

  it('says nothing when the refresh fails', async () => {
    const { fixture, projectsApi } = setup();
    projectsApi.refreshGitHub.mockReturnValue(throwError(() => ({ status: 500 })));

    fixture.componentInstance.refreshGitHubActivity();
    await fixture.whenStable();
    fixture.detectChanges();

    // The user did not ask for this call. A banner about it reads as a broken board.
    expect(fixture.componentInstance.error()).toBeNull();
  });
```

Add `throwError` to the `rxjs` import.

In `sprints-page.component.spec.ts`, do the same, with `api.board` as the re-read being counted instead of `tasksApi.getBacklog`.

- [ ] **Step 2: Run them and watch them fail**

Run: `npm test`
Expected: FAIL — `refreshGitHubActivity` is not a function.

- [ ] **Step 3: Implement on the backlog page**

In `tasks-page.component.ts`, call it from the same place the project id is resolved in `ngOnInit` — immediately after `this.reloadAll();`:

```typescript
        this.refreshGitHubActivity();
```

and add the method beside `onTasksMovedBySync`:

```typescript
  /**
   * Fired once per project load. Refreshes this project's pull requests and applies any merge →
   * Done transitions, then re-reads only if something actually moved.
   *
   * Failures are swallowed deliberately: the user did not ask for this call, GitHub being slow
   * or unreachable must not delay or break a board, and the manual Sync button is still the
   * visible, diagnosable path.
   */
  refreshGitHubActivity() {
    const pid = this.projectId();
    if (!pid) return;

    this.projectsApi.refreshGitHub(pid)
      .pipe(
        tap((result) => {
          if (result?.tasksTransitioned > 0) this.refreshTasks();
        }),
        catchError(() => of(null)),
      )
      .subscribe();
  }
```

- [ ] **Step 4: Implement on the board page**

In `sprints-page.component.ts`, the same method, calling `this.loadBoard(s.id)` for the selected sprint instead of `refreshTasks()`:

```typescript
  refreshGitHubActivity() {
    const pid = this.projectId();
    if (!pid) return;

    this.projectsApi.refreshGitHub(pid)
      .pipe(
        tap((result) => {
          if (result?.tasksTransitioned > 0) {
            const s = this.selectedSprint();
            if (s) this.loadBoard(s.id);
          }
        }),
        catchError(() => of(null)),
      )
      .subscribe();
  }
```

Call it once where the project id resolves in `ngOnInit`, next to the existing project load.

- [ ] **Step 5: Run them and watch them pass**

Run: `npm test`
Expected: PASS at the new baseline; the 2 pre-existing `app.spec.ts` failures remain.

- [ ] **Step 6: Prove the re-read is load-bearing**

Temporarily change `if (result?.tasksTransitioned > 0)` to `if (false)` on the backlog page. Re-run. Expected: `re-reads the backlog when the refresh moved something` FAILS. Restore. Repeat on the board page and confirm its equivalent test fails.

- [ ] **Step 7: Build and commit**

```bash
cd client && npm run build && cd ..
git add client/src
git commit -m "Refresh GitHub when a board or backlog opens, and re-read only when it moved something"
```

---

### Task 11: Independent mutation sweep

**Files:** none — measurement only.

- [ ] **Step 1: Verify the baseline yourself**

Run `dotnet test` and `npm test` and record the exact counts. Do not quote a number from any agent's summary.

- [ ] **Step 2: Dispatch the sweep**

Dispatch a measurer distinct from whoever implemented the feature. Targets: `ProjectActivityRefreshService.cs`, the repository filter in `MergeTransitionService.cs`, `GitHubPullRequestMirror.cs`, the endpoint, and the two page components.

Require a **checkpoint file** written as each verdict is reached, with every mutant recorded, and require the report to state KILLED / SURVIVED / INVALID / NOT-APPLIED counts.

- [ ] **Step 3: Audit the report against the checkpoint file**

Count the checkpoint file's rows yourself and compare them to the summary's claimed totals. Five agent reports on this branch have misstated their own work. **Read the artifact, not the narrative.**

- [ ] **Step 4: Judge the survivors**

For each: equivalent, test gap, or production defect. A survivor is evidence about the **tests**, not about the app — any claim that it would break live behaviour is a hypothesis until the live run confirms it.

- [ ] **Step 5: Close the real gaps, re-mutating each new test to prove it kills**

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "Close the gaps the sweep found, each new test re-mutated to prove it kills"
```

---

### Task 12: Live verification

**Files:** none — this is Rigon's, against the real app and real GitHub.

No new GitHub App permission is needed: this feature writes nothing to GitHub.

- [ ] **Step 1:** With `AutoDoneOnMerge` **on**, merge a pull request on GitHub, switch to TaskSphere, open the board. The task reaches `Done` **without pressing Sync**.
- [ ] **Step 2:** Reopen the board immediately — the cooldown suppresses the call and nothing changes.
- [ ] **Step 3:** Wait past 60 seconds and reopen — a fresh call is made, and nothing moves, because the marker already applied.
- [ ] **Step 4:** Repeat step 1 as a **member rather than an admin**.
- [ ] **Step 5:** With the toggle **off**, confirm no task moves.
- [ ] **Step 6:** Move a transitioned task back to `InProgress` by hand, reopen the board, and confirm it **stays** — the human override must survive the new trigger.
- [ ] **Step 7:** Confirm the backlog behaves the same as the board.
- [ ] **Step 8:** Stop the internet (or disconnect GitHub) and confirm the board still loads with nothing on screen about it.

Record which of these were actually exercised and which were not. The previous feature's live run confirmed the happy path only, and its log says so rather than implying the rest passed.

---

## Notes for the executor

- **`docs/` is gitignored** (`.gitignore:487`); this plan and its spec were force-added. A new doc under `docs/` needs `git add -f` or it is silently invisible.
- **Baseline:** backend 439, client 129 with 127 passing.
- **`GitHubActivitySyncTests` has an open cold-run flake** — 5 failures on a cold full-suite run, passing warm, mechanism unknown. Re-run before investigating a failure there.
- **Stop the app before `dotnet test`.**
- The **manual Sync button is unchanged** and stays: it still owns commits, branches, and the whole-company case.
