# Branch → Commit Inheritance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A commit that never mentions `TS-1` appears under TS-1 because it sits ahead of the default branch on a branch linked to TS-1.

**Architecture:** `SyncCommitsAsync` already lists commits branch by branch and throws the branch away. It starts listing the default branch first, keeps its shas, and writes a `GitHubBranchCommit` row for every commit on a later branch that the default branch did not reach. A fourth resolver pass turns those rows into `TaskLink`s stamped with `ViaGitHubBranchId`. Zero new GitHub calls.

**Tech Stack:** .NET 10 (`net10.0`), EF Core + SQL Server, xUnit against LocalDB with real migrations, Angular 21 + Vitest.

**Spec:** `docs/superpowers/specs/2026-08-27-branch-commit-inheritance-design.md`

## Global Constraints

- **Inheritance means "ahead of the default branch"** — reachable from the linked branch, not reachable from `GitHubRepository.DefaultBranch`.
- **Add-only.** Nothing in this slice ever deletes or soft-deletes a `TaskLink` or a `GitHubBranchCommit`. A merged branch keeps its rows.
- **Fail closed.** If the default branch cannot be listed, write **zero** join rows for that repository. Never treat an empty default set as "everything is ahead".
- **Branch-name comparisons are `StringComparer.OrdinalIgnoreCase`.** `GitHubBranches.Name` is `SQL_Latin1_General_CP1_CI_AS`; ordinal comparison is the 2026-08-16 soft-delete defect.
- **Zero new GitHub calls.** No `GET /compare`. If a task needs a new endpoint, the design is wrong — stop and say so.
- **Company-wide sync only.** Do not touch `ProjectActivityRefreshService` or any board-triggered path.
- **The three existing `TaskLink` unique indexes do not change.**
- **Existing suite must stay green:** backend 471/471; client 140 total / 138 passing (the 2 failures are the pre-existing `app.spec.ts` `NG0201` pair — do not "fix" them).
- **Never run `dotnet test` while a subagent is working.** It produces a red suite of its own making.

---

### Task 1: Schema — join table, provenance column, migration

**Files:**
- Create: `TaskSphere.Domain/Entities/GitHubBranchCommit.cs`
- Modify: `TaskSphere.Domain/Entities/TaskLink.cs`
- Modify: `TaskSphere.Infrastructure/Data/ApplicationDbContext.cs` (DbSet near line 46; entity config after the `TaskLink` block ending ~line 433)
- Create: `TaskSphere.Infrastructure/Migrations/<timestamp>_BranchCommitInheritance.cs` (generated)
- Test: `TaskSphere.Tests/Integration/GitHubModelConfigurationTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `TaskSphere.Domain.Entities.GitHubBranchCommit` with `int Id`, `Guid CompanyId`, `int GitHubBranchId`, `GitHubBranch? Branch`, `int GitHubCommitId`, `GitHubCommit? Commit`. `TaskLink.ViaGitHubBranchId` as `int?`. `ApplicationDbContext.GitHubBranchCommits` as `DbSet<GitHubBranchCommit>`. Index name `IX_GitHubBranchCommits_BranchId_CommitId`.

- [ ] **Step 1: Write the failing test**

Append to `TaskSphere.Tests/Integration/GitHubModelConfigurationTests.cs`:

```csharp
[Fact]
public void GitHubBranchCommit_HasFilteredUniqueIndexOnBranchAndCommit()
{
    using var db = NewContext();
    var entity = db.Model.FindEntityType(typeof(TaskSphere.Domain.Entities.GitHubBranchCommit))!;

    var index = entity.GetIndexes().Single(i =>
        i.Properties.Select(p => p.Name).SequenceEqual(new[] { "GitHubBranchId", "GitHubCommitId" }));

    Assert.True(index.IsUnique);
    Assert.Equal("IX_GitHubBranchCommits_BranchId_CommitId", index.GetDatabaseName());

    // Filtered, not unfiltered: a join row is a TaskSphere-owned derived row like TaskLink,
    // not a GitHub identity like GitHubCommit. Recomputable, so nothing needs reviving — and
    // filtering it is what keeps IgnoreQueryFilters out of this whole slice.
    Assert.Equal("[IsDeleted] = 0", index.GetFilter());
}

