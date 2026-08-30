# Merge → Done — Design Spec

**Date:** 2026-08-24
**Project:** TaskSphere
**Sub-project:** D (smart commits) — first slice
**Status:** Approved

---

## Goal

When a pull request whose **head branch** names a task key is merged, move that task to `Done` — once, accountably, and only where the project has opted in.

This is the first slice of sub-project D. It deliberately does **not** implement webhooks, commit-message commands, or any transition other than merge → `Done`.

---

## Why this is buildable now

Create-branch-from-task (completed and verified live 2026-08-24) means TaskSphere itself names branches `TS-42/crud-for-product`. The head branch of a PR opened from such a branch carries a well-formed, uppercase, scanner-visible key by construction. That is what makes head-branch resolution reliable rather than best-effort, and it is why the 2026-08-22 ordering put create-branch first.

---

## Scope Decisions

### The trigger is the existing manual sync

The transition runs as part of `GitHubActivitySyncService`'s pass. **No webhooks, no background poller.**

B2 webhooks are designed but never built, and TaskSphere is deployed local-only, so GitHub cannot reach it without a tunnel. Adding real-time delivery would make this feature the second half of a much larger piece of work.

**Accepted limitation — operational, not theoretical.** `SyncWindowDays = 30`, and the PR loop `break`s when `updatedAt < since` (`GitHubActivitySyncService.cs:377`). Sync at least once every 30 days and every merge is caught. Go longer and that PR is never fetched again: the mirror keeps it at its last-known state, the transition never fires, and there is **no catch-up path and no warning**. This must be stated in the user-facing docs and in the live-verification checklist.

### The key comes from the head branch, never the title or body

A PR that merely *mentions* `TS-42` in its description does not move `TS-42`. Only the head branch does.

**Consequence that drives the schema (see Marker below):** `TaskLink` rows for a pull request are produced by `GitHubTaskLinkResolver` from **title + body** text scanning. A PR on branch `TS-42/crud-for-product` whose title never says "TS-42" therefore has **no `TaskLink` row for that task**. The marker cannot live on `TaskLink`, because the row may not exist.

### Only `Open` and `InProgress` are moved

| Current status | Merged PR resolves to it | Result |
|---|---|---|
| `Open` | yes | → `Done` |
| `InProgress` | yes | → `Done` |
| `Blocked` | yes | **left alone** — a human deliberately flagged a problem, and a merge does not clear it |
| `Done` | yes | no-op |

In **every** one of these cases the marker is stamped, so a task skipped here is never revisited.

`Task.Status` is a free-form `string` on the entity but a closed set in practice: `TaskStatuses.Open | InProgress | Blocked | Done` (`TaskSphere.Domain/Enums/TicketStatus.cs`), enforced by `CreateTaskValidator` and `UpdateTaskValidator`. `Done` is therefore globally well-defined and needs no per-project column mapping.

---

## Data Layer

### `GitHubPullRequest.MergeTransitionAppliedAtUtc`

```csharp
public DateTime? MergeTransitionAppliedAtUtc { get; set; }
```

Nullable. `null` means "this PR has never been considered for a merge transition". Non-null means "considered — do not consider again", regardless of whether anything actually moved.

New EF Core migration required.

**Why a column and not a join table.** A head branch names one task in the overwhelming majority of cases, so a single column carries the fact. `TaskKeyScanner.Scan` can return several keys from one branch name (`TS-42-and-TS-43/foo`), which one column cannot record per-task — but the "stamp regardless" rule makes that coherent: all resolved tasks are processed in a single pass, then the column is stamped once. There is no state in which one key has been applied and another is still pending.

**Do not** reuse `GitHubUpdatedAtUtc` or any GitHub-sourced timestamp for this. The sync overwrites every GitHub-sourced field on every pass (`GitHubActivitySyncService.cs:412-424`); this column is TaskSphere's own and must survive that overwrite. The PR upsert must be changed to leave it untouched.

### `Project.AutoDoneOnMerge`

```csharp
public bool AutoDoneOnMerge { get; set; } = false;
```

Non-nullable, **default `false`** — opt-in per project.

New EF Core migration required (may share the migration with the column above).

---

## The transition is not derived from a state change

`GitHubActivitySyncService` **overwrites** pull-request state on every pass:

```csharp
existing.State = state;   // line 415
```

The previous value is gone, so "this PR *just became* merged" is not observable after the write. Rather than capture the edge, the design detects it through the marker:

> **`State == Merged` AND `MergeTransitionAppliedAtUtc IS NULL`** → this is a merge we have not acted on yet.

This collapses the 2026-08-22 analysis's separate "trigger" and "idempotency" hazards into one mechanism, and it is robust to the sync running any number of times, in any order, after any interruption.

