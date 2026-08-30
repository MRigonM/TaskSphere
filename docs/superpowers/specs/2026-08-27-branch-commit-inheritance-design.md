# Branch → Commit Inheritance — Design Spec

**Date:** 2026-08-27
**Project:** TaskSphere
**Sub-project:** D (smart commits) — third slice
**Status:** Approved (design), not implemented

---

## Goal

A commit that does not mention `TS-1` anywhere still appears under TS-1, because it sits on a branch linked to TS-1.

---

## Why this exists

Today the resolver scans each mirrored record's *own* text independently (`GitHubTaskLinkResolver.ResolveAsync`). A commit reaches a task only by naming it in its message. That means the common case — one branch named `feature/TS-1-login`, twelve commits on it with ordinary messages — puts a branch on the task's Activity tab and none of the work.

This is the last of the three GitHub features explored and deferred on 2026-08-22. Create-branch-from-task and merge → Done both shipped. It was ordered last deliberately: a branch TaskSphere named itself is a branch it can key on, so create-branch-from-task makes this slice more reliable than it would have been in August.

---

## What changed since the August exploration

The 2026-08-22 note assumed inheritance needed new GitHub calls and a new branch↔commit concept. Reading the code first says otherwise:

`GitHubActivitySyncService.SyncCommitsAsync` **already iterates branch by branch** — one `GET /commits?sha={branch}` per branch — and discards the branch at the upsert because `GitHubCommits` is keyed `(repository, sha)`. The branch↔commit association is already flowing through the sync and being thrown away.

`GitHubRepository.DefaultBranch` is already mirrored, and the default branch appears in the same listing loop under the same `since` window. So "ahead of the default branch" is a **local set difference**, not a new API call.

**This slice adds zero GitHub calls.**

---

## Scope Decisions

### 1. Inheritance means "ahead of the default branch"

A task inherits the commits reachable from its linked branch but **not** reachable from the repository's default branch.

The rejected alternative is everything reachable: `GET /commits?sha={branch}` returns commits *reachable* from a branch, not *unique* to it, so a branch cut from `main` reaches all of `main`'s history and TS-1 would show every commit the company made in 30 days. This was the trap the August note said had to be settled before any code.

Also rejected: *unique to this branch* (ahead of default **and** unreachable from any other non-default branch). It is stricter and handles branches cut from other feature branches, but a commit stops being unique the moment a colleague branches off you, so link meaning would change silently between syncs.

**Accepted consequence:** a branch cut from another feature branch inherits its parent's commits too.

**Accepted consequence:** the ahead-set is bounded by the 30-day sync window. A branch older than the window has its ahead-set truncated. `GET /compare/{default}...{branch}` would fix this authoritatively but doubles the commits pass from one call per branch to two, on the path that is already the expensive one — and the panel already accepts a 30-day horizon everywhere else.

### 2. Inherited links survive the merge — add-only

When a branch merges into the default branch, its commits become reachable from default and the set difference for that branch goes empty. The link **stays**.

This matches how the resolver already behaves: it creates `TaskLink` rows and never removes them. Recomputing instead would strip a task's commit history at the exact moment the work lands, and would make this the first thing in the system that deletes a `TaskLink`.

**Accepted gap:** a branch created *and* merged between two syncs was never observed while ahead, so it inherits nothing, ever. This is not hypothetical — the commits pass only runs on the company-wide sync. Closing it would need a second inheritance path triggered at merge, with its own trigger, tests, and GitHub call for the pull request's commit list. Deferred, not refused.

### 3. Provenance is a nullable FK, not an enum

`TaskLink` gains `int? ViaGitHubBranchId`. Null means the commit named the task itself; set means the commit was inherited through that branch.

An enum (`Direct | ViaBranch`) costs the same migration and carries less: with several branches on one task, a generic "via branch" marker cannot say *which* branch pulled a commit in. The FK answers "why is this commit here?" completely.

Without any discriminator the panel would show commits whose messages never mention the task, with nothing explaining their presence — the problem the August note raised.

### 4. Ahead-ness is baked at ingest, not recomputed at read

The stored row is a historical fact: *this commit was ahead of default on this branch when we saw it.* Never recomputed, never deleted.