[Fact]
public void TaskLink_ViaGitHubBranchId_IsNullableAndNotPartOfAnyUniqueIndex()
{
    using var db = NewContext();
    var entity = db.Model.FindEntityType(typeof(TaskSphere.Domain.Entities.TaskLink))!;

    var via = entity.FindProperty("ViaGitHubBranchId")!;
    Assert.True(via.IsNullable);

    // The load-bearing half. If ViaGitHubBranchId ever joins IX_TaskLinks_TaskId_CommitId,
    // a directly-named commit and an inherited one stop colliding — and precedence, which
    // the whole design rests on, silently stops existing.
    Assert.DoesNotContain(
        entity.GetIndexes().Where(i => i.IsUnique),
        i => i.Properties.Any(p => p.Name == "ViaGitHubBranchId"));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TaskSphere.Tests --filter "FullyQualifiedName~GitHubModelConfigurationTests"`
Expected: FAIL — `GitHubBranchCommit` does not compile (type not found).

- [ ] **Step 3: Write minimal implementation**

Create `TaskSphere.Domain/Entities/GitHubBranchCommit.cs`:

```csharp
namespace TaskSphere.Domain.Entities;

/// <summary>
/// A commit that was ahead of its repository's default branch on this branch <em>at the moment
/// the sync saw it</em>. A historical fact, never recomputed and never deleted: when the branch
/// merges, the commit stops being ahead but the row stays, which is what keeps an inherited
/// TaskLink alive past the merge.
/// <para>
/// Deliberately a join rather than a branch FK on <see cref="GitHubCommit"/>: a commit can be
/// ahead on two branches at once (a branch cut from another feature branch), and the commit row
/// itself must stay single and shared.
/// </para>
/// </summary>
public class GitHubBranchCommit : BaseEntity<int>
{
    public Guid CompanyId { get; set; }
    public int GitHubBranchId { get; set; }
    public GitHubBranch? Branch { get; set; }
    public int GitHubCommitId { get; set; }
    public GitHubCommit? Commit { get; set; }
}
```

In `TaskSphere.Domain/Entities/TaskLink.cs`, add below `GitHubPullRequestId` and extend the class comment:

```csharp
    public int? GitHubPullRequestId { get; set; }

    /// <summary>
    /// Null when the record named the task itself. Set when the link was inherited: the commit
    /// sits ahead of the default branch on that branch, and never mentions the task at all.
    /// <para>
    /// Provenance only — it is NOT part of any unique index. (TaskId, GitHubCommitId) stays
    /// unique, so a commit that both names the task and sits on its branch is one row, and the
    /// resolver's pass order decides that it reads as direct.
    /// </para>
    /// </summary>
    public int? ViaGitHubBranchId { get; set; }
```

In `ApplicationDbContext.cs`, add the DbSet beside `TaskLinks`:

```csharp
    public DbSet<GitHubBranchCommit> GitHubBranchCommits { get; set; }
```

Add after the `TaskLink` entity block:

```csharp
        modelBuilder.Entity<GitHubBranchCommit>(entity =>
        {
            entity.HasQueryFilter(bc => !bc.IsDeleted);

            // Filtered, following TaskLink rather than the mirror tables: this is a derived
            // TaskSphere row, not a GitHub identity, so nothing needs to be revived and no
            // lookup in this slice needs IgnoreQueryFilters.
            entity.HasIndex(bc => new { bc.GitHubBranchId, bc.GitHubCommitId })
                .IsUnique()
                .HasFilter("[IsDeleted] = 0")
                .HasDatabaseName("IX_GitHubBranchCommits_BranchId_CommitId");

            entity.HasOne(bc => bc.Branch)
                .WithMany()
                .HasForeignKey(bc => bc.GitHubBranchId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(bc => bc.Commit)
                .WithMany()
                .HasForeignKey(bc => bc.GitHubCommitId)
                .OnDelete(DeleteBehavior.Restrict);
        });
```

Inside the existing `modelBuilder.Entity<TaskLink>` block, after the three existing `HasOne` calls:

```csharp
            // A SECOND relationship to GitHubBranch on a different FK. Distinct from the
            // GitHubBranchId one above — that says "this link IS a branch", this says "this
            // link came VIA a branch".
            entity.HasOne<GitHubBranch>()
                .WithMany()
                .HasForeignKey(l => l.ViaGitHubBranchId)
                .OnDelete(DeleteBehavior.Restrict);
```

- [ ] **Step 4: Generate the migration**

```bash
dotnet ef migrations add BranchCommitInheritance --project TaskSphere.Infrastructure --startup-project TaskSphere
```

Open the generated `Up` and confirm it does exactly three things: creates `GitHubBranchCommits`, adds `ViaGitHubBranchId` to `TaskLinks`, and creates the two FK constraints plus the filtered unique index. **If it contains anything else, the model snapshot has drifted** — stop and report rather than editing the migration by hand.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test TaskSphere.Tests --filter "FullyQualifiedName~GitHubModelConfigurationTests"`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add TaskSphere.Domain/Entities/GitHubBranchCommit.cs TaskSphere.Domain/Entities/TaskLink.cs TaskSphere.Infrastructure/Data/ApplicationDbContext.cs TaskSphere.Infrastructure/Migrations TaskSphere.Tests/Integration/GitHubModelConfigurationTests.cs
git commit -m "Add GitHubBranchCommit join table and TaskLink.ViaGitHubBranchId

Ahead-ness is stored as a historical fact rather than recomputed, so an
inherited link survives the merge that ends the commit's ahead-ness."
```

---

### Task 2: Repository and wiring

**Files:**
- Create: `TaskSphere.Domain/Interfaces/IGitHubBranchCommitRepository.cs`
- Create: `TaskSphere.Infrastructure/Repositories/GitHubBranchCommitRepository.cs`
- Modify: `TaskSphere.Domain/Interfaces/IReadOnlyUnitOfWork.cs`
- Modify: `TaskSphere.Infrastructure/Repositories/UnitOfWork.cs`
- Test: `TaskSphere.Tests/Integration/GitHubActivityRepositoryTests.cs`

**Interfaces:**
- Consumes: `GitHubBranchCommit` (Task 1).
- Produces: `IGitHubBranchCommitRepository : IGenericRepository<GitHubBranchCommit, int>` with `IQueryable<GitHubBranchCommit> GetByCompany(Guid companyId)` and `Task<bool> ExistsForPairAsync(int gitHubBranchId, int gitHubCommitId, CancellationToken cancellationToken = default)`. Reached as `_unitOfWork.GitHubBranchCommits`.

- [ ] **Step 1: Write the failing test**

Append to `TaskSphere.Tests/Integration/GitHubActivityRepositoryTests.cs`:

The fixture in this file exposes `_companyId`, `_otherCompanyId` and `_repositoryId` but no branch or commit id, so each test seeds its own pair from `_repositoryId`. Deliberately local rather than new `InitializeAsync` fields: a shared fixture that later tasks silently depend on is how `ProjectActivityRefreshTests` reached ~370 lines of setup.

```csharp
private async SystemTask.Task<(int BranchId, int CommitId)> SeedBranchAndCommit(ApplicationDbContext db)
{
    var branch = new TaskSphere.Domain.Entities.GitHubBranch
    {
        CompanyId = _companyId,
        GitHubRepositoryId = _repositoryId,
        Name = "TS-42-login",
        HeadSha = "bbb",
    };

    var commit = new TaskSphere.Domain.Entities.GitHubCommit
    {
        CompanyId = _companyId,
        GitHubRepositoryId = _repositoryId,
        Sha = "ahead1",
        Message = "wire up the login form",
        AuthorName = "Rigon",
        CommittedAtUtc = DateTime.UtcNow,
        HtmlUrl = "https://github.com/rigon-org/api/commit/ahead1",
    };

    db.GitHubBranches.Add(branch);
    db.GitHubCommits.Add(commit);
    await db.SaveChangesAsync();

    return (branch.Id, commit.Id);
}

[Fact]
public async SystemTask.Task GitHubBranchCommits_ExistsForPair_IsTrueOnlyForTheExactPair()
{
    await using var db = NewContext();
    var uow = new UnitOfWork(db);
    var (branchId, commitId) = await SeedBranchAndCommit(db);

    await uow.GitHubBranchCommits.AddAsync(new TaskSphere.Domain.Entities.GitHubBranchCommit
    {
        CompanyId = _companyId,
        GitHubBranchId = branchId,
        GitHubCommitId = commitId,
    });
    await uow.SaveChangesAsync(default);

    Assert.True(await uow.GitHubBranchCommits.ExistsForPairAsync(branchId, commitId));

    // The dangerous direction: a transposed or partially-matching pair must NOT read as
    // present, or the sync stops writing rows it should write.
    Assert.False(await uow.GitHubBranchCommits.ExistsForPairAsync(commitId, branchId));
    Assert.False(await uow.GitHubBranchCommits.ExistsForPairAsync(branchId, commitId + 1));
    Assert.False(await uow.GitHubBranchCommits.ExistsForPairAsync(branchId + 1, commitId));
}

[Fact]
public async SystemTask.Task GitHubBranchCommits_GetByCompany_ExcludesOtherCompanies()
{
    await using var db = NewContext();
    var uow = new UnitOfWork(db);
    var (branchId, commitId) = await SeedBranchAndCommit(db);

    await uow.GitHubBranchCommits.AddAsync(new TaskSphere.Domain.Entities.GitHubBranchCommit
    {
        CompanyId = _companyId,
        GitHubBranchId = branchId,
        GitHubCommitId = commitId,
    });
    await uow.SaveChangesAsync(default);

    Assert.Single(await uow.GitHubBranchCommits.GetByCompany(_companyId).ToListAsync());
    Assert.Empty(await uow.GitHubBranchCommits.GetByCompany(_otherCompanyId).ToListAsync());
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TaskSphere.Tests --filter "FullyQualifiedName~GitHubActivityRepositoryTests"`
Expected: FAIL — `IUnitOfWork` has no `GitHubBranchCommits`.

- [ ] **Step 3: Write minimal implementation**

Create `TaskSphere.Domain/Interfaces/IGitHubBranchCommitRepository.cs`:

```csharp
using TaskSphere.Domain.Entities;

namespace TaskSphere.Domain.Interfaces;

public interface IGitHubBranchCommitRepository : IGenericRepository<GitHubBranchCommit, int>
{
    IQueryable<GitHubBranchCommit> GetByCompany(Guid companyId);

    /// <summary>
    /// The upsert guard for the sync's per-commit write. Named ForPair rather than overloading
    /// ExistsAsync, whose single-argument form takes a primary key — two ints in either order
    /// is a transposition waiting to happen.
    /// <para>
    /// No IgnoreQueryFilters, unlike the mirror lookups: the unique index is filtered on
    /// IsDeleted, so a soft-deleted row neither blocks a new one nor needs reviving.
    /// </para>
    /// </summary>
    Task<bool> ExistsForPairAsync(int gitHubBranchId, int gitHubCommitId, CancellationToken cancellationToken = default);
}
```

Create `TaskSphere.Infrastructure/Repositories/GitHubBranchCommitRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using TaskSphere.Domain.Entities;
using TaskSphere.Domain.Interfaces;
using TaskSphere.Infrastructure.Data;

namespace TaskSphere.Infrastructure.Repositories;

public class GitHubBranchCommitRepository : GenericRepository<GitHubBranchCommit, int>, IGitHubBranchCommitRepository
{
    private readonly ApplicationDbContext _context;

    public GitHubBranchCommitRepository(ApplicationDbContext context) : base(context)
    {
        _context = context;
    }

    public IQueryable<GitHubBranchCommit> GetByCompany(Guid companyId)
    {
        return _context.GitHubBranchCommits.Where(bc => bc.CompanyId == companyId);
    }

    public Task<bool> ExistsForPairAsync(int gitHubBranchId, int gitHubCommitId, CancellationToken cancellationToken = default)
    {
        return _context.GitHubBranchCommits
            .AnyAsync(bc => bc.GitHubBranchId == gitHubBranchId && bc.GitHubCommitId == gitHubCommitId, cancellationToken);
    }
}
```

In `IReadOnlyUnitOfWork.cs`, add after `IGitHubBranchRepository GitHubBranches { get; }`:

```csharp
    IGitHubBranchCommitRepository GitHubBranchCommits { get; }
```

In `UnitOfWork.cs`, add beside the other GitHub repositories:

```csharp
    private IGitHubBranchCommitRepository? _gitHubBranchCommits;
    public IGitHubBranchCommitRepository GitHubBranchCommits =>
        _gitHubBranchCommits ??= new GitHubBranchCommitRepository(_context);
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test TaskSphere.Tests --filter "FullyQualifiedName~GitHubActivityRepositoryTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add TaskSphere.Domain/Interfaces TaskSphere.Infrastructure/Repositories TaskSphere.Tests/Integration/GitHubActivityRepositoryTests.cs
git commit -m "Add GitHubBranchCommit repository and unit-of-work wiring"
```

---

### Task 3: Sync — list the default branch first and record ahead commits

**Files:**
- Modify: `TaskSphere.Infrastructure/Services/GitHubActivitySyncService.cs` (`SyncCompanyAsync` call site ~line 99; `SyncCommitsAsync` ~lines 158-250)
- Test: `TaskSphere.Tests/Integration/GitHubActivitySyncTests.cs`

**Interfaces:**
- Consumes: `_unitOfWork.GitHubBranchCommits.ExistsForPairAsync` (Task 2); `_unitOfWork.GitHubBranches.GetByNameIncludingDeletedAsync(int, string, CancellationToken)` (existing).
- Produces: `SyncCommitsAsync(GitHubInstallation, int repositoryRowId, string fullName, List<string> branches, string defaultBranch, DateTime since, CancellationToken)` — note the new fifth parameter, before `since`.

- [ ] **Step 1: Write the failing test**

Append to `TaskSphere.Tests/Integration/GitHubActivitySyncTests.cs`:

```csharp
[Fact]
public async SystemTask.Task DefaultBranchIsListedFirst_AndCommitsAheadOfItGetJoinRows()
{
    // "main" reaches shared1; the feature branch reaches shared1 AND ahead1. Only ahead1 is
    // ahead of default, so only ahead1 earns a join row. This is the whole feature in one case.
    var api = new FakeApiClient()
        .On("/repos/rigon-org/api/branches", Branches(("main", "aaa"), ("TS-42-fix", "bbb")))
        .On("sha=main", Commits(("shared1", "chore: bump deps", "MRigonM")))
        .On("sha=TS-42-fix", Commits(
            ("shared1", "chore: bump deps", "MRigonM"),
            ("ahead1", "wire up the login form", "MRigonM")));

    await Sync(api);

    // Ordering is behavioural and invisible in the schema: the default branch's listing has to
    // be in hand before any other branch is differenced against it.
    var commitUrls = api.RequestedUrls.Where(u => u.Contains("/commits", StringComparison.Ordinal)).ToList();
    Assert.Contains("sha=main", commitUrls[0]);

    await using var db = NewContext();
    var rows = await db.GitHubBranchCommits
        .Include(bc => bc.Branch)
        .Include(bc => bc.Commit)
        .ToListAsync();

    var row = Assert.Single(rows);
    Assert.Equal("TS-42-fix", row.Branch!.Name);
    Assert.Equal("ahead1", row.Commit!.Sha);
    Assert.Equal(_companyId, row.CompanyId);
}

[Fact]
public async SystemTask.Task CommitsReachableFromTheDefaultBranch_GetNoJoinRow()
{
    // The trap the definition exists to avoid: a branch cut from main reaches all of main's
    // history. If this fails, a task inherits every commit the company made in 30 days.
    var api = new FakeApiClient()
        .On("/repos/rigon-org/api/branches", Branches(("main", "aaa"), ("TS-42-fix", "bbb")))
        .On("sha=main", Commits(
            ("old1", "feat: something unrelated", "MRigonM"),
            ("old2", "fix: also unrelated", "MRigonM")))
        .On("sha=TS-42-fix", Commits(
            ("old1", "feat: something unrelated", "MRigonM"),
            ("old2", "fix: also unrelated", "MRigonM")));

    await Sync(api);

    await using var db = NewContext();
    Assert.Empty(await db.GitHubBranchCommits.ToListAsync());
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TaskSphere.Tests --filter "FullyQualifiedName~GitHubActivitySyncTests.DefaultBranchIsListedFirst_AndCommitsAheadOfItGetJoinRows"`
Expected: FAIL — `db.GitHubBranchCommits` is empty; no join rows are written at all.

- [ ] **Step 3: Write minimal implementation**

In `SyncCompanyAsync`, pass the default branch through:

```csharp
            var (inserted, commitFailures) = await SyncCommitsAsync(
                installation, repository.Id, repository.FullName, branchResult.Value!,
                repository.DefaultBranch, since, cancellationToken);
```

In `SyncCommitsAsync`, add the parameter and replace the loop header:

```csharp
    private async Task<(int Inserted, List<SyncFailureDto> Failures)> SyncCommitsAsync(
        GitHubInstallation installation,
        int repositoryRowId,
        string fullName,
        List<string> branches,
        string defaultBranch,
        DateTime since,
        CancellationToken cancellationToken)
    {
        var inserted = 0;
        var failures = new List<SyncFailureDto>();

        // Case-insensitively, because GitHubBranches.Name is SQL_Latin1_General_CP1_CI_AS and
        // comparing ordinally here is the 2026-08-16 soft-delete defect in a new place.
        var defaultName = branches.FirstOrDefault(b => string.Equals(b, defaultBranch, StringComparison.OrdinalIgnoreCase));

        // Default first, explicitly rather than by sorting on a bool: every later branch is
        // differenced against its shas, so it must already be in hand.
        var ordered = new List<string>();

        if (defaultName is not null)
            ordered.Add(defaultName);

        ordered.AddRange(branches.Where(b => !string.Equals(b, defaultName, StringComparison.OrdinalIgnoreCase)));

        // Empty is not "nothing is ahead" — it is "everything is ahead", which is the outcome
        // the whole definition exists to avoid. Task 5 makes that failure closed.
        var defaultShas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inheritanceEnabled = defaultName is not null;

        foreach (var branch in ordered)
        {
            var isDefault = string.Equals(branch, defaultName, StringComparison.OrdinalIgnoreCase);
```

Leave the URL construction, the request, and the three failure paths exactly as they are. Replace the body of `foreach (var commit in payload)` with:

```csharp
            foreach (var commit in payload)
            {
                if (string.IsNullOrEmpty(commit.Sha))
                    continue;

                if (isDefault)
                    defaultShas.Add(commit.Sha);

                var existing = await _unitOfWork.GitHubCommits
                    .GetByShaIncludingDeletedAsync(repositoryRowId, commit.Sha, cancellationToken);

                int commitRowId;

                if (existing is not null)
                {
                    // A commit is immutable; the only thing worth doing to an existing row is
                    // reviving it, so a repository that came back is not missing its history.
                    if (existing.IsDeleted)
                    {
                        existing.IsDeleted = false;
                        existing.DeletedAt = null;
                        await _unitOfWork.GitHubCommits.Update(existing, cancellationToken);
                        await _unitOfWork.SaveChangesAsync(cancellationToken);
                    }

                    commitRowId = existing.Id;
                }
                else
                {
                    var added = new TaskSphere.Domain.Entities.GitHubCommit
                    {
                        GitHubRepositoryId = repositoryRowId,
                        CompanyId = installation.CompanyId,
                        Sha = commit.Sha,
                        Message = commit.Commit?.Message ?? "",
                        AuthorName = commit.Commit?.Author?.Name ?? "",
                        AuthorLogin = commit.Author?.Login,
                        CommittedAtUtc = commit.Commit?.Author?.Date ?? DateTime.UtcNow,
                        HtmlUrl = commit.HtmlUrl ?? "",
                    };

                    await _unitOfWork.GitHubCommits.AddAsync(added, cancellationToken);
                    inserted++;

                    // Saved per commit so the next iteration's GetByShaIncludingDeletedAsync
                    // sees it: the same sha can arrive twice within one run, from two branches.
                    await _unitOfWork.SaveChangesAsync(cancellationToken);

                    commitRowId = added.Id;
                }

                await RecordAheadAsync(branch, commitRowId, commit.Sha);
            }
```

Add the local function at the end of `SyncCommitsAsync`, before `return (inserted, failures);`:

```csharp
        async System.Threading.Tasks.Task RecordAheadAsync(string branch, int commitRowId, string sha)
        {
            if (!inheritanceEnabled)
                return;

            // A commit on main is not ahead of main.
            if (string.Equals(branch, defaultName, StringComparison.OrdinalIgnoreCase))
                return;

            if (defaultShas.Contains(sha))
                return;

            var branchRow = await _unitOfWork.GitHubBranches
                .GetByNameIncludingDeletedAsync(repositoryRowId, branch, cancellationToken);

            if (branchRow is null)
                return;

            if (await _unitOfWork.GitHubBranchCommits.ExistsForPairAsync(branchRow.Id, commitRowId, cancellationToken))
                return;

            await _unitOfWork.GitHubBranchCommits.AddAsync(new GitHubBranchCommit
            {
                CompanyId = installation.CompanyId,
                GitHubBranchId = branchRow.Id,
                GitHubCommitId = commitRowId,
            }, cancellationToken);

            // On the same per-commit save the commit upsert uses, so a mid-loop failure never
            // leaves a commit in the mirror with its ahead-ness lost.
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
```

Add `using GitHubBranchCommit = TaskSphere.Domain.Entities.GitHubBranchCommit;` to the file's alias block.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test TaskSphere.Tests --filter "FullyQualifiedName~GitHubActivitySyncTests"`
Expected: PASS, including every pre-existing test in the class.

- [ ] **Step 5: Commit**

```bash
git add TaskSphere.Infrastructure/Services/GitHubActivitySyncService.cs TaskSphere.Tests/Integration/GitHubActivitySyncTests.cs
git commit -m "Record commits ahead of the default branch during the commits pass

The default branch is listed first and every later branch is differenced
against its shas, so inheritance costs no extra GitHub calls."
```

---

### Task 4: Sync — the already-mirrored commit still earns its join row

**Files:**
- Test: `TaskSphere.Tests/Integration/GitHubActivitySyncTests.cs`
- Modify (only if the test fails): `TaskSphere.Infrastructure/Services/GitHubActivitySyncService.cs`

**Interfaces:**
- Consumes: `SyncCommitsAsync` as restructured in Task 3.
- Produces: nothing new.

Task 3's restructure was written to handle this, but the behaviour is invisible to any test that syncs once — so it gets its own test and its own gate.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async SystemTask.Task ACommitAlreadyInTheMirror_StillEarnsAJoinRowOnALaterBranch()
{
    // Run 1: the commit arrives on main only, so it is not ahead of anything.
    var first = new FakeApiClient()
        .On("/repos/rigon-org/api/branches", Branches(("main", "aaa")))
        .On("sha=main", Commits(("c1", "feat: groundwork", "MRigonM")));

    await Sync(first);

    await using (var db = NewContext())
        Assert.Empty(await db.GitHubBranchCommits.ToListAsync());

    // Run 2: a branch appears carrying c1, and main has since moved on without it — a branch
    // cut from older work. c1 is already in the mirror, so the upsert takes the "existing"
    // path. If that path skips the join write, inheritance only ever works for commits whose
    // FIRST sighting was on the feature branch.
    var second = new FakeApiClient()
        .On("/repos/rigon-org/api/branches", Branches(("main", "zzz"), ("TS-42-fix", "bbb")))
        .On("sha=main", Commits(("c9", "chore: unrelated", "MRigonM")))
        .On("sha=TS-42-fix", Commits(("c1", "feat: groundwork", "MRigonM")));

    await Sync(second);

    await using var after = NewContext();
    var row = Assert.Single(await after.GitHubBranchCommits
        .Include(bc => bc.Commit)
        .Include(bc => bc.Branch)
        .ToListAsync());

    Assert.Equal("c1", row.Commit!.Sha);
    Assert.Equal("TS-42-fix", row.Branch!.Name);
}

[Fact]
public async SystemTask.Task RunningTheSyncTwice_DoesNotDuplicateJoinRows()
{
    static FakeApiClient Api() => new FakeApiClient()
        .On("/repos/rigon-org/api/branches", Branches(("main", "aaa"), ("TS-42-fix", "bbb")))
        .On("sha=main", Commits(("shared1", "chore: bump deps", "MRigonM")))
        .On("sha=TS-42-fix", Commits(
            ("shared1", "chore: bump deps", "MRigonM"),
            ("ahead1", "wire up the login form", "MRigonM")));

    await Sync(Api());
    await Sync(Api());

    await using var db = NewContext();
    Assert.Single(await db.GitHubBranchCommits.ToListAsync());
}

[Fact]
public async SystemTask.Task AnInheritedLink_SurvivesTheBranchBeingMergedIntoTheDefault()
{
    // The add-only decision, tested rather than assumed. Run 1: TS-42's branch is ahead, so
    // the commit is inherited. Run 2: the work has merged, main now reaches the commit, and
    // the set difference for that branch is empty. The link must still be there — losing a
    // task's commit history the moment the work lands is the outcome add-only exists to avoid.
    var before = new FakeApiClient()
        .On("/repos/rigon-org/api/branches", Branches(("main", "aaa"), ("TS-42-fix", "bbb")))
        .On("sha=main", Commits(("base1", "chore: bump deps", "MRigonM")))
        .On("sha=TS-42-fix", Commits(
            ("base1", "chore: bump deps", "MRigonM"),
            ("ahead1", "wire up the login form", "MRigonM")));

    await Sync(before);

    await using (var db = NewContext())
    {
        var link = Assert.Single(await db.TaskLinks.Where(l => l.ViaGitHubBranchId != null).ToListAsync());
        Assert.Equal(_ts42TaskId, link.TaskId);
    }

    var after = new FakeApiClient()
        .On("/repos/rigon-org/api/branches", Branches(("main", "ccc"), ("TS-42-fix", "bbb")))
        .On("sha=main", Commits(
            ("base1", "chore: bump deps", "MRigonM"),
            ("ahead1", "wire up the login form", "MRigonM")))   // merged: main reaches it now
        .On("sha=TS-42-fix", Commits(
            ("base1", "chore: bump deps", "MRigonM"),
            ("ahead1", "wire up the login form", "MRigonM")));

    await Sync(after);

    await using var final = NewContext();
    Assert.Single(await final.TaskLinks.Where(l => l.ViaGitHubBranchId != null).ToListAsync());
}
```

This test needs a task whose key routes to `TS-42-fix`. `GitHubActivitySyncTests` already seeds `_ts42TaskId` in `InitializeAsync`; use it rather than seeding another.

- [ ] **Step 2: Run tests**

Run: `dotnet test TaskSphere.Tests --filter "FullyQualifiedName~GitHubActivitySyncTests"`
Expected: all three new tests PASS if Task 3 was implemented as written. **If `ACommitAlreadyInTheMirror...` FAILS, the `continue` on the existing-commit path survived the restructure** — fix `SyncCommitsAsync` so `RecordAheadAsync` is reached on both branches of the upsert, then re-run.

- [ ] **Step 3: Prove the test can fail**

Temporarily change `RecordAheadAsync`'s call site so it runs only in the `else` (newly-inserted) branch. Re-run both tests.
Expected: `ACommitAlreadyInTheMirror...` FAILS. Restore the code and re-run to green.

A test that cannot fail is worse than no test — this branch is exactly where a vacuous one would hide.

- [ ] **Step 4: Commit**

```bash
git add TaskSphere.Tests/Integration/GitHubActivitySyncTests.cs TaskSphere.Infrastructure/Services/GitHubActivitySyncService.cs
git commit -m "Pin that an already-mirrored commit earns its join row on a later branch

The existing-commit path is where inheritance would silently work only for
first sightings, and one sync per test cannot see it."
```

---

### Task 5: Sync — fail closed, and match the default branch case-insensitively

**Files:**
- Modify: `TaskSphere.Infrastructure/Services/GitHubActivitySyncService.cs` (`SyncCommitsAsync`)
- Test: `TaskSphere.Tests/Integration/GitHubActivitySyncTests.cs`

**Interfaces:**
- Consumes: `SyncCommitsAsync` from Tasks 3-4.
- Produces: nothing new.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async SystemTask.Task WhenTheDefaultBranchListingFails_NoJoinRowsAreWritten_ButCommitsStillIngest()
{
    // Failing OPEN here is the worst outcome in the feature: an empty default set makes every
    // commit on every branch "ahead", which is the full-history inheritance the definition
    // exists to prevent. Asserting the sync merely succeeded would not catch it.
    var api = new FakeApiClient()
        .On("/repos/rigon-org/api/branches", Branches(("main", "aaa"), ("TS-42-fix", "bbb")))
        .Fail("sha=main", new Error("GitHub.SyncFailed", "GitHub is unavailable."))
        .On("sha=TS-42-fix", Commits(("ahead1", "wire up the login form", "MRigonM")));

    var result = await Sync(api);

    await using var db = NewContext();

    Assert.Empty(await db.GitHubBranchCommits.ToListAsync());

    // The commits themselves are unaffected — only inheritance is skipped.
    Assert.Contains(await db.GitHubCommits.ToListAsync(), c => c.Sha == "ahead1");
    Assert.Contains(result.Value!.Failures, f => f.Branch == "main");
}

[Fact]
public async SystemTask.Task WhenTheDefaultBranchIsAbsentFromTheBranchList_NoJoinRowsAreWritten()
{
    // DefaultBranch is "main" on the seeded repository, but GitHub reports only the feature
    // branch. Nothing to difference against, so nothing may be claimed as ahead.
    var api = new FakeApiClient()
        .On("/repos/rigon-org/api/branches", Branches(("TS-42-fix", "bbb")))
        .On("sha=TS-42-fix", Commits(("ahead1", "wire up the login form", "MRigonM")));

    await Sync(api);

    await using var db = NewContext();
    Assert.Empty(await db.GitHubBranchCommits.ToListAsync());
}

[Fact]
public async SystemTask.Task TheDefaultBranchIsMatchedCaseInsensitively()
{
    // GitHub reports "Main"; the repository's DefaultBranch is "main". Ordinally these differ,
    // and treating them as different branches means main's own history is claimed as ahead —
    // the 2026-08-16 collation defect, one table over.
    var api = new FakeApiClient()
        .On("/repos/rigon-org/api/branches", Branches(("Main", "aaa"), ("TS-42-fix", "bbb")))
        .On("sha=Main", Commits(("shared1", "chore: bump deps", "MRigonM")))
        .On("sha=TS-42-fix", Commits(
            ("shared1", "chore: bump deps", "MRigonM"),
            ("ahead1", "wire up the login form", "MRigonM")));

    await Sync(api);

    await using var db = NewContext();
    var row = Assert.Single(await db.GitHubBranchCommits.Include(bc => bc.Commit).ToListAsync());
    Assert.Equal("ahead1", row.Commit!.Sha);
}
```

Confirm the seeded `_apiRepositoryId` repository in `InitializeAsync` has `DefaultBranch = "main"`. If it does not, set it there — do not work around it in the test body.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test TaskSphere.Tests --filter "FullyQualifiedName~GitHubActivitySyncTests.WhenTheDefaultBranchListingFails_NoJoinRowsAreWritten_ButCommitsStillIngest"`
Expected: FAIL — `ahead1` gets a join row, because a failed default listing leaves `defaultShas` empty while `inheritanceEnabled` stays true.

- [ ] **Step 3: Write minimal implementation**

In `SyncCommitsAsync`, disable inheritance on each of the default branch's three failure paths. Replace each of the three `continue;` statements inside the loop with a call that first records the failure, then:

```csharp
            if (!response.IsSuccess)
            {
                failures.Add(new SyncFailureDto(fullName, response.Errors[0].Description, branch));

                // Fail closed: with no default listing, "not in defaultShas" would be true of
                // every commit in the repository.
                if (isDefault)
                    inheritanceEnabled = false;

                continue;
            }
```

Apply the same two lines to the `JsonException` path and the `payload is null` path.

The case-insensitive match and the absent-default case are already handled by Task 3's `defaultName` lookup and its `inheritanceEnabled = defaultName is not null`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test TaskSphere.Tests --filter "FullyQualifiedName~GitHubActivitySyncTests"`
Expected: PASS

- [ ] **Step 5: Commit**

```bash
git add TaskSphere.Infrastructure/Services/GitHubActivitySyncService.cs TaskSphere.Tests/Integration/GitHubActivitySyncTests.cs
git commit -m "Fail closed when the default branch cannot be listed

An empty default sha set means every commit is ahead, so a failed listing
must disable inheritance rather than widen it."
```

---

### Task 6: Resolver — the inheritance pass

**Files:**
- Modify: `TaskSphere.Infrastructure/Services/GitHubTaskLinkResolver.cs`
- Test: `TaskSphere.Tests/Integration/GitHubTaskLinkResolverTests.cs`

**Interfaces:**
- Consumes: `_unitOfWork.GitHubBranchCommits.GetByCompany` (Task 2); `TaskLink.ViaGitHubBranchId` (Task 1).
- Produces: `TaskLink` rows with `GitHubCommitId` set and `ViaGitHubBranchId` set to the branch that conferred them. `TaskLinkResolution.LinksCreated` counts them; `KeysSeen` and `Unresolved` do not move.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async SystemTask.Task ACommitAheadOnALinkedBranch_IsInheritedByThatBranchesTask()
{
    await using var db = NewContext();
    var uow = new UnitOfWork(db);

    // The branch names TS-42; the commit names nothing at all. Message-only resolution links
    // the branch and stops, which is the gap this feature closes.
    var branch = new GitHubBranch { CompanyId = _companyId, GitHubRepositoryId = _apiRepositoryId, Name = "TS-42-login", HeadSha = "bbb" };
    var commit = new GitHubCommit
    {
        CompanyId = _companyId,
        GitHubRepositoryId = _apiRepositoryId,
        Sha = "ahead1",
        Message = "wire up the login form",
        AuthorName = "Rigon",
        CommittedAtUtc = DateTime.UtcNow,
        HtmlUrl = "https://github.com/rigon-org/api/commit/ahead1",
    };
    db.GitHubBranches.Add(branch);
    db.GitHubCommits.Add(commit);
    await db.SaveChangesAsync();

    db.GitHubBranchCommits.Add(new TaskSphere.Domain.Entities.GitHubBranchCommit
    {
        CompanyId = _companyId,
        GitHubBranchId = branch.Id,
        GitHubCommitId = commit.Id,
    });
    await db.SaveChangesAsync();

    var result = await new GitHubTaskLinkResolver(uow).ResolveAsync(_companyId);

    // The branch link AND the inherited commit link, in one run: the branch link is created by
    // the branch pass of this same run and is still unsaved when inheritance reads it.
    Assert.Equal(2, result.LinksCreated);

    var inherited = Assert.Single(await db.TaskLinks.Where(l => l.GitHubCommitId != null).ToListAsync());
    Assert.Equal(_ts42TaskId, inherited.TaskId);
    Assert.Equal(commit.Id, inherited.GitHubCommitId);
    Assert.Equal(branch.Id, inherited.ViaGitHubBranchId);
}

[Fact]
public async SystemTask.Task InheritedLinks_DoNotInflateKeysSeen()
{
    await using var db = NewContext();
    var uow = new UnitOfWork(db);

    var branch = new GitHubBranch { CompanyId = _companyId, GitHubRepositoryId = _apiRepositoryId, Name = "TS-42-login", HeadSha = "bbb" };
    var commit = new GitHubCommit
    {
        CompanyId = _companyId,
        GitHubRepositoryId = _apiRepositoryId,
        Sha = "ahead1",
        Message = "wire up the login form",
        AuthorName = "Rigon",
        CommittedAtUtc = DateTime.UtcNow,
        HtmlUrl = "https://github.com/rigon-org/api/commit/ahead1",
    };
    db.GitHubBranches.Add(branch);
    db.GitHubCommits.Add(commit);
    await db.SaveChangesAsync();

    db.GitHubBranchCommits.Add(new TaskSphere.Domain.Entities.GitHubBranchCommit
    {
        CompanyId = _companyId,
        GitHubBranchId = branch.Id,
        GitHubCommitId = commit.Id,
    });
    await db.SaveChangesAsync();

    var result = await new GitHubTaskLinkResolver(uow).ResolveAsync(_companyId);

    // One key was read in this run — the branch name. Inheritance reads no text, so counting
    // it as a key seen would make the sync summary lie about how much GitHub data was scanned.
    Assert.Equal(1, result.KeysSeen);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TaskSphere.Tests --filter "FullyQualifiedName~GitHubTaskLinkResolverTests.ACommitAheadOnALinkedBranch_IsInheritedByThatBranchesTask"`
Expected: FAIL — `LinksCreated` is 1, not 2; no commit link exists.

- [ ] **Step 3: Write minimal implementation**

In `GitHubTaskLinkResolver.ResolveAsync`, after the pull-request `foreach` and **before** the `if (created > 0)` save:

```csharp
        // Fourth pass, and deliberately last: the three text passes must have claimed their
        // tuples first, so a commit that names the task itself reads as direct rather than
        // inherited. See InheritFromBranchesAsync.
        await InheritFromBranchesAsync();
```

Add the local function beside `LinkAll`:

```csharp
        async Task InheritFromBranchesAsync()
        {
            // `existing` is the reason this works in a single run. It was seeded from the
            // database and has been added to by the branch pass, so it holds both the branch
            // links that already existed and the ones created moments ago — which are still
            // unsaved and invisible to a fresh query. Reading the database here instead would
            // make a newly linked branch inherit nothing until the NEXT sync.
            //
            // Materialized before the loop: the loop adds to `existing`, and iterating a
            // HashSet while adding to it throws.
            var branchLinks = existing
                .Where(e => e.GitHubBranchId is not null)
                .Select(e => (e.TaskId, BranchId: e.GitHubBranchId!.Value))
                .ToList();

            if (branchLinks.Count == 0)
                return;

            var branchIds = branchLinks.Select(l => l.BranchId).Distinct().ToList();

            var aheadRows = await _unitOfWork.GitHubBranchCommits
                .GetByCompany(companyId)
                .Where(bc => branchIds.Contains(bc.GitHubBranchId))
                .Select(bc => new { bc.GitHubBranchId, bc.GitHubCommitId })
                .ToListAsync(cancellationToken);

            var commitsByBranch = aheadRows
                .GroupBy(r => r.GitHubBranchId)
                .ToDictionary(g => g.Key, g => g.Select(r => r.GitHubCommitId).ToList());

            foreach (var (taskId, branchId) in branchLinks)
            {
                if (!commitsByBranch.TryGetValue(branchId, out var commitIds))
                    continue;

                foreach (var commitId in commitIds)
                {
                    // The SAME tuple shape the commit pass uses — ViaGitHubBranchId is not part
                    // of it, and must never become part of it. Widening the tuple would let a
                    // direct link and an inherited one for one commit both look new, and they
                    // collide on IX_TaskLinks_TaskId_CommitId. Task 7 pins this.
                    if (!existing.Add((taskId, commitId, null, null)))
                        continue;

                    await _unitOfWork.TaskLinks.AddAsync(new TaskLink
                    {
                        CompanyId = companyId,
                        TaskId = taskId,
                        GitHubCommitId = commitId,
                        ViaGitHubBranchId = branchId,
                    }, cancellationToken);

                    created++;
                }
            }
        }
```

`seen` and `unresolved` are untouched: no keys were read.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test TaskSphere.Tests --filter "FullyQualifiedName~GitHubTaskLinkResolverTests"`
Expected: PASS, including every pre-existing test in the class.

- [ ] **Step 5: Commit**

```bash
git add TaskSphere.Infrastructure/Services/GitHubTaskLinkResolver.cs TaskSphere.Tests/Integration/GitHubTaskLinkResolverTests.cs
git commit -m "Inherit commits ahead of default onto tasks via their linked branch

The pass reads the run's own `existing` set rather than the database, so a
branch linked moments earlier confers its commits in the same run."
```

---

### Task 7: Resolver — precedence, the tuple guard, and the many-to-many

**Files:**
- Test: `TaskSphere.Tests/Integration/GitHubTaskLinkResolverTests.cs`
- Modify (only if a test fails): `TaskSphere.Infrastructure/Services/GitHubTaskLinkResolver.cs`

**Interfaces:**
- Consumes: the inheritance pass from Task 6.
- Produces: nothing new.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async SystemTask.Task ACommitThatNamesTheTaskAndSitsOnItsBranch_IsOneRowMarkedDirect()
{
    await using var db = NewContext();
    var uow = new UnitOfWork(db);

    var branch = new GitHubBranch { CompanyId = _companyId, GitHubRepositoryId = _apiRepositoryId, Name = "TS-42-login", HeadSha = "bbb" };
    var commit = new GitHubCommit
    {
        CompanyId = _companyId,
        GitHubRepositoryId = _apiRepositoryId,
        Sha = "both1",
        Message = "TS-42 wire up the login form",   // names the task ITSELF
        AuthorName = "Rigon",
        CommittedAtUtc = DateTime.UtcNow,
        HtmlUrl = "https://github.com/rigon-org/api/commit/both1",
    };
    db.GitHubBranches.Add(branch);
    db.GitHubCommits.Add(commit);
    await db.SaveChangesAsync();

    db.GitHubBranchCommits.Add(new TaskSphere.Domain.Entities.GitHubBranchCommit
    {
        CompanyId = _companyId,
        GitHubBranchId = branch.Id,
        GitHubCommitId = commit.Id,
    });
    await db.SaveChangesAsync();

    await new GitHubTaskLinkResolver(uow).ResolveAsync(_companyId);

    // ONE row — IX_TaskLinks_TaskId_CommitId is unique — and it must read as direct. If the
    // passes ever reorder, this flips to the branch id and the panel starts claiming a commit
    // was inherited when its own message named the task.
    var link = Assert.Single(await db.TaskLinks.Where(l => l.GitHubCommitId == commit.Id).ToListAsync());
    Assert.Null(link.ViaGitHubBranchId);
}

[Fact]
public async SystemTask.Task ACommitAheadOnTwoLinkedBranches_ReachesBothTasks()
{
    await using var db = NewContext();
    var uow = new UnitOfWork(db);

    // The case the join table exists for: TS-51's branch was cut from TS-42's, so one commit
    // is ahead of default on both. A column on GitHubCommit could record only one of them.
    var branch42 = new GitHubBranch { CompanyId = _companyId, GitHubRepositoryId = _apiRepositoryId, Name = "TS-42-login", HeadSha = "bbb" };
    var branch51 = new GitHubBranch { CompanyId = _companyId, GitHubRepositoryId = _apiRepositoryId, Name = "TS-51-signup", HeadSha = "ccc" };
    var commit = new GitHubCommit
    {
        CompanyId = _companyId,
        GitHubRepositoryId = _apiRepositoryId,
        Sha = "shared-ahead",
        Message = "extract the auth form base",
        AuthorName = "Rigon",
        CommittedAtUtc = DateTime.UtcNow,
        HtmlUrl = "https://github.com/rigon-org/api/commit/shared-ahead",
    };
    db.GitHubBranches.AddRange(branch42, branch51);
    db.GitHubCommits.Add(commit);
    await db.SaveChangesAsync();

    db.GitHubBranchCommits.AddRange(
        new TaskSphere.Domain.Entities.GitHubBranchCommit { CompanyId = _companyId, GitHubBranchId = branch42.Id, GitHubCommitId = commit.Id },
        new TaskSphere.Domain.Entities.GitHubBranchCommit { CompanyId = _companyId, GitHubBranchId = branch51.Id, GitHubCommitId = commit.Id });
    await db.SaveChangesAsync();

    await new GitHubTaskLinkResolver(uow).ResolveAsync(_companyId);

    var links = await db.TaskLinks
        .Where(l => l.GitHubCommitId == commit.Id)
        .OrderBy(l => l.TaskId)
        .ToListAsync();

    // Assert the PAIRING, not the count: two rows with the right task ids and the right via
    // branches. Counting two would pass if both rows belonged to the same task.
    Assert.Equal(2, links.Count);
    Assert.Contains(links, l => l.TaskId == _ts42TaskId && l.ViaGitHubBranchId == branch42.Id);
    Assert.Contains(links, l => l.TaskId == _ts51TaskId && l.ViaGitHubBranchId == branch51.Id);
}

[Fact]
public async SystemTask.Task RunningTheResolverTwice_CreatesNoSecondInheritedLink()
{
    await using var db = NewContext();
    var uow = new UnitOfWork(db);

    var branch = new GitHubBranch { CompanyId = _companyId, GitHubRepositoryId = _apiRepositoryId, Name = "TS-42-login", HeadSha = "bbb" };
    var commit = new GitHubCommit
    {
        CompanyId = _companyId,
        GitHubRepositoryId = _apiRepositoryId,
        Sha = "ahead1",
        Message = "wire up the login form",
        AuthorName = "Rigon",
        CommittedAtUtc = DateTime.UtcNow,
        HtmlUrl = "https://github.com/rigon-org/api/commit/ahead1",
    };
    db.GitHubBranches.Add(branch);
    db.GitHubCommits.Add(commit);
    await db.SaveChangesAsync();

    db.GitHubBranchCommits.Add(new TaskSphere.Domain.Entities.GitHubBranchCommit
    {
        CompanyId = _companyId,
        GitHubBranchId = branch.Id,
        GitHubCommitId = commit.Id,
    });
    await db.SaveChangesAsync();

    await new GitHubTaskLinkResolver(uow).ResolveAsync(_companyId);
    var second = await new GitHubTaskLinkResolver(uow).ResolveAsync(_companyId);

    // A re-run must insert nothing. If the `existing` tuple ever gains ViaGitHubBranchId, the
    // second run's inherited tuple stops matching the stored one and the insert violates
    // IX_TaskLinks_TaskId_CommitId with a DbUpdateException out of the whole sync.
    Assert.Equal(0, second.LinksCreated);
    Assert.Single(await db.TaskLinks.Where(l => l.GitHubCommitId == commit.Id).ToListAsync());
}
```

- [ ] **Step 2: Run tests**

Run: `dotnet test TaskSphere.Tests --filter "FullyQualifiedName~GitHubTaskLinkResolverTests"`
Expected: PASS if Task 6 was implemented as written.

If `ACommitThatNamesTheTaskAndSitsOnItsBranch...` fails, the inheritance pass is not running last. If `RunningTheResolverTwice...` fails, the `existing` tuple was widened.

- [ ] **Step 3: Prove the guard test can fail**

Temporarily widen the tuple: change `existing` to a `HashSet<(int, int?, int?, int?, int?)>` carrying `ViaGitHubBranchId`, and update both `LinkAll` and `InheritFromBranchesAsync` accordingly. Re-run.
Expected: `RunningTheResolverTwice_CreatesNoSecondInheritedLink` FAILS with a duplicate-key `DbUpdateException`. Restore and re-run to green.

This is the step that makes the "change nothing" decision defensible to a later reader — without it, the four-element tuple looks like an oversight.

- [ ] **Step 4: Commit**

```bash
git add TaskSphere.Tests/Integration/GitHubTaskLinkResolverTests.cs
git commit -m "Pin resolver precedence, the un-widened tuple, and the two-branch case

A commit that names its task stays direct; the existing-tuple shape is
load-bearing and now fails a test if it is widened."
```

---

### Task 8: Read path — carry provenance to the DTO

**Files:**
- Modify: `TaskSphere.Domain/DataTransferObjects/GitHub/GitHubActivityDtos.cs` (`TaskCommitDto`)
- Modify: `TaskSphere.Infrastructure/Services/GitHubTaskActivityService.cs` (commit projection ~lines 90-102)
- Test: `TaskSphere.Tests/Integration/GitHubTaskActivityReadTests.cs`

**Interfaces:**
- Consumes: `TaskLink.ViaGitHubBranchId` (Task 1), links produced by Task 6.
- Produces: `TaskCommitDto(string Sha, string ShortSha, string Message, string AuthorName, string? AuthorLogin, DateTime CommittedAtUtc, string HtmlUrl, string RepositoryFullName, string? ViaBranchName)` — the new member is **last**, so existing positional construction sites keep compiling.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact]
public async SystemTask.Task AnInheritedCommit_CarriesTheNameOfTheBranchThatConferredIt()
{
    await using var db = NewContext();

    // Two commits on one task: one named the task, one was inherited. The panel has to tell
    // them apart, and the read must name WHICH branch — a bare flag could not.
    var branch = new TaskSphere.Domain.Entities.GitHubBranch
    {
        CompanyId = _companyId,
        GitHubRepositoryId = _repositoryId,
        Name = "TS-42-login",
        HeadSha = "bbb",
    };
    db.GitHubBranches.Add(branch);

    var direct = NewCommit("direct1", "TS-42 add the form");
    var inherited = NewCommit("ahead1", "wire up the login form");
    db.GitHubCommits.AddRange(direct, inherited);
    await db.SaveChangesAsync();

    db.TaskLinks.AddRange(
        new TaskLink { CompanyId = _companyId, TaskId = _taskId, GitHubBranchId = branch.Id },
        new TaskLink { CompanyId = _companyId, TaskId = _taskId, GitHubCommitId = direct.Id },
        new TaskLink { CompanyId = _companyId, TaskId = _taskId, GitHubCommitId = inherited.Id, ViaGitHubBranchId = branch.Id });
    await db.SaveChangesAsync();

    var result = await Read(isCompanyAdmin: true);

    var commits = result.Value!.Commits;

    // The pairing, not the presence: assert each sha carries the right provenance. Asserting
    // that "TS-42-login" appears somewhere would pass even if it were on the wrong commit.
    // Single-with-predicate rather than indexing, because the fixture seeds records of its own.
    Assert.Equal("TS-42-login", Assert.Single(commits, c => c.Sha == "ahead1").ViaBranchName);
    Assert.Null(Assert.Single(commits, c => c.Sha == "direct1").ViaBranchName);
}

private TaskSphere.Domain.Entities.GitHubCommit NewCommit(string sha, string message) => new()
{
    CompanyId = _companyId,
    GitHubRepositoryId = _repositoryId,
    Sha = sha,
    Message = message,
    AuthorName = "Rigon",
    CommittedAtUtc = DateTime.UtcNow,
    HtmlUrl = $"https://github.com/rigon-org/api/commit/{sha}",
};
```

`Read(...)` and `_taskId` / `_repositoryId` are the fixture's own helpers (`GitHubTaskActivityReadTests.cs:162`, `:38-41`) — reuse them rather than constructing `GitHubTaskActivityService` inline.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test TaskSphere.Tests --filter "FullyQualifiedName~GitHubTaskActivityReadTests.AnInheritedCommit_CarriesTheNameOfTheBranchThatConferredIt"`
Expected: FAIL — `TaskCommitDto` has no `ViaBranchName`.

- [ ] **Step 3: Write minimal implementation**

In `GitHubActivityDtos.cs`:

```csharp
public record TaskCommitDto(
    string Sha,
    string ShortSha,
    string Message,
    string AuthorName,
    string? AuthorLogin,
    DateTime CommittedAtUtc,
    string HtmlUrl,
    string RepositoryFullName,
    /// <summary>
    /// Null when the commit's own message named the task. Set to the branch that conferred it
    /// when the link was inherited — the commit itself never mentions the task, and without
    /// this the panel shows it with nothing explaining why it is there.
    /// </summary>
    string? ViaBranchName);
```

In `GitHubTaskActivityService.GetForTaskAsync`, after the `branches` query and before the `return`:

```csharp
        // Both halves are already in hand: `links` is materialized above, and every via-branch
        // necessarily has its OWN TaskLink on this task — inheritance only flows through a
        // branch already linked — so it is already in `branches`. No extra query.
        var viaBranchIdByCommitId = links
            .Where(l => l.GitHubCommitId is not null && l.ViaGitHubBranchId is not null)
            .ToDictionary(l => l.GitHubCommitId!.Value, l => l.ViaGitHubBranchId!.Value);

        var branchNamesById = branches.ToDictionary(b => b.Id, b => b.Name);
```

Extend the commit projection with a ninth argument:

```csharp
                .Select(c => new TaskCommitDto(
                    c.Sha,
                    c.Sha.Length >= 7 ? c.Sha[..7] : c.Sha,
                    c.Message,
                    c.AuthorName,
                    c.AuthorLogin,
                    c.CommittedAtUtc,
                    c.HtmlUrl,
                    repositoryNames[c.GitHubRepositoryId],
                    viaBranchIdByCommitId.TryGetValue(c.Id, out var viaId)
                        && branchNamesById.TryGetValue(viaId, out var viaName)
                            ? viaName
                            : null))
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test TaskSphere.Tests --filter "FullyQualifiedName~GitHubTaskActivityReadTests"`
Expected: PASS

- [ ] **Step 5: Run the whole backend suite**

Run: `dotnet test TaskSphere.Tests`
Expected: every test passes. Baseline was 471; this plan adds 18 backend tests (2+2+2+3+3+2+3+1 across Tasks 1-8), so expect 489. **Report the actual number rather than the expected one** — a count that matches because you copied it from here is worth nothing.

- [ ] **Step 6: Commit**

```bash
git add TaskSphere.Domain/DataTransferObjects/GitHub/GitHubActivityDtos.cs TaskSphere.Infrastructure/Services/GitHubTaskActivityService.cs TaskSphere.Tests/Integration/GitHubTaskActivityReadTests.cs
git commit -m "Carry inherited-commit provenance through to TaskCommitDto

The via-branch is always already loaded for the task, so naming it costs
no additional query."
```

---

### Task 9: Client — render the via-branch marker

**Files:**
- Modify: `client/src/app/core/models/github-activity.models.ts` (`TaskCommitDto`)
- Modify: `client/src/app/components/tasks/task-github-activity.component.html` (commits `<li>`, ~line 109)
- Test: `client/src/app/components/tasks/task-github-activity.component.spec.ts`

**Interfaces:**
- Consumes: `viaBranchName` from Task 8's DTO.
- Produces: nothing further.

- [ ] **Step 1: Write the failing test**

Append to `task-github-activity.component.spec.ts`, following the fixture construction already in the file:

```typescript
it('marks an inherited commit with the branch that conferred it, and leaves a direct one bare', async () => {
  // Two commits, so the assertion can prove the marker landed on the RIGHT one. With a single
  // fixture commit, "the text appears somewhere" is true of a marker rendered on every row.
  const activity = {
    commits: [
      { sha: 'direct1', shortSha: 'direct1', message: 'TS-42 add the form', authorName: 'Rigon', authorLogin: 'MRigonM', committedAtUtc: '2026-08-27T10:00:00Z', htmlUrl: 'https://example.invalid/1', repositoryFullName: 'rigon-org/api', viaBranchName: null },
      { sha: 'ahead1', shortSha: 'ahead1', message: 'wire up the login form', authorName: 'Rigon', authorLogin: 'MRigonM', committedAtUtc: '2026-08-27T11:00:00Z', htmlUrl: 'https://example.invalid/2', repositoryFullName: 'rigon-org/api', viaBranchName: 'TS-42-login' },
    ],
    branches: [],
    pullRequests: [],
    lastSyncedAtUtc: null,
  };

  const { fixture } = await setup({ payload: activity as TaskGitHubActivityDto });

  // `host()` exists because `nativeElement` is `any` and a generic querySelector on it is TS2347.
  const rows = host(fixture).querySelectorAll('section li');

  expect(rows[0].querySelector('[data-via-branch]')).toBeNull();
  expect(rows[1].querySelector('[data-via-branch]')?.textContent?.trim()).toBe('via TS-42-login');
});
```

`setup({ payload })` and `host(fixture)` are the file's existing helpers (`task-github-activity.component.spec.ts:88` and `:58`) — do not add a new TestBed configuration.

- [ ] **Step 2: Run test to verify it fails**

Run: `cd client && npm test -- task-github-activity`
Expected: FAIL — no `[data-via-branch]` element exists.

- [ ] **Step 3: Write minimal implementation**

In `github-activity.models.ts`, add to `TaskCommitDto`:

```typescript
  /**
   * Null when the commit's own message named the task. Set to the branch that conferred it
   * when the link was inherited — the commit never mentions the task at all.
   */
  viaBranchName: string | null;
```

In `task-github-activity.component.html`, inside the commits `<li>` after the author span:

```html
          <span
            *ngIf="commit.viaBranchName"
            data-via-branch
            class="text-xs text-slate-500"
          >via <span class="font-mono">{{ commit.viaBranchName }}</span></span>
```

The `via ` and the branch name sit in one element on purpose — `preserveWhitespaces` is off by default, so two sibling text nodes would render glued together as `viaTS-42-login`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `cd client && npm test`
Expected: 141 total, 139 passing. The 2 failures must still be exactly the pre-existing `app.spec.ts` `NG0201` pair — **if any other test fails, stop and report it rather than adjusting the test.**

- [ ] **Step 5: Commit**

```bash
git add client/src/app/core/models/github-activity.models.ts client/src/app/components/tasks/task-github-activity.component.html client/src/app/components/tasks/task-github-activity.component.spec.ts
git commit -m "Mark inherited commits with the branch that conferred them

A directly-named commit renders exactly as before; only an inherited one
gains the marker."
```

---

## Live Verification Checklist

Code-complete is not done — the last three slices each had their most valuable defect found by using the feature, not by testing it. After Task 9:

- [ ] Create a branch from a task via the existing create-branch flow, push two commits whose messages mention nothing, press **Sync**, open the task's Activity tab. Both commits appear, each marked *via `<branch>`*.
- [ ] A commit whose message *does* name the task shows **no** marker.
- [ ] Merge the branch. Press **Sync** again. The inherited commits are **still** on the task.
- [ ] A task linked only to the default branch inherits nothing.
- [ ] A repository whose default branch was renamed on GitHub still inherits correctly on the next sync.
- [ ] Two tasks whose branches share a commit both show it.

---

## Deferred (do not build in this plan)

- The created-and-merged-between-syncs gap
- Board-triggered commit refresh
- `GET /compare` for an untruncated ahead-set
- A separate `InheritedLinks` count in `SyncActivityResultDto`