---

## Components

### `IMergeTransitionService` (new)

Lives in `TaskSphere.Application/Interfaces`, implemented in `TaskSphere.Infrastructure/Services`.

A separate service rather than code inside `GitHubActivitySyncService`: that file is already ~450 lines, and this is a separable concern with its own tests and its own failure mode.

```csharp
Task<Result<MergeTransitionResult>> ApplyAsync(
    Guid companyId,
    string? actorUsername,
    CancellationToken cancellationToken = default);
```

Algorithm, company-scoped throughout:

1. Load pull requests where `State == Merged && MergeTransitionAppliedAtUtc == null`.
2. For each, scan `HeadBranch` with `TaskKeyScanner` → zero or more `TaskKey`s.
3. Resolve each key to a task **through the shared resolution unit** (below).
4. Load `AutoDoneOnMerge` for the resolved tasks' projects. If `false` → no status write.
5. If `true` and status is `Open` or `InProgress` → set `Done`, enqueue one audit entry.
6. Stamp `MergeTransitionAppliedAtUtc = DateTime.UtcNow` on the pull request **unconditionally**.
7. `SaveChangesAsync` **per pull request** (see Failure below).

A pull request whose head branch yields **no keys** (a hand-made branch like `hotfix/login`) moves nothing and is stamped like any other — it is never reconsidered, which is correct: its branch name cannot change retroactively.

`Result<T>` is failed only when the pass cannot run at all (e.g. the company has no installation). A pass that runs and has individual pull requests fail returns **success** carrying non-zero `Failed` — partial failure is a count, not an error, matching how repository sync already reports.

Ordering: must run **after** `SyncPullRequestsAsync` so `State` is current. It has **no dependency on `GitHubTaskLinkResolver`** and does not read `TaskLink` — it may run before or after the resolver.

### Shared task resolution (refactor — required, not opportunistic)

`GitHubTaskLinkResolver` currently holds the three-step resolution as a local function:

```csharp
int? ResolveTask(TaskKey key, int gitHubRepositoryId)
{
    if (!projectsByKey.TryGetValue(key.ProjectKey, out var projectId)) return null;
    if (!authorized.Contains((projectId, gitHubRepositoryId))) return null;
    return taskIdByProjectAndNumber.TryGetValue((projectId, key.Number), out var id) ? id : null;
}
```

Step 2 is **the authorization boundary**: without it, push access to any repository under the installation is enough to attach activity to — or now, *change the status of* — any project's tasks.

Hand-rolling this a second time in `MergeTransitionService` would place the authorization boundary in two files that will drift, and the drift's failure mode is cross-project task writes. Extract it into a single unit both services call, with its own tests.

The extraction must preserve the existing behaviour exactly; `GitHubTaskLinkResolver`'s tests must pass unchanged before any new behaviour is added.

### Actor plumbing

The syncing username is passed **down as an explicit parameter**: `GitHubController` → `IGitHubActivitySyncService.SyncAsync` → `IMergeTransitionService.ApplyAsync`.

**Not** by injecting `IHttpContextAccessor` into Infrastructure. That layer has no HTTP dependency today and must not acquire one.

### `PATCH /api/projects/{projectId}/settings` (new endpoint)

Projects are currently **immutable after creation** — `ProjectsController` exposes create, get, get-by-id, get-members, add-member, remove-member, and nothing else. This endpoint is new.

```csharp
[Audit("Changed project settings")]
[Authorize(Roles = Roles.Company)]
[HttpPatch("{projectId:int}/settings")]
```

`Company`-gated, matching how `Create` is gated.

```csharp
public record UpdateProjectSettingsDto(bool AutoDoneOnMerge);
```

**The DTO carries only `AutoDoneOnMerge`.** It is deliberately *not* a general project-update shape. `Project.Key` is uppercase-and-load-bearing: changing it orphans every existing task key and silently breaks `TaskKeyScanner`. Adding a settings endpoint must not become a back door to editing it.

`ProjectDto` gains `AutoDoneOnMerge` so the client can render current state.

**Checked, and the expected trap does not apply.** `ProjectDto` is a positional record, and AutoMapper's constructor mapping on positional records is a recorded failure mode on this project — but `ProjectDto` has **no AutoMapper profile at all**. It is hand-projected in four places (`ProjectService.cs:69,79,92` and `AccessControlService.cs:64`), so widening it is a compile-time break at each site and nothing more. No mapping configuration to update, and none should be added.

### Client

A checkbox on the existing admin screen `client/src/app/company-dashboard/projects/project-page.component`. No new screen.

The sync button's result gains a count so the transition is not invisible: "3 tasks moved to Done".

