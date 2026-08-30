# Task Activity Refresh On Open — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Opening a task's Activity tab pulls that task's project's repositories fresh — branches, commits and pull requests — so the panel shows current GitHub activity without anyone pressing "Sync all repositories".

**Architecture:** Extract the commits pass out of `GitHubActivitySyncService` into a `GitHubCommitMirror` shaped like the two mirrors that already exist, then build a task-scoped `TaskActivityRefreshService` on the proven `ProjectActivityRefreshService` pattern: resolve task → project → linked repositories, skip anything inside its cooldown, refresh, run the resolver and the merge transition, stamp only on success. The endpoint lands on `TasksController` (`CompanyOrUser`), so a project member can refresh their own task's activity — something the `Company`-only Sync button has never allowed.

**Tech Stack:** .NET 10 (`net10.0`), EF Core + SQL Server, xUnit integration tests against LocalDB, Angular 21 standalone components with signals, Vitest.

**Spec:** This document. The design was settled in conversation on 2026-08-29; the "Design Decisions" section below is the spec half and the tasks argue from it. There is no separate spec file.

## Global Constraints

- Target framework is **`net10.0`**. The repo's `CLAUDE.md` claims .NET 9 and is wrong; do not follow it.
- Sync window is **30 days**, the same constant both existing paths use (`SyncWindowDays`). A commit is visible to every path or none.
- Pull-request/branch cooldown stays **60 seconds** (`PullRequestsRefreshedAtUtc`). The new commits cooldown is **5 minutes** (`CommitsRefreshedAtUtc`).
- Cooldowns are **per repository, never per task or per project** — one repository can serve many projects, and refreshing it once serves every board and every task that shows it.
- A repository is stamped **only when its pass succeeded**. A failed pass keeps the old stamp so the next open retries rather than waiting out a cooldown it never earned.
- **No new GitHub call shapes.** This slice reuses the three listings that already exist (`/branches`, `/commits`, `/pulls`). If a task makes you write a new URL, stop.
- The refresh endpoint is **not audited** — it is fired by opening a UI panel, and auditing it would flood the log. Follow `ProjectActivityRefreshEndpointTests`.
- Every new test file uses **decoy rows** so `Projects`, `GitHubRepositories`, `GitHubBranches`, `GitHubCommits` and `Tasks` do not share identity values. This defect has recurred twice; see `Knowledge/independent-tables-share-identity-seeds` in the vault.
- Webhooks are **out of scope**. See "Deferred: Option B" at the end — do not build toward it, do not add abstractions "for later".

---

## Design Decisions

**D1 — Commits are included, not just branches and pull requests.** The board refresh is deliberately PR-and-branches only because commits cost one listing per branch. But the Activity tab is mostly commits, and inherited commits are commits: excluding them means the tab still looks stale on open, which is the entire complaint. Accepted cost: one listing per branch per repository, bounded by the 5-minute cooldown.

**D2 — Blast radius is the repositories linked to the task's project.** Not "repositories this task already has links in", which is circular — a task with no links yet could never discover its first one, so a newly created branch would never appear. Not "all company repositories", which is what the Sync button does and is far too expensive per modal open.

**D3 — A second cooldown column rather than reusing `PullRequestsRefreshedAtUtc`.** The two passes differ in cost by roughly the branch count. One stamp for both would either make the cheap board refresh pay the expensive path's latency, or let a board load suppress the tab's commit refresh for 60 seconds.

**D4 — The two cooldowns are evaluated independently per repository.** A repository can be due for commits but not for pull requests, or the reverse. Branches are refreshed whenever *either* is due, because the commits pass consumes the branch list the branch pass returns.

**D5 — `LastSyncedAtUtc` starts telling the truth.** Today the panel's "Last synced" reads `GitHubInstallation.ActivitySyncedAtUtc`, which only the company-wide sync writes. Once a task refresh can update the mirror without touching that column, the label would claim data is older than it is. It becomes a stamp derived from the task's own repositories. Both halves of a feature must agree what they are naming — this is the third time that rule has come up on this branch.

**D6 — The commits pass moves to a mirror as a pure refactor.** `SyncCommitsAsync` is ~130 lines carrying the ahead-set difference and four fail-closed guards, including the `rel="next"` truncation guard added on 2026-08-28. It is extracted **verbatim**, its behaviour unchanged, so both callers share one implementation and the guards cannot drift apart.

---

## File Structure

**Created**
- `TaskSphere.Infrastructure/Services/GitHubCommitMirror.cs` — the commits pass, ahead-set computation and its four fail-closed guards, lifted from `GitHubActivitySyncService`.
- `TaskSphere.Application/Interfaces/ITaskActivityRefreshService.cs` — interface + `TaskActivityRefreshDto`.
- `TaskSphere.Infrastructure/Services/TaskActivityRefreshService.cs` — task-scoped refresh.
- `TaskSphere.Infrastructure/Migrations/<timestamp>_AddCommitsRefreshedAt.cs` — generated, not hand-written.
- `TaskSphere.Tests/Integration/TaskActivityRefreshTests.cs`
- `TaskSphere.Tests/Integration/TaskActivityRefreshModelTests.cs`
- `TaskSphere.Tests/Integration/TaskActivityRefreshEndpointTests.cs`

**Modified**
- `TaskSphere.Infrastructure/Services/GitHubActivitySyncService.cs` — `SyncCommitsAsync` deleted, replaced by a `GitHubCommitMirror` call.
- `TaskSphere.Domain/Entities/GitHubRepository.cs` — `CommitsRefreshedAtUtc`.
- `TaskSphere.Infrastructure/Services/GitHubRepositorySyncService.cs:137` — the upsert must leave the new column alone, as it already does for the old one.
- `TaskSphere.Infrastructure/Services/GitHubTaskActivityService.cs` — `LastSyncedAtUtc` derived from repository stamps.
- `TaskSphere/Controllers/TaskController.cs` — the new endpoint.
- `TaskSphere/Extensions/ApplicationServices.cs:101-107` — two registrations.
- `client/src/app/core/services/github-activity.service.ts` — `refreshForTask`.
- `client/src/app/components/tasks/task-github-activity.component.ts` — refresh-then-load on task change.

---

### Task 1: Extract the commits pass into `GitHubCommitMirror`

A pure refactor. No behaviour changes and no new tests of GitHub behaviour — the existing suite is the proof, and it must stay green at 490.