This is what makes the storage model and the keep-after-merge policy state the same thing. The alternative — storing full membership and deriving ahead-ness at resolve time — would have the data say "not ahead" while the link says otherwise, and would be far larger (40 branches reaching 100 shared commits is ~4,000 rows per sync where this writes tens).

### 5. Company-wide sync only

Inheritance rides the existing commits pass. Slice 2's board-triggered refresh stays **pull-request-only**.

Extending the board refresh to commits would cost one GitHub call per branch per repository on every board load past cooldown — a repository with 40 branches is 40 calls where it is currently 1. That is the exact spend the PR-only decision was made to avoid.

**Consequence:** inherited commits appear when an admin presses **Sync**. A task's commit list can be stale until then — already true of every commit in the panel today.

---

## Data Model

### New entity — `GitHubBranchCommit : BaseEntity<int>`

| Field | Type |
|---|---|
| `CompanyId` | `Guid` |
| `GitHubBranchId` | `int` + nav |
| `GitHubCommitId` | `int` + nav |

One row per commit that was ahead of default on that branch at the moment it was seen.

Unique index `IX_GitHubBranchCommits_BranchId_CommitId` on `(GitHubBranchId, GitHubCommitId)`, **filtered** on `[IsDeleted] = 0` — following `TaskLink`, not the mirror tables.

This is the right analogue because a `GitHubBranchCommit` is a TaskSphere-owned derived row, not a GitHub identity. The mirror tables (`IX_GitHubCommits_RepositoryId_Sha`, `IX_GitHubBranches_RepositoryId_Name`) are unfiltered so a soft-deleted GitHub object is *revived* rather than duplicated — identity must be preserved. A join row has no identity to preserve; it is recomputable from a sync. Filtering it means ordinary queries suffice and **no `IgnoreQueryFilters` is needed anywhere in this slice**.

New `IGitHubBranchCommitRepository` on `IReadOnlyUnitOfWork`, following `IGitHubCommitRepository`.

`CompanyId` is strictly derivable from either FK. It earns its place by letting the resolver scope its read the same way as `GetByCompany` everywhere else, not by carrying new information.

Rows are never deleted. A merged branch keeps its rows; a soft-deleted branch keeps its rows.

### `TaskLink` gains `int? ViaGitHubBranchId` + nav

**The three unique indexes do not change.** `(TaskId, GitHubCommitId)` stays unique, so a commit that both names TS-1 *and* sits ahead on TS-1's branch produces **one** row — and which `ViaGitHubBranchId` it carries is decided purely by which resolver pass runs first.

One migration: new table plus the nullable column.

---

## The Sync Pass

`SyncCommitsAsync` gains a `string defaultBranch` parameter (from `repository.DefaultBranch`, already in scope in the `SyncCompanyAsync` loop) and one reordering: **the default branch is listed first**, its payload shas collected into a `HashSet<string>`, and every subsequent branch's payload differenced against it. Surviving shas get a `GitHubBranchCommit` row written on the same per-commit save the commit upsert already uses.

### Fail closed when the default listing is missing

If `repository.DefaultBranch` is empty, is absent from the branches list, or its `GET /commits` call fails, `defaultShas` is empty — and an empty subtrahend makes *every commit on every branch* ahead. That reaches the rejected "everything reachable" outcome by accident rather than by decision.

**No default listing → no join rows for that repository at all.** Commits still ingest exactly as today; only inheritance is skipped, and the repository reports a failure line naming the default branch.

The dangerous direction is failing open. The test must assert **zero join rows**, not merely that the sync passed.

### The default-branch match is case-insensitive

`GitHubBranches.Name` is `SQL_Latin1_General_CP1_CI_AS`. Matching `repository.DefaultBranch` against the payload ordinally is the same class of bug as the branch soft-delete defect of 2026-08-16. Use `StringComparer.OrdinalIgnoreCase`.

### The existing-commit path writes join rows too

Today the loop `continue`s when the sha is already in the mirror. But a commit ingested on a previous run is still ahead on a branch that has no join row yet — the first sync after a branch is created off older work. The loop body restructures so the commit's row id is resolved on **both** paths before the join row is upserted.