---

## Consequence: the marker is stamped even when the toggle is off

Enabling `AutoDoneOnMerge` on a project applies only to **future** merges. It does not retroactively sweep the 30-day window.

This is deliberate. The alternative — leaving the marker null while disabled — means ticking a checkbox silently mass-moves every task whose PR merged in the last 30 days. A predictable inert toggle is better than an unpredictable flood.

The cost: enabling the toggle feels like nothing happened until the next merge. The live-verification checklist must therefore **enable the toggle first, then merge**, or it will read as a broken feature.

---

## Failure Handling

**The partial-failure unit is one pull request.** `SaveChangesAsync` runs per pull request, not once at the end.

The reported unit must equal the persisted unit. The 2026-08-19 session reversed exactly this mistake in the sync loop — adds were change-tracker-only with the flush after the loop, so the sync reported zero commits for repositories whose commits were already in SQL Server. Repeating that shape here would let one bad task discard every earlier transition in the same pass.

A pull request that throws is counted as failed, does not stamp its marker, and does not stop the pass.

`MergeTransitionResult` carries `Transitioned`, `Skipped`, and `Failed` counts, surfaced through the existing sync result.

---

## Audit

One entry per **actual** transition — not per PR considered, not per skip.

`AuditQueue` and `AuditEntry` live in `TaskSphere.Domain/Audit`, which Infrastructure already references. No layering violation; the service enqueues directly.

The entry is **HTTP-shaped and will be partly empty**: `HttpMethod`, `Path`, `Ip`, `UserAgent`, `StatusCode`, and `DurationMs` have no meaning for a sync-driven transition.

**The plan must verify the audit dashboard renders a row with those fields blank rather than throwing.** That UI already carries one live defect on record; this is the first non-HTTP entry it will ever receive.

- `Username` = the syncing user (a real, authenticated, accountable actor)
- `Action` = a description naming the task, the PR number, and the cause
- `CompanyId` = the synced company

---

## Testing

TDD throughout, per project convention. Beyond the happy path, these tests carry the design:

1. **The dangerous direction.** A PR in a repository linked to project A, head branch naming `TSB-42` from project B → **no status write**. Cross-project task writes are the worst failure this feature can produce, and the happy-path test cannot catch it.
2. **Idempotency.** Sync twice → exactly one transition, exactly one audit entry.
3. **The human override — the test that justifies the marker.** Merge → `Done` → a human sets `InProgress` → sync again → **stays `InProgress`**.
4. **Guard and marker interaction.** A `Blocked` task is not moved, the marker *is* stamped, and a later sync does not revisit it even after someone unblocks it.
5. **Toggle off.** No status write, marker stamped, count reports zero — and a subsequent sync after enabling the toggle still does nothing for that PR.
6. **Multi-key branch.** A head branch naming two keys transitions both, then stamps once.
7. **The sync overwrite does not clear the marker.** Run the PR upsert over an already-stamped PR and assert `MergeTransitionAppliedAtUtc` survives.
8. **Partial failure.** One failing pull request does not discard transitions already persisted in the same pass.

**Fixture requirement — mandatory.** Projects, tasks, repositories and pull requests must be seeded with **different identity values**. A fixture that migrates a fresh database gives every table the same identity seeds, so a lookup passing the wrong entity's id resolves correctly by accident — the trap that survived nineteen tests on create-branch-from-task. Use decoy rows, not additional assertions.

**Mutation sweep** over the finished feature, independently dispatched (measurer distinct from implementer), per established practice on this branch. Note that a survivor is evidence about the tests, not about the app — severity claims require a live run to confirm.

---

## Out of Scope

- Webhooks (B2) and any real-time trigger
- Commit-message commands (`fixes TS-42`) — a separate sub-project D slice
- Any transition other than merge → `Done` (closed-without-merge, review-requested, etc.)
- Reverting a task when a merge is reverted
- Branch → commit inheritance (the third 2026-08-22 candidate; unrelated and larger)
- A general project-update endpoint

---

## Live Verification

Cannot be confirmed by tests. After implementation:

1. **Enable `AutoDoneOnMerge`** on the test project — the feature is inert until this is done.
2. Create a branch from a task via the existing button (`TS-42/...`).
3. Open a PR from that branch; confirm the task does **not** move.
4. Merge the PR.
5. Press **Sync**; confirm the task moves to `Done` and the count reports it.
6. Press **Sync** again; confirm nothing changes and no second audit entry appears.
7. Move the task back to `InProgress` by hand; sync; confirm it **stays** `InProgress`.
8. Confirm the audit dashboard renders the transition entry without error.
9. Repeat 2-5 against a `Blocked` task; confirm it is not moved and is not revisited.