**Files:**
- Create: `TaskSphere.Infrastructure/Services/GitHubCommitMirror.cs`
- Modify: `TaskSphere.Infrastructure/Services/GitHubActivitySyncService.cs` (delete `SyncCommitsAsync`, lines ~145-367; call the mirror from the loop at ~line 99)
- Modify: `TaskSphere/Extensions/ApplicationServices.cs:105-106`
- Test: `TaskSphere.Tests/Integration/GitHubActivitySyncTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `GitHubCommitMirror.RefreshAsync(GitHubInstallation installation, int repositoryRowId, string fullName, List<string> branches, string defaultBranch, DateTime since, CancellationToken cancellationToken)` returning `Task<(int Inserted, List<SyncFailureDto> Failures)>`. Constructor is `GitHubCommitMirror(IGitHubApiClient apiClient, IUnitOfWork unitOfWork)` — the same two dependencies `GitHubBranchMirror` and `GitHubPullRequestMirror` take, in that order.

- [ ] **Step 1: Create the mirror file with the pass moved verbatim**

Move the whole of `SyncCommitsAsync`, its `CommitPayload` record, and every comment attached to them. The tuple return stays a tuple and does **not** become a `Result` — the existing XML doc explains why, and that reason is unchanged: "one listing that does not come back says nothing about the other thirty-nine".

```csharp
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using TaskSphere.Application.Interfaces;
using TaskSphere.Domain.Common;
using TaskSphere.Domain.DataTransferObjects.GitHub;
using TaskSphere.Domain.Interfaces;

using GitHubBranchCommit = TaskSphere.Domain.Entities.GitHubBranchCommit;
using GitHubInstallation = TaskSphere.Domain.Entities.GitHubInstallation;

namespace TaskSphere.Infrastructure.Services;

/// <summary>
/// The commits pass, shared by the company-wide sync and the task-scoped refresh. It lives
/// here rather than inside either caller so the ahead-set difference and its four fail-closed
/// guards cannot drift apart between the two paths.
/// </summary>
public class GitHubCommitMirror
{
    private readonly IGitHubApiClient _apiClient;
    private readonly IUnitOfWork _unitOfWork;

    public GitHubCommitMirror(IGitHubApiClient apiClient, IUnitOfWork unitOfWork)
    {
        _apiClient = apiClient;
        _unitOfWork = unitOfWork;
    }

    public async Task<(int Inserted, List<SyncFailureDto> Failures)> RefreshAsync(
        GitHubInstallation installation,
        int repositoryRowId,
        string fullName,
        List<string> branches,
        string defaultBranch,
        DateTime since,
        CancellationToken cancellationToken)
    {
        // ... the body of SyncCommitsAsync, moved unchanged, including RecordAheadAsync ...
    }
}
```

- [ ] **Step 2: Replace the call site in `GitHubActivitySyncService`**

Inject `GitHubCommitMirror _commits` alongside the two existing mirrors and swap the call:

```csharp
var (inserted, commitFailures) = await _commits.RefreshAsync(
    installation, repository.Id, repository.FullName, branchResult.Value!,
    repository.DefaultBranch, since, cancellationToken);
```

- [ ] **Step 3: Register it**

In `TaskSphere/Extensions/ApplicationServices.cs`, beside the other two:

```csharp
services.AddScoped<GitHubCommitMirror>();
```

- [ ] **Step 4: Fix the test construction sites**

Every test that builds `GitHubActivitySyncService` by hand now needs the fourth mirror. Find them, do not guess at them:

```bash
grep -rn "new GitHubActivitySyncService(" TaskSphere.Tests/
```

Each gains `new GitHubCommitMirror(api, uow)` in the position matching the constructor order you wrote in Step 1.

- [ ] **Step 5: Run the full backend suite**

Run: `dotnet test TaskSphere.Tests`
Expected: **490/490 passing**. Any failure here is the refactor, not a pre-existing condition — the suite was 490 green at `63c3fc0`.

- [ ] **Step 6: Prove the pass actually moved**

A refactor that leaves the old code in place and calls the new copy passes every test. Add this to `TaskSphere.Tests/Integration/GitHubActivitySyncTests.cs`:

```csharp
[Fact]
public void TheCommitsPass_LivesInTheMirror_NotInTheSyncService()
{
    var syncSource = File.ReadAllText(
        "../../../../TaskSphere.Infrastructure/Services/GitHubActivitySyncService.cs");
    var mirrorSource = File.ReadAllText(
        "../../../../TaskSphere.Infrastructure/Services/GitHubCommitMirror.cs");

    // The commits listing URL is the pass's fingerprint. Two copies means the guards can drift.
    Assert.DoesNotContain("/commits?sha=", syncSource);
    Assert.Contains("/commits?sha=", mirrorSource);

    // The truncation guard added on 2026-08-28 must have travelled with it.
    Assert.Contains("rel=\\\"next\\\"", mirrorSource);
}
```

Confirm the relative-path idiom against a source-scanning test that already exists in this suite (`ProjectActivityRefreshModelTests` has one) rather than assuming four `../`.

- [ ] **Step 7: Run it**

Run: `dotnet test TaskSphere.Tests --filter "FullyQualifiedName~TheCommitsPass_LivesInTheMirror"`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add TaskSphere.Infrastructure/Services/GitHubCommitMirror.cs TaskSphere.Infrastructure/Services/GitHubActivitySyncService.cs TaskSphere/Extensions/ApplicationServices.cs TaskSphere.Tests/
git commit -m "Extract the commits pass into a mirror both sync paths can share"
```

---

### Task 2: The `CommitsRefreshedAtUtc` column

**Files:**
- Modify: `TaskSphere.Domain/Entities/GitHubRepository.cs`
- Modify: `TaskSphere.Infrastructure/Services/GitHubRepositorySyncService.cs` (near line 137)
- Create: migration via CLI
- Test: `TaskSphere.Tests/Integration/TaskActivityRefreshModelTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `GitHubRepository.CommitsRefreshedAtUtc` — `DateTime?`.

- [ ] **Step 1: Write the failing test**

Modelled on `ProjectActivityRefreshModelTests`, which already pins the same two facts for the older column. Read that file first and follow its fixture exactly.

```csharp
[Fact]
public async SystemTask.Task CommitsRefreshedAtUtc_RoundTrips_AndDefaultsToNull()
{
    await using var db = NewContext();

    var reloaded = await db.GitHubRepositories.FirstAsync(r => r.Id == _repositoryId);
    Assert.Null(reloaded.CommitsRefreshedAtUtc);

    var stamp = new DateTime(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc);
    reloaded.CommitsRefreshedAtUtc = stamp;
    await db.SaveChangesAsync();

    await using var verify = NewContext();
    var again = await verify.GitHubRepositories.FirstAsync(r => r.Id == _repositoryId);
    Assert.Equal(stamp, again.CommitsRefreshedAtUtc);
}