Skipping this makes inheritance work only for commits whose very first sighting was on the feature branch — invisible to any test that syncs once.

### Deliberate non-additions

- **The default branch gets no join rows of its own.** A commit on `main` is not ahead of `main`, so a task linked to the default branch inherits nothing. Correct, and tested, because it reads like an omission.
- **No new count on `SyncActivityResultDto`.** Inherited links land in the existing `LinksCreated`. A separate figure is real information that nothing in the UI asks for yet.

---

## The Resolver Pass

Inheritance is a **fourth pass** and is not a text scan — it never calls `TaskKeyScanner`. For each `TaskLink` with `GitHubBranchId` set, it reads that branch's `GitHubBranchCommit` rows and creates `(TaskId, commitId, ViaGitHubBranchId: branchId)`.

It lives in its own method, not a fourth optional parameter on `LinkAll`. That signature was already flagged as strained, and inheritance shares none of its text-scanning body.

`seen` and `unresolved` do not move — no keys were read. Only `created` increments.

### The `existing` tuple must NOT gain `ViaGitHubBranchId`

It is keyed `(TaskId, GitHubCommitId, GitHubBranchId, GitHubPullRequestId)`. Adding via to it would let a direct link and an inherited link for the same commit both look new — and they collide on `IX_TaskLinks_TaskId_CommitId`, throwing `DbUpdateException` out of the whole run.

Leaving the tuple alone is what makes direct-wins work: the inheritance pass simply fails to add a tuple the message pass already claimed.

**This is a case where the correct change is to change nothing, which is exactly the kind of line a later refactor deletes as redundant.** It needs a test that fails if the tuple is widened.

### The pass runs last

After commits, branches and pull requests, so direct links win precedence. The ordering is behavioural, invisible in the schema, and needs its own test rather than a comment.

### It must see branch links created in the same run

The branch pass adds `TaskLink`s to the change tracker, unsaved; a fresh query would not return them. The inheritance pass therefore works from the branch links the run accumulated in memory **plus** those already in the database.

Miss this and a newly-linked branch inherits nothing until the *next* sync — which no single-sync test can catch.

### Authorization

Holds by construction. The branch link was authorized through the repository-scoped key map, the join row is within that same repository, and `GitHubTaskActivityService` re-checks `ProjectRepositoryLinks` on every read regardless.

---

## Read Path and UI

`TaskCommitDto` gains `string? ViaBranchName`.

`GitHubTaskActivityService` already materializes `links`, and a via-branch always has its own `TaskLink` for the same task — inheritance only flows through a branch already linked. So the via-branch names are already in the `branches` list the method loads. Implementation is a `Dictionary<int, int?>` from commit id to via-branch id, resolved against that list. **No new query.**

Client: `viaBranchName?: string | null` on the commit model in `github-activity.models.ts`, and a marker in `task-github-activity.component.html` reading *via `feature/TS-1-login`*. Null renders nothing, so a directly-named commit looks exactly as it does today.

---

## Testing

TDD per task. The tests that carry weight, all in the dangerous direction:

| Test | Guards against |
|---|---|
| Default listing fails → **zero** join rows, commits still ingested | Failing open into full-history inheritance |
| Commit already in the mirror, newly ahead on a branch → join row written | The `continue` path silently skipping inheritance |
| Default branch itself → no join rows | Inheriting `main`'s history onto a task linked to `main` |
| `Main` vs `main` → matched | The 2026-08-16 collation class of bug |
| Commit both named and inherited → one row, `ViaGitHubBranchId` null | Precedence inverting on pass reorder |
| Widening the `existing` tuple → test fails | A refactor deleting a load-bearing non-change |
| Branch linked in the same run → inherits in that run | Inheritance lagging one sync behind |
| Two branches ahead of the same commit → both tasks inherit | The many-to-many the join table exists for |
| Branch merges into default → the inherited link is still there | Add-only being assumed rather than pinned |

---

## Out of Scope

- Closing the created-and-merged-between-syncs gap (needs a merge-triggered second inheritance path)
- Board-triggered commit refresh (cost; revisit with real staleness data)
- `GET /compare` for an untruncated ahead-set
- Any change to the three existing `TaskLink` unique indexes
- A separate `InheritedLinks` count in the sync summary