[Fact]
public void TheRepositoryUpsert_LeavesTheCommitsStampAlone()
{
    var source = File.ReadAllText(
        "../../../../TaskSphere.Infrastructure/Services/GitHubRepositorySyncService.cs");

    // The upsert refreshes GitHub's own fields. Overwriting TaskSphere's cooldown stamps there
    // would reset every cooldown on every repository sync.
    Assert.DoesNotContain("existing.CommitsRefreshedAtUtc", source);
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dotnet test TaskSphere.Tests --filter "FullyQualifiedName~TaskActivityRefreshModelTests"`
Expected: FAIL — `CommitsRefreshedAtUtc` does not exist.

- [ ] **Step 3: Add the property**

```csharp
/// <summary>
/// TaskSphere's own column, not GitHub's — when a task-scoped refresh last pulled this
/// repository's commits. A separate stamp from PullRequestsRefreshedAtUtc because the commits
/// pass costs one listing per branch and carries a much longer cooldown.
/// The repository upsert must leave this field alone.
/// </summary>
public DateTime? CommitsRefreshedAtUtc { get; set; }
```

- [ ] **Step 4: Extend the upsert comment**

At `GitHubRepositorySyncService.cs:137` the comment names only the old column. Make it name both, so the next reader sees the rule rather than one instance of it.

- [ ] **Step 5: Generate the migration**

```bash
dotnet ef migrations add AddCommitsRefreshedAt --project TaskSphere.Infrastructure --startup-project TaskSphere
```

Open the generated file and confirm it is one nullable `AddColumn<DateTime>` with a matching `DropColumn` in `Down`. If it contains anything else, the model has drifted — surface that, do not absorb it.

- [ ] **Step 6: Apply it and run the tests**

```bash
dotnet ef database update --project TaskSphere.Infrastructure --startup-project TaskSphere
dotnet test TaskSphere.Tests --filter "FullyQualifiedName~TaskActivityRefreshModelTests"
```
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add TaskSphere.Domain/Entities/GitHubRepository.cs TaskSphere.Infrastructure/Services/GitHubRepositorySyncService.cs TaskSphere.Infrastructure/Migrations/ TaskSphere.Tests/
git commit -m "Add a commits-specific refresh stamp, separate from the pull-request one"
```

---

### Task 3: The service shell — authorization and the trivial exits

**Files:**
- Create: `TaskSphere.Application/Interfaces/ITaskActivityRefreshService.cs`
- Create: `TaskSphere.Infrastructure/Services/TaskActivityRefreshService.cs`
- Test: `TaskSphere.Tests/Integration/TaskActivityRefreshTests.cs`

**Interfaces:**
- Consumes: `GitHubCommitMirror.RefreshAsync` (Task 1), `GitHubRepository.CommitsRefreshedAtUtc` (Task 2).
- Produces:
  - `TaskActivityRefreshDto(bool Refreshed, int RepositoriesRefreshed, int TasksTransitioned, DateTime? LastSyncedAtUtc)`
  - `ITaskActivityRefreshService.RefreshAsync(Guid companyId, int taskId, string userId, bool isCompanyAdmin, string? actorUsername, CancellationToken cancellationToken = default)` returning `Task<Result<TaskActivityRefreshDto>>`
  - Constructor: `TaskActivityRefreshService(IUnitOfWork unitOfWork, IAccessControlService accessControl, GitHubBranchMirror branches, GitHubCommitMirror commits, GitHubPullRequestMirror pullRequests, IMergeTransitionService mergeTransitions, IGitHubTaskLinkResolver resolver)`

- [ ] **Step 1: Write the interface**

```csharp
using TaskSphere.Domain.Common;

namespace TaskSphere.Application.Interfaces;

/// <summary>
/// <c>Refreshed</c> is false when nothing was fetched — the task has no project, its project
/// links no repositories, or every repository was inside both cooldowns. All ordinary
/// outcomes, not errors. <c>LastSyncedAtUtc</c> is derived from the task's own repositories
/// after the run, so the panel's label matches what it is showing.
/// </summary>
public sealed record TaskActivityRefreshDto(
    bool Refreshed,
    int RepositoriesRefreshed,
    int TasksTransitioned,
    DateTime? LastSyncedAtUtc);

/// <summary>
/// Refreshes branches, commits and pull requests for the repositories linked to one task's
/// project, then runs the resolver and the merge → Done transition scoped to those
/// repositories. Fired by opening a task's Activity tab, so it is reachable by project members
/// and not only company admins — task access is what authorizes it, the same fact the activity
/// read relies on.
/// <para>
/// Unlike the project refresh this includes commits, because the Activity tab is mostly
/// commits. That costs one listing per branch, which is why the commits pass carries its own
/// five-minute cooldown while pull requests keep their sixty-second one.
/// </para>
/// </summary>
public interface ITaskActivityRefreshService
{
    Task<Result<TaskActivityRefreshDto>> RefreshAsync(
        Guid companyId,
        int taskId,
        string userId,
        bool isCompanyAdmin,
        string? actorUsername,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Write the failing tests**

Copy the fixture shape from `ProjectActivityRefreshTests` — the `FakeGitHubApiClient`, the LocalDB connection string with a **new database name** (`TaskSphereTaskRefreshTests`), the decoy rows. Read that file rather than reproducing it from memory. Extend the fake to answer `/commits` URLs, which it does not do today, and add a `_unlinkedTaskId` on a project with no repository links.

```csharp
[Fact]
public async SystemTask.Task ANonMember_IsForbidden_AndNoGitHubCallIsMade()
{
    await using var db = NewContext();
    var api = new FakeGitHubApiClient();
    var service = NewService(db, api);

    var result = await service.RefreshAsync(
        _companyId, _ts42TaskId, StrangerUserId, isCompanyAdmin: false, actorUsername: null);

    Assert.False(result.IsSuccess);
    Assert.Equal("Auth.Forbidden", result.Errors[0].Code);
    Assert.Empty(api.Calls);
}

[Fact]
public async SystemTask.Task AMissingTask_ReadsAsForbidden_ToANonMember()
{
    await using var db = NewContext();
    var api = new FakeGitHubApiClient();
    var service = NewService(db, api);

    // 999999 does not exist. A non-member must not be able to tell that apart from a task
    // they simply cannot see — the same rule GitHubTaskActivityService.GetForTaskAsync follows.
    var result = await service.RefreshAsync(
        _companyId, 999999, StrangerUserId, isCompanyAdmin: false, actorUsername: null);

    Assert.False(result.IsSuccess);
    Assert.Equal("Auth.Forbidden", result.Errors[0].Code);
    Assert.Empty(api.Calls);
}

[Fact]
public async SystemTask.Task AdminOnAMissingTask_GetsNotFound()
{
    await using var db = NewContext();
    var api = new FakeGitHubApiClient();
    var service = NewService(db, api);

    var result = await service.RefreshAsync(
        _companyId, 999999, MemberUserId, isCompanyAdmin: true, actorUsername: null);

    Assert.False(result.IsSuccess);
    Assert.Empty(api.Calls);
}

[Fact]
public async SystemTask.Task ATaskWhoseProjectLinksNoRepositories_IsQuiet()
{
    await using var db = NewContext();
    var api = new FakeGitHubApiClient();
    var service = NewService(db, api);

    var result = await service.RefreshAsync(
        _companyId, _unlinkedTaskId, MemberUserId, isCompanyAdmin: true, actorUsername: null);

    Assert.True(result.IsSuccess);
    Assert.False(result.Value!.Refreshed);
    Assert.Empty(api.Calls);
}
```

- [ ] **Step 3: Run them and watch them fail**

Run: `dotnet test TaskSphere.Tests --filter "FullyQualifiedName~TaskActivityRefreshTests"`
Expected: FAIL to compile — `TaskActivityRefreshService` does not exist.

- [ ] **Step 4: Write the shell**

```csharp
public async Task<Result<TaskActivityRefreshDto>> RefreshAsync(
    Guid companyId, int taskId, string userId, bool isCompanyAdmin,
    string? actorUsername, CancellationToken cancellationToken = default)
{
    // Before the lookup, deliberately — the same order GitHubTaskActivityService.GetForTaskAsync
    // uses, so a non-member cannot distinguish a missing task from a forbidden one.
    if (!isCompanyAdmin && !await _accessControl.CanAccessTaskAsync(companyId, userId, taskId, cancellationToken))
        return Result<TaskActivityRefreshDto>.Failure(EntityError.Forbidden);

    var task = await _unitOfWork.Tasks.GetByIdForCompanyAsync(taskId, companyId, cancellationToken);

    if (task is null)
        return Result<TaskActivityRefreshDto>.Failure(EntityError.NotFound(taskId));

    if (task.ProjectId is null)
        return Result<TaskActivityRefreshDto>.Success(new TaskActivityRefreshDto(false, 0, 0, null));

    var linkedRepositoryIds = await _unitOfWork.ProjectRepositoryLinks
        .GetByProject(companyId, task.ProjectId.Value)
        .Select(l => l.GitHubRepositoryId)
        .Distinct()
        .ToListAsync(cancellationToken);

    if (linkedRepositoryIds.Count == 0)
        return Result<TaskActivityRefreshDto>.Success(new TaskActivityRefreshDto(false, 0, 0, null));

    var installation = await _unitOfWork.GitHubInstallations.GetByCompanyAsync(companyId, cancellationToken);

    if (installation is null)
    {
        return Result<TaskActivityRefreshDto>.Failure(new Error(
            "GitHub.NotConnected",
            "This company is not connected to GitHub."));
    }

    // Task 4 filters by cooldown; Task 5 fills in the passes.
    return Result<TaskActivityRefreshDto>.Success(new TaskActivityRefreshDto(false, 0, 0, null));
}
```

Note the deliberate difference from `ProjectActivityRefreshService`: that one exits early when `AutoDoneOnMerge` is false, because a project that cannot transition anything costs nothing. **This service has no such check** — `AutoDoneOnMerge` governs the merge → Done transition, not whether a task's activity may be shown, and a project with the flag off still has commits worth displaying.

- [ ] **Step 5: Run the tests**

Run: `dotnet test TaskSphere.Tests --filter "FullyQualifiedName~TaskActivityRefreshTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add TaskSphere.Application/Interfaces/ITaskActivityRefreshService.cs TaskSphere.Infrastructure/Services/TaskActivityRefreshService.cs TaskSphere.Tests/
git commit -m "Add the task activity refresh shell, authorized by task access"
```

---

### Task 4: Two independent cooldowns

**Files:**
- Modify: `TaskSphere.Infrastructure/Services/TaskActivityRefreshService.cs`
- Test: `TaskSphere.Tests/Integration/TaskActivityRefreshTests.cs`

**Interfaces:**
- Consumes: the shell from Task 3.
- Produces: `PullCooldown` (60 s) and `CommitCooldown` (5 min); a private `record RepositoryWork(GitHubRepository Repository, bool RefreshPulls, bool RefreshCommits)`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public async SystemTask.Task ARepositoryInsideBothCooldowns_IsNotCalledAtAll()
{
    await using var seed = NewContext();
    var repository = await seed.GitHubRepositories.FirstAsync(r => r.Id == _apiRepositoryId);
    repository.PullRequestsRefreshedAtUtc = DateTime.UtcNow.AddSeconds(-5);
    repository.CommitsRefreshedAtUtc = DateTime.UtcNow.AddSeconds(-5);
    await seed.SaveChangesAsync();

    await using var db = NewContext();
    var api = new FakeGitHubApiClient();
    var service = NewService(db, api);

    var result = await service.RefreshAsync(
        _companyId, _ts42TaskId, MemberUserId, isCompanyAdmin: true, actorUsername: null);

    Assert.True(result.IsSuccess);
    Assert.False(result.Value!.Refreshed);
    Assert.Empty(api.Calls);
}

[Fact]
public async SystemTask.Task CommitsDue_ButPullsNot_CallsBranchesAndCommits_NotPulls()
{
    await using var seed = NewContext();
    var repository = await seed.GitHubRepositories.FirstAsync(r => r.Id == _apiRepositoryId);
    repository.PullRequestsRefreshedAtUtc = DateTime.UtcNow.AddSeconds(-5);   // inside 60s
    repository.CommitsRefreshedAtUtc = DateTime.UtcNow.AddMinutes(-10);       // outside 5min
    await seed.SaveChangesAsync();

    await using var db = NewContext();
    var api = new FakeGitHubApiClient();
    var service = NewService(db, api);

    await service.RefreshAsync(_companyId, _ts42TaskId, MemberUserId, true, null);

    // Branches are fetched because the commits pass consumes the branch list, not because
    // pull requests are due.
    Assert.Contains(api.Calls, c => c.Contains("/branches"));
    Assert.Contains(api.Calls, c => c.Contains("/commits?sha="));
    Assert.DoesNotContain(api.Calls, c => c.Contains("/pulls"));
}

[Fact]
public async SystemTask.Task PullsDue_ButCommitsNot_SkipsTheCommitListings()
{
    await using var seed = NewContext();
    var repository = await seed.GitHubRepositories.FirstAsync(r => r.Id == _apiRepositoryId);
    repository.PullRequestsRefreshedAtUtc = DateTime.UtcNow.AddMinutes(-2);   // outside 60s
    repository.CommitsRefreshedAtUtc = DateTime.UtcNow.AddSeconds(-30);       // inside 5min
    await seed.SaveChangesAsync();

    await using var db = NewContext();
    var api = new FakeGitHubApiClient();
    var service = NewService(db, api);

    await service.RefreshAsync(_companyId, _ts42TaskId, MemberUserId, true, null);

    Assert.Contains(api.Calls, c => c.Contains("/pulls"));
    Assert.DoesNotContain(api.Calls, c => c.Contains("/commits?sha="));
}
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test TaskSphere.Tests --filter "FullyQualifiedName~TaskActivityRefreshTests"`
Expected: the two "due" tests FAIL — no call is made at all, because Task 3's shell returns before fetching.

- [ ] **Step 3: Implement the selection**

```csharp
/// <summary>
/// Pull requests keep the project refresh's sixty seconds — and the same column, so a board
/// load and a tab open share one cooldown rather than each paying for the other.
/// </summary>
private static readonly TimeSpan PullCooldown = TimeSpan.FromSeconds(60);

/// <summary>
/// Commits cost one listing per branch, so they get their own, much longer window. Sized
/// against push → alt-tab → open the task, which is slower than merge → look at the board.
/// </summary>
private static readonly TimeSpan CommitCooldown = TimeSpan.FromMinutes(5);

private const int SyncWindowDays = 30;

private sealed record RepositoryWork(
    GitHubRepository Repository, bool RefreshPulls, bool RefreshCommits);
```

```csharp
var now = DateTime.UtcNow;
var pullCutoff = now - PullCooldown;
var commitCutoff = now - CommitCooldown;

var repositories = await _unitOfWork.GitHubRepositories
    .GetByCompany(companyId)
    .Where(r => linkedRepositoryIds.Contains(r.Id))
    .OrderBy(r => r.FullName)
    .ToListAsync(cancellationToken);

var work = repositories
    .Select(r => new RepositoryWork(
        r,
        RefreshPulls: r.PullRequestsRefreshedAtUtc == null || r.PullRequestsRefreshedAtUtc < pullCutoff,
        RefreshCommits: r.CommitsRefreshedAtUtc == null || r.CommitsRefreshedAtUtc < commitCutoff))
    .Where(w => w.RefreshPulls || w.RefreshCommits)
    .ToList();

if (work.Count == 0)
    return Result<TaskActivityRefreshDto>.Success(new TaskActivityRefreshDto(false, 0, 0, LastStamp(repositories)));
```

The flags are computed in memory rather than as a SQL `Where`: two nullable-date comparisons producing two independent decisions should not be expressed twice, once for the query and once for the flags.

`LastStamp` arrives in Task 6. Stub it as `private static DateTime? LastStamp(List<GitHubRepository> r) => null;` here so this task compiles, and finish it there.

- [ ] **Step 4: Run the tests**

Run: `dotnet test TaskSphere.Tests --filter "FullyQualifiedName~TaskActivityRefreshTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add TaskSphere.Infrastructure/Services/TaskActivityRefreshService.cs TaskSphere.Tests/
git commit -m "Evaluate the pull-request and commit cooldowns independently per repository"
```

---

### Task 5: The three passes, stamped only on success

**Files:**
- Modify: `TaskSphere.Infrastructure/Services/TaskActivityRefreshService.cs`
- Test: `TaskSphere.Tests/Integration/TaskActivityRefreshTests.cs`

**Interfaces:**
- Consumes: `RepositoryWork` (Task 4), the three mirrors.
- Produces: `RepositoriesRefreshed` populated on the DTO.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public async SystemTask.Task AFreshRepository_FetchesBranchesCommitsAndPulls_AndIsStamped()
{
    await using var db = NewContext();
    var api = new FakeGitHubApiClient();
    var service = NewService(db, api);

    var result = await service.RefreshAsync(
        _companyId, _ts42TaskId, MemberUserId, isCompanyAdmin: true, actorUsername: null);

    Assert.True(result.IsSuccess);
    Assert.True(result.Value!.Refreshed);
    Assert.Equal(1, result.Value!.RepositoriesRefreshed);

    await using var verify = NewContext();
    var repository = await verify.GitHubRepositories.FirstAsync(r => r.Id == _apiRepositoryId);
    Assert.NotNull(repository.CommitsRefreshedAtUtc);
    Assert.NotNull(repository.PullRequestsRefreshedAtUtc);
}

[Fact]
public async SystemTask.Task AFailedBranchListing_LeavesBothStampsNull_SoTheNextOpenRetries()
{
    await using var db = NewContext();
    var api = new FakeGitHubApiClient { Fail = true };
    var service = NewService(db, api);

    var result = await service.RefreshAsync(
        _companyId, _ts42TaskId, MemberUserId, isCompanyAdmin: true, actorUsername: null);

    Assert.True(result.IsSuccess);        // a repository that failed is not a failed request
    Assert.False(result.Value!.Refreshed);

    await using var verify = NewContext();
    var repository = await verify.GitHubRepositories.FirstAsync(r => r.Id == _apiRepositoryId);
    Assert.Null(repository.CommitsRefreshedAtUtc);
    Assert.Null(repository.PullRequestsRefreshedAtUtc);
}

[Fact]
public async SystemTask.Task OnlyThePassThatRan_IsStamped()
{
    await using var seed = NewContext();
    var repository = await seed.GitHubRepositories.FirstAsync(r => r.Id == _apiRepositoryId);
    repository.PullRequestsRefreshedAtUtc = DateTime.UtcNow.AddSeconds(-5);   // inside 60s
    await seed.SaveChangesAsync();
    var untouched = repository.PullRequestsRefreshedAtUtc;

    await using var db = NewContext();
    var api = new FakeGitHubApiClient();
    var service = NewService(db, api);

    await service.RefreshAsync(_companyId, _ts42TaskId, MemberUserId, true, null);

    await using var verify = NewContext();
    var after = await verify.GitHubRepositories.FirstAsync(r => r.Id == _apiRepositoryId);
    Assert.NotNull(after.CommitsRefreshedAtUtc);
    // The pull pass never ran, so its stamp must not move — otherwise a suppressed pass
    // silently extends its own cooldown and pull requests go stale for as long as anyone
    // keeps opening tasks.
    Assert.Equal(untouched, after.PullRequestsRefreshedAtUtc);
}
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test TaskSphere.Tests --filter "FullyQualifiedName~TaskActivityRefreshTests"`
Expected: FAIL — nothing is fetched or stamped yet.

- [ ] **Step 3: Implement the loop**

```csharp
var since = now.AddDays(-SyncWindowDays);
var refreshed = 0;

foreach (var (repository, refreshPulls, refreshCommits) in work)
{
    try
    {
        // Branches first and unconditionally: the commits pass consumes the list it returns,
        // and RecordAheadAsync looks up branch rows that must already exist.
        var branchResult = await _branches.RefreshAsync(
            installation, repository.Id, repository.FullName, cancellationToken);

        if (!branchResult.IsSuccess)
            continue;

        var didSomething = false;

        if (refreshCommits)
        {
            var (_, commitFailures) = await _commits.RefreshAsync(
                installation, repository.Id, repository.FullName, branchResult.Value!,
                repository.DefaultBranch, since, cancellationToken);

            // Any per-branch failure withholds the stamp: a partial pass must not buy five
            // minutes of silence for the branches that did not come back.
            if (commitFailures.Count == 0)
            {
                repository.CommitsRefreshedAtUtc = DateTime.UtcNow;
                didSomething = true;
            }
        }

        if (refreshPulls)
        {
            var pullResult = await _pullRequests.RefreshAsync(
                installation, repository.Id, repository.FullName, since, cancellationToken);

            if (pullResult.IsSuccess)
            {
                repository.PullRequestsRefreshedAtUtc = DateTime.UtcNow;
                didSomething = true;
            }
        }

        if (!didSomething)
            continue;

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
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test TaskSphere.Tests --filter "FullyQualifiedName~TaskActivityRefreshTests"`
Expected: PASS.

- [ ] **Step 5: Prove the stamp guard kills**

Change `if (commitFailures.Count == 0)` to stamp unconditionally, run `AFailedBranchListing_LeavesBothStampsNull_SoTheNextOpenRetries`, and confirm it fails. Revert. A cooldown stamp that survives a failure is the defect this guard exists for, and a test that cannot catch it is worse than none.

- [ ] **Step 6: Commit**

```bash
git add TaskSphere.Infrastructure/Services/TaskActivityRefreshService.cs TaskSphere.Tests/
git commit -m "Refresh branches, commits and pull requests, stamping only what succeeded"
```

---

### Task 6: Resolver, transition, and an honest `LastSyncedAtUtc`

**Files:**
- Modify: `TaskSphere.Infrastructure/Services/TaskActivityRefreshService.cs`
- Modify: `TaskSphere.Infrastructure/Services/GitHubTaskActivityService.cs`
- Test: `TaskSphere.Tests/Integration/TaskActivityRefreshTests.cs`

**Interfaces:**
- Consumes: the loop from Task 5.
- Produces: `TasksTransitioned` and `LastSyncedAtUtc` populated; `GitHubTaskActivityService` returning a repository-derived `LastSyncedAtUtc`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public async SystemTask.Task NothingRefreshed_DoesNotRunTheResolver()
{
    await using var seed = NewContext();
    var repository = await seed.GitHubRepositories.FirstAsync(r => r.Id == _apiRepositoryId);
    repository.PullRequestsRefreshedAtUtc = DateTime.UtcNow;
    repository.CommitsRefreshedAtUtc = DateTime.UtcNow;
    await seed.SaveChangesAsync();

    await using var db = NewContext();
    var linksBefore = await db.TaskLinks.CountAsync();
    var service = NewService(db, new FakeGitHubApiClient());

    await service.RefreshAsync(_companyId, _ts42TaskId, MemberUserId, true, null);

    // The resolver reads every commit, branch and pull request in the company. A cooldown hit
    // must stay free.
    Assert.Equal(linksBefore, await db.TaskLinks.CountAsync());
}

[Fact]
public async SystemTask.Task AMergedPullRequest_MovesTheTask_AndIsCounted()
{
    await using var db = NewContext();
    var api = new FakeGitHubApiClient();
    var service = NewService(db, api);

    var result = await service.RefreshAsync(
        _companyId, _ts42TaskId, MemberUserId, isCompanyAdmin: true, actorUsername: "rigon");

    Assert.True(result.IsSuccess);
    Assert.Equal(1, result.Value!.TasksTransitioned);
}

[Fact]
public async SystemTask.Task LastSyncedAtUtc_ComesFromTheTasksOwnRepositories()
{
    await using var db = NewContext();
    var api = new FakeGitHubApiClient();
    var service = NewService(db, api);

    var before = DateTime.UtcNow;
    var result = await service.RefreshAsync(_companyId, _ts42TaskId, MemberUserId, true, null);

    // Not the installation's ActivitySyncedAtUtc, which only the company-wide sync writes and
    // which this run deliberately leaves alone.
    Assert.NotNull(result.Value!.LastSyncedAtUtc);
    Assert.True(result.Value!.LastSyncedAtUtc >= before);

    await using var verify = NewContext();
    var installation = await verify.GitHubInstallations.FirstAsync(i => i.CompanyId == _companyId);
    Assert.Null(installation.ActivitySyncedAtUtc);
}
```

Read how `ProjectActivityRefreshTests` asserts the merge transition — the status enum's name and namespace — rather than writing an assertion on the task's status from memory.

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test TaskSphere.Tests --filter "FullyQualifiedName~TaskActivityRefreshTests"`
Expected: the transition and `LastSyncedAtUtc` tests FAIL — the DTO still carries zeros and a null.

- [ ] **Step 3: Implement the tail**

```csharp
// Skipped entirely when nothing was refreshed: the resolver reads every commit, branch and
// pull request in the company, and a cooldown hit must stay free.
if (refreshed > 0)
    await _resolver.ResolveAsync(companyId, cancellationToken);

// Scoped to this task's repositories, not company-wide: opening one task must not transition
// tasks on unrelated projects.
var transitions = await _mergeTransitions.ApplyAsync(
    companyId, actorUsername, linkedRepositoryIds, cancellationToken);

return Result<TaskActivityRefreshDto>.Success(new TaskActivityRefreshDto(
    Refreshed: refreshed > 0,
    RepositoriesRefreshed: refreshed,
    TasksTransitioned: transitions.Value?.Transitioned ?? 0,
    LastSyncedAtUtc: LastStamp(repositories)));
```

```csharp
/// <summary>
/// The OLDEST of the per-repository stamps, deliberately: the panel may only claim freshness
/// as of the least recently refreshed repository it is showing. Max() would let one
/// just-refreshed repository vouch for a stale one beside it. Null when any repository has
/// never been refreshed at all — "unknown", not "old".
/// </summary>
private static DateTime? LastStamp(List<GitHubRepository> repositories)
{
    var stamps = repositories
        .Select(r => Newer(r.CommitsRefreshedAtUtc, r.PullRequestsRefreshedAtUtc))
        .ToList();

    return stamps.Count == 0 || stamps.Any(s => s is null) ? null : stamps.Min();
}

private static DateTime? Newer(DateTime? a, DateTime? b) =>
    a is null ? b : b is null ? a : (a > b ? a : b);
```

- [ ] **Step 4: Make the read agree**

`GitHubTaskActivityService.GetForTaskAsync` sets `lastSynced` from `installation?.ActivitySyncedAtUtc`. It must use the same rule over the same repositories it already loads for `repositoryNames` — otherwise the label and the refresh's answer disagree, which is the exact shape of the 2026-08-26 resolver defect. Put `LastStamp` somewhere both call, or duplicate it with a comment pointing at the other; do not leave two different rules in the codebase.

- [ ] **Step 5: Run the whole backend suite**

Run: `dotnet test TaskSphere.Tests`
Expected: all green. `GitHubTaskActivityReadTests` has assertions written against the installation column — read each failure before touching it, and change a test only where the new rule is genuinely the intended behaviour.

- [ ] **Step 6: Commit**

```bash
git add TaskSphere.Infrastructure/Services/ TaskSphere.Tests/
git commit -m "Resolve, transition, and report freshness from the task's own repositories"
```

---

### Task 7: The endpoint

**Files:**
- Modify: `TaskSphere/Controllers/TaskController.cs`
- Modify: `TaskSphere/Extensions/ApplicationServices.cs`
- Test: `TaskSphere.Tests/Integration/TaskActivityRefreshEndpointTests.cs`

**Interfaces:**
- Consumes: `ITaskActivityRefreshService` (Task 3).
- Produces: `POST api/Tasks/{taskId:int}/github-refresh`.

- [ ] **Step 1: Write the failing tests**

Follow `ProjectActivityRefreshEndpointTests` — it pins the same facts by reflection over the action method. Read it first.

```csharp
[Fact]
public void TheRefreshEndpoint_IsNotAudited()
{
    var method = typeof(TasksController).GetMethod(nameof(TasksController.RefreshGitHubActivity));

    // Fired by opening a panel. Auditing it would bury every real action in the log.
    Assert.Null(method!.GetCustomAttribute<AuditAttribute>());
}

[Fact]
public void TheRefreshEndpoint_IsOnTheMemberReachableController()
{
    var authorize = typeof(TasksController).GetCustomAttribute<AuthorizeAttribute>();

    // The whole point of the slice: a project member can refresh their own task's activity,
    // which the Company-only Sync button has never allowed.
    Assert.Equal(Roles.CompanyOrUser, authorize!.Roles);
}

[Fact]
public void TheRefreshEndpoint_IsAPostOnTheTaskScopedRoute()
{
    var method = typeof(TasksController).GetMethod(nameof(TasksController.RefreshGitHubActivity));
    var post = method!.GetCustomAttribute<HttpPostAttribute>();

    Assert.NotNull(post);
    Assert.Equal("{taskId:int}/github-refresh", post!.Template);
}
```

- [ ] **Step 2: Run them and watch them fail**

Run: `dotnet test TaskSphere.Tests --filter "FullyQualifiedName~TaskActivityRefreshEndpointTests"`
Expected: FAIL — `RefreshGitHubActivity` does not exist.

- [ ] **Step 3: Add the endpoint**

`TasksController` does not currently import `System.Security.Claims`; add it, and inject `ITaskActivityRefreshService _taskActivityRefresh`.

```csharp
/// <summary>
/// Refreshes the branches, commits and pull requests of this task's project's repositories,
/// then applies any merge → Done transitions. Fired by opening the Activity tab, so it is
/// reachable by members: task access is what authorizes it, checked in the service. Not
/// audited — see TaskActivityRefreshEndpointTests.
/// </summary>
[HttpPost("{taskId:int}/github-refresh")]
public async Task<IActionResult> RefreshGitHubActivity(int taskId, CancellationToken ct)
{
    var result = await _taskActivityRefresh.RefreshAsync(
        CompanyId, taskId, UserId, IsCompanyAdmin, User.FindFirst(ClaimTypes.Name)?.Value, ct);

    return FromResult(result);
}
```

- [ ] **Step 4: Register the service**

```csharp
services.AddScoped<ITaskActivityRefreshService, TaskActivityRefreshService>();
```

- [ ] **Step 5: Build and run the tests**

Run: `dotnet build` then `dotnet test TaskSphere.Tests`
Expected: build clean, all green.

- [ ] **Step 6: Commit**

```bash
git add TaskSphere/Controllers/TaskController.cs TaskSphere/Extensions/ApplicationServices.cs TaskSphere.Tests/
git commit -m "Expose the task activity refresh on the member-reachable route"
```

---

### Task 8: The client calls it on open

**Files:**
- Modify: `client/src/app/core/services/github-activity.service.ts`
- Modify: `client/src/app/core/models/github-activity.models.ts`
- Modify: `client/src/app/components/tasks/task-github-activity.component.ts`
- Test: `client/src/app/components/tasks/task-github-activity.component.spec.ts`
- Test: `client/src/app/core/services/github-activity.service.spec.ts`

**Interfaces:**
- Consumes: `POST api/Tasks/{taskId}/github-refresh` (Task 7).
- Produces: `GitHubActivityService.refreshForTask(taskId: number): Observable<TaskActivityRefreshDto>`; `TaskActivityRefreshDto { refreshed: boolean; repositoriesRefreshed: number; tasksTransitioned: number; lastSyncedAtUtc: string | null }`.

- [ ] **Step 1: Write the failing service test**

```typescript
it('posts to the task-scoped refresh endpoint', () => {
  service.refreshForTask(42).subscribe();

  const req = httpMock.expectOne(`${environment.apiUrl}Tasks/42/github-refresh`);
  expect(req.request.method).toBe('POST');
  expect(req.request.body).toBeNull();
  req.flush({ refreshed: true, repositoriesRefreshed: 1, tasksTransitioned: 0, lastSyncedAtUtc: null });
});
```

- [ ] **Step 2: Write the failing component tests**

Read the existing spec file's TestBed setup and its empty-activity fixture rather than inventing them; the file already has both.

```typescript
it('refreshes before reading, so the panel shows what GitHub has now', () => {
  component.taskId = 42;
  component.ngOnChanges({ taskId: { currentValue: 42, previousValue: undefined, firstChange: true, isFirstChange: () => true } });

  const refresh = httpMock.expectOne(`${environment.apiUrl}Tasks/42/github-refresh`);
  expect(refresh.request.method).toBe('POST');
  refresh.flush({ refreshed: true, repositoriesRefreshed: 1, tasksTransitioned: 0, lastSyncedAtUtc: null });

  const read = httpMock.expectOne(`${environment.apiUrl}Tasks/42/github-activity`);
  expect(read.request.method).toBe('GET');
});

it('still reads when the refresh fails, so a GitHub outage does not blank the panel', () => {
  component.taskId = 42;
  component.ngOnChanges({ taskId: { currentValue: 42, previousValue: undefined, firstChange: true, isFirstChange: () => true } });

  httpMock.expectOne(`${environment.apiUrl}Tasks/42/github-refresh`)
    .flush({ message: 'boom' }, { status: 500, statusText: 'Server Error' });

  // The mirror still holds whatever the last successful sync wrote. Showing it beats showing
  // an error over data that exists.
  httpMock.expectOne(`${environment.apiUrl}Tasks/42/github-activity`);
});

it('tells the board when the refresh moved a task to Done', () => {
  const moved = vi.fn();
  component.tasksMoved.subscribe(moved);

  component.taskId = 42;
  component.ngOnChanges({ taskId: { currentValue: 42, previousValue: undefined, firstChange: true, isFirstChange: () => true } });

  httpMock.expectOne(`${environment.apiUrl}Tasks/42/github-refresh`)
    .flush({ refreshed: true, repositoriesRefreshed: 1, tasksTransitioned: 1, lastSyncedAtUtc: null });

  // Without this the panel says a task moved while the board behind it shows the old column
  // until a page reload — the 2026-08-25 defect, reached through a new trigger.
  expect(moved).toHaveBeenCalled();
});
```

- [ ] **Step 3: Run them and watch them fail**

Run: `npm test -- --include "**/task-github-activity.component.spec.ts"`
(The form `npm test -- task-github-activity` does **not** filter against this repo's `ng test`.)
Expected: FAIL — no refresh request is made.

- [ ] **Step 4: Add the service method**

```typescript
/**
 * Task-scoped and member-reachable, unlike sync(). Fired on open so the panel shows current
 * activity without anyone pressing the admin-only Sync button.
 */
refreshForTask(taskId: number): Observable<TaskActivityRefreshDto> {
  return this.http.post<TaskActivityRefreshDto>(
    `${environment.apiUrl}Tasks/${taskId}/github-refresh`, null
  );
}
```

- [ ] **Step 5: Wire the component**

```typescript
ngOnChanges(changes: SimpleChanges) {
  if (!changes['taskId']?.currentValue) return;

  this.data.set(null);
  this.refreshThenLoad();
}

/**
 * The refresh is best-effort: its failure must not stop the read, because the mirror still
 * holds whatever the last successful sync wrote, and showing that beats showing an error over
 * data that exists.
 */
private refreshThenLoad() {
  this.loading.set(true);

  this.activityApi
    .refreshForTask(this.taskId)
    .pipe(
      tap(result => {
        if (result.tasksTransitioned > 0)
          this.tasksMoved.emit();
      }),
      catchError(() => of(null))
    )
    .subscribe(() => this.load());
}
```

`retry()` keeps calling `load()` alone — a retry after a failed read must not spend another round of GitHub calls.

- [ ] **Step 6: Run the client suite**

Run: `npm test`
Expected: **144 total, 142 passing**. The 2 failures are the pre-existing `app.spec.ts` `NG0201 ActivatedRoute` pair and nothing else. A third failure is yours.

- [ ] **Step 7: Commit**

```bash
git add client/src/app/core/services/github-activity.service.ts client/src/app/core/models/github-activity.models.ts client/src/app/components/tasks/
git commit -m "Refresh a task's GitHub activity when its panel opens"
```

---

### Task 9: The Sync button earns its keep, or loses it

**Files:**
- Modify: `client/src/app/components/tasks/task-github-activity.component.html`
- Test: `client/src/app/components/tasks/task-github-activity.component.spec.ts`

**Interfaces:**
- Consumes: everything above.
- Produces: no new API surface.

- [ ] **Step 1: Write the failing test**

Check what hook the template already puts on that button and use it; do not add a second `data-testid` beside an existing one.

```typescript
it('keeps the company-wide Sync button for admins only', () => {
  // The on-open refresh covers this task's repositories. Sync all repositories still reaches
  // every repository in the company, which nothing else does — so it stays, for admins.
  component.isCompanyAdmin = true;
  fixture.detectChanges();

  expect(fixture.nativeElement.querySelector('[data-testid="sync-all"]')).toBeTruthy();
});

it('does not show the Sync button to a member, who no longer needs it', () => {
  component.isCompanyAdmin = false;
  fixture.detectChanges();

  expect(fixture.nativeElement.querySelector('[data-testid="sync-all"]')).toBeFalsy();
});
```

- [ ] **Step 2: Run and see where it stands**

Run: `npm test -- --include "**/task-github-activity.component.spec.ts"`
If both pass unchanged, the gating was already right — say so, delete nothing, and go to Step 4.

- [ ] **Step 3: Adjust the template only if the tests demand it**

- [ ] **Step 4: Correct the "Last synced" copy**

It now reflects the task's own repositories rather than a company-wide sync. If the template implies company scope, fix the wording. `Last synced` on its own is fine.

- [ ] **Step 5: Full verification, both suites, first-hand**

```bash
dotnet test TaskSphere.Tests
cd client && npm test
```

Expected: backend all green — record the real number rather than asserting a predicted one — and client **144/142**. Run these yourself; do not accept a count from a subagent's report.

- [ ] **Step 6: Commit**

```bash
git add client/src/app/components/tasks/
git commit -m "Keep the company-wide sync admin-only now that members can refresh their own tasks"
```

---

## Live Verification

Automated tests cannot see this working — nothing in this slice will have run against real GitHub until you do this. The last three slices each had their most valuable defect found by *using* the feature.

1. Push a commit to a branch linked to an open task **without** opening TaskSphere. Then open the task's Activity tab. The commit is there, with no button pressed.
2. Close the modal and reopen it within five minutes. In the network tab the refresh returns `refreshed: false`, and no GitHub call is made.
3. Merge a pull request whose head branch names a task, then open that task. It reaches Done, and the board behind the modal updates without a page reload.
4. Sign in as a **project member, not a company admin**, and open a task. The refresh succeeds; the Sync button is neither visible nor needed. This is the step that proves the slice's real point, and it is the one deferred since 2026-08-26.
5. Open a task whose project links a repository GitHub is failing on. The panel still renders the mirror's contents rather than an error.

---

## Deferred: Option B (webhooks), post-publish

Not built here, and nothing in this slice should anticipate it. Once the app is published and can host a public endpoint, a GitHub App webhook receiver for `push`, `pull_request` and branch `create`/`delete` inverts the direction: the mirror updates when GitHub has news, and opening a tab costs zero API calls. It needs HMAC signature verification against the webhook secret, event dedupe and out-of-order handling, and it does **not** remove the polling sync, which stays as the backfill for downtime and for the 30-day window.

When that lands, this slice's cooldowns become the fallback path rather than the primary one — the code above survives, with the windows lengthened. That is what makes it safe to build A now: it is not a detour on the way to B, it is B's degraded mode.
